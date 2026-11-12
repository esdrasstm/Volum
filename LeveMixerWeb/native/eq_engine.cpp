#include "eq_engine.h"

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <xmmintrin.h>

#include <algorithm>
#include <cmath>
#include <cstring>

// Motor do equalizador do Volum.
//
// Cada banda é um ou mais estágios SVF (Simper, "Linear Trapezoidal Integrated SVF"). A resposta é
// idêntica à dos filtros do "Audio EQ Cookbook" (RBJ), que é o que o AutoEq e o Equalizer APO usam,
// então um preset importado soa igual. A diferença é o comportamento com o som tocando: o SVF
// aceita parâmetros mudando a cada bloco sem estalo nem instabilidade, coisa que o biquad em forma
// direta não garante.
//
// Garantias para o uso em tempo real:
//  - nenhuma alocação, trava ou chamada de sistema dentro de process();
//  - latência zero (IIR, processa no lugar, sem lookahead);
//  - parâmetros suavizados (~12 ms), liga/desliga e troca de tipo com transição de ~10 ms;
//  - entrada NaN/Inf vira silêncio e estado estragado é zerado, então o filtro nunca "trava";
//  - denormais desligados durante o processamento (FTZ/DAZ), com o MXCSR restaurado na saída.

namespace {

constexpr double kPi = 3.14159265358979323846;
constexpr double kSmoothSec = 0.012;
constexpr double kFadeSec = 0.010;
constexpr double kLn10Over20 = 0.11512925464970229;   // ln(10)/20

bool finite_or_zero(double& x) {
    if (std::isfinite(x)) return true;
    x = 0.0;
    return false;
}

double clamp(double v, double lo, double hi, double fallback) {
    if (!std::isfinite(v)) return fallback;
    return v < lo ? lo : (v > hi ? hi : v);
}

bool is_cut(int type) { return type == VOLUM_EQ_HIGHPASS || type == VOLUM_EQ_LOWPASS; }

double max_freq_for(double rate) {
    double nyq = rate * 0.49;
    return nyq < VOLUM_EQ_MAX_FREQ ? nyq : VOLUM_EQ_MAX_FREQ;
}

void finish_stage(EqStage& s) {
    s.a1 = 1.0 / (1.0 + s.g * (s.g + s.k));
    s.a2 = s.g * s.a1;
    s.a3 = s.g * s.a2;
}

// "{0.0.0.00000000}.{guid}" (id do IMMDevice) e "{guid}" (PKEY_AudioEndpoint_GUID) são o mesmo dispositivo.
const wchar_t* endpoint_tail(const wchar_t* id) {
    const wchar_t* dot = wcsrchr(id, L'.');
    return dot ? dot + 1 : id;
}

bool same_id(const wchar_t* a, const wchar_t* b) {
    if (!a || !b || !a[0] || !b[0]) return false;
    return CompareStringOrdinal(endpoint_tail(a), -1, endpoint_tail(b), -1, TRUE) == CSTR_EQUAL;
}

} // namespace

// ---------------------------------------------------------------------------------------------
// Troca de parâmetros

EqParamBox::EqParamBox() : m_middle(1) {
    std::memset(m_buf, 0, sizeof(m_buf));
    for (auto& b : m_buf) b.bypass = 1;
}

void EqParamBox::publish(const VolumEqParams& params) {
    m_buf[m_back] = params;
    uint32_t old = m_middle.exchange(m_back | 4u, std::memory_order_acq_rel);
    m_back = old & 3u;
}

bool EqParamBox::take(VolumEqParams* out) {
    if ((m_middle.load(std::memory_order_acquire) & 4u) == 0) return false;
    uint32_t old = m_middle.exchange(m_front, std::memory_order_acq_rel);
    m_front = old & 3u;
    *out = m_buf[m_front];
    return true;
}

// ---------------------------------------------------------------------------------------------
// Desenho dos filtros

void EqEngine::sanitize(VolumEqParams* p) {
    if (!p) return;
    p->bypass = p->bypass ? 1 : 0;
    if (p->band_count < 0) p->band_count = 0;
    if (p->band_count > VOLUM_EQ_MAX_BANDS) p->band_count = VOLUM_EQ_MAX_BANDS;
    p->preamp_db = clamp(p->preamp_db, VOLUM_EQ_MIN_PREAMP, VOLUM_EQ_MAX_PREAMP, 0.0);
    for (int i = 0; i < VOLUM_EQ_MAX_BANDS; i++) {
        VolumEqBand& b = p->bands[i];
        if (b.type < VOLUM_EQ_OFF || b.type > VOLUM_EQ_TYPE_LAST) b.type = VOLUM_EQ_OFF;
        b.enabled = b.enabled ? 1 : 0;
        if (b.slope < 1) b.slope = 1;
        if (b.slope > VOLUM_EQ_MAX_SLOPE) b.slope = VOLUM_EQ_MAX_SLOPE;
        b.reserved = 0;
        b.freq_hz = clamp(b.freq_hz, VOLUM_EQ_MIN_FREQ, VOLUM_EQ_MAX_FREQ, 1000.0);
        b.gain_db = clamp(b.gain_db, -VOLUM_EQ_MAX_GAIN, VOLUM_EQ_MAX_GAIN, 0.0);
        b.q = clamp(b.q, VOLUM_EQ_MIN_Q, VOLUM_EQ_MAX_Q, 0.7071);
    }
}

bool EqEngine::design(const VolumEqBand& band, double rate, EqDesign* d) {
    d->count = 0;
    d->neutral = 0;
    if (!band.enabled || band.type <= VOLUM_EQ_OFF || band.type > VOLUM_EQ_TYPE_LAST) return false;
    if (!(rate >= 8000.0)) rate = 48000.0;

    const double f = clamp(band.freq_hz, VOLUM_EQ_MIN_FREQ, max_freq_for(rate), 1000.0);
    const double q = clamp(band.q, VOLUM_EQ_MIN_Q, VOLUM_EQ_MAX_Q, 0.7071);
    const double gain = clamp(band.gain_db, -VOLUM_EQ_MAX_GAIN, VOLUM_EQ_MAX_GAIN, 0.0);
    const double w = std::tan(kPi * f / rate);

    if (band.type == VOLUM_EQ_BELL || band.type == VOLUM_EQ_LOWSHELF || band.type == VOLUM_EQ_HIGHSHELF) {
        const double A = std::pow(10.0, gain / 40.0);
        EqStage& s = d->stage[0];
        switch (band.type) {
        case VOLUM_EQ_BELL:
            s.g = w;
            s.k = 1.0 / (q * A);
            s.m0 = 1.0;
            s.m1 = s.k * (A * A - 1.0);
            s.m2 = 0.0;
            break;
        case VOLUM_EQ_LOWSHELF:
            s.g = w / std::sqrt(A);
            s.k = 1.0 / q;
            s.m0 = 1.0;
            s.m1 = s.k * (A - 1.0);
            s.m2 = A * A - 1.0;
            break;
        default:
            s.g = w * std::sqrt(A);
            s.k = 1.0 / q;
            s.m0 = A * A;
            s.m1 = s.k * (1.0 - A) * A;
            s.m2 = 1.0 - A * A;
            break;
        }
        finish_stage(s);
        d->count = 1;
        d->neutral = std::fabs(gain) < 1e-9 ? 1 : 0;   // 0 dB: os coeficientes viram identidade
        return d->neutral == 0;
    }

    if (band.type == VOLUM_EQ_NOTCH) {
        EqStage& s = d->stage[0];
        s.g = w;
        s.k = 1.0 / q;
        s.m0 = 1.0;
        s.m1 = -s.k;
        s.m2 = 0.0;
        finish_stage(s);
        d->count = 1;
        return true;
    }

    // Corte (passa-altas / passa-baixas): Butterworth de ordem 2n em n estágios.
    // O Q da banda vira a ressonância do estágio mais agudo (0,707 = Butterworth puro).
    int n = band.slope < 1 ? 1 : (band.slope > VOLUM_EQ_MAX_SLOPE ? VOLUM_EQ_MAX_SLOPE : band.slope);
    const int order = 2 * n;
    for (int i = 0; i < n; i++) {
        double qs = 1.0 / (2.0 * std::sin(kPi * (2.0 * (i + 1) - 1.0) / (2.0 * order)));
        if (i == 0) qs *= q / 0.70710678118654752;
        EqStage& s = d->stage[i];
        s.g = w;
        s.k = 1.0 / qs;
        if (band.type == VOLUM_EQ_HIGHPASS) {
            s.m0 = 1.0;
            s.m1 = -s.k;
            s.m2 = -1.0;
        } else {
            s.m0 = 0.0;
            s.m1 = 0.0;
            s.m2 = 1.0;
        }
        finish_stage(s);
    }
    d->count = n;
    return true;
}

// Resposta exata do filtro digital: a transformação bilinear leva a frequência digital f para a
// frequência analógica tan(pi f / fs), então basta avaliar o protótipo analógico de cada estágio.
double EqEngine::design_db(const EqDesign& d, double rate, double freq_hz) {
    if (d.count <= 0) return 0.0;
    if (!(rate >= 8000.0)) rate = 48000.0;
    double f = freq_hz;
    if (!(f > 0.0)) f = 1.0;
    if (f > rate * 0.4999) f = rate * 0.4999;
    const double t = std::tan(kPi * f / rate);
    double db = 0.0;
    for (int i = 0; i < d.count; i++) {
        const EqStage& s = d.stage[i];
        const double W = t / s.g;
        const double dr = 1.0 - W * W, di = s.k * W;
        const double nr = s.m0 * dr + s.m2, ni = s.m0 * di + s.m1 * W;
        const double den = dr * dr + di * di;
        const double num = nr * nr + ni * ni;
        if (den > 0.0 && num > 1e-30) db += 10.0 * std::log10(num / den);
        else if (num <= 1e-30) db += -300.0;
    }
    return db;
}

double EqEngine::magnitude_db(const VolumEqParams& in, double rate, double freq_hz) {
    VolumEqParams p = in;
    sanitize(&p);
    double db = p.preamp_db;
    for (int i = 0; i < p.band_count; i++) {
        EqDesign d;
        if (design(p.bands[i], rate, &d)) db += design_db(d, rate, freq_hz);
    }
    return db;
}

// ---------------------------------------------------------------------------------------------
// Processamento

EqEngine::EqEngine() {
    std::memset(&m_target, 0, sizeof(m_target));
    m_target.bypass = 1;
    sanitize(&m_target);
}

void EqEngine::prepare(int channels, double sample_rate) {
    if (channels < 1) channels = 1;
    if (!(sample_rate >= 8000.0) || sample_rate > 768000.0) sample_rate = 48000.0;
    m_channels = channels;
    m_filtered = channels < kMaxChannels ? channels : kMaxChannels;
    m_rate = sample_rate;
    m_smooth = 1.0 - std::exp(-static_cast<double>(kBlock) / (kSmoothSec * sample_rate));
    m_fade_step = static_cast<double>(kBlock) / (kFadeSec * sample_rate);
    if (m_fade_step > 1.0) m_fade_step = 1.0;
    for (auto& b : m_bands) b.dirty = true;
    reset();
    m_primed = false;
    m_settled = false;
    m_peak.store(0.f, std::memory_order_relaxed);
}

void EqEngine::reset() {
    std::memset(m_state, 0, sizeof(m_state));
}

void EqEngine::clear_band(int index) {
    std::memset(m_state[index], 0, sizeof(m_state[index]));
}

void EqEngine::redesign(Band& b) {
    VolumEqBand band = {};
    band.type = b.type;
    band.enabled = b.type != VOLUM_EQ_OFF;
    band.slope = b.slope;
    band.freq_hz = std::exp(b.log_freq);
    band.gain_db = b.gain;
    band.q = std::exp(b.log_q);
    design(band, m_rate, &b.design);
    b.dirty = false;
}

// Leva em conta o bloco que acabou de ser planejado (início e fim de cada rampa), então a última
// fatia de uma transição ainda é processada antes de o sinal passar direto.
bool EqEngine::idle() const {
    if (!m_primed) return true;
    if (!m_settled) return false;
    if (m_wet0 <= 0.0 && m_wet1 <= 0.0) return true;
    if (m_gain0 != 1.0 || m_gain1 != 1.0) return false;
    for (int i = 0; i < VOLUM_EQ_MAX_BANDS; i++)
        if ((m_amount0[i] > 0.0 || m_amount1[i] > 0.0) && m_bands[i].audible()) return false;
    return true;
}

void EqEngine::control_step(bool first) {
    VolumEqParams fresh;
    if (m_box.take(&fresh)) {
        sanitize(&fresh);
        m_target = fresh;
        m_settled = false;
    }

    bool settled = true;
    for (int i = 0; i < VOLUM_EQ_MAX_BANDS; i++) {
        Band& b = m_bands[i];
        const VolumEqBand& t = m_target.bands[i];
        const bool want = i < m_target.band_count && t.enabled && t.type != VOLUM_EQ_OFF;
        const int slope = is_cut(t.type) ? t.slope : 1;
        const double lf = std::log(clamp(t.freq_hz, VOLUM_EQ_MIN_FREQ, max_freq_for(m_rate), 1000.0));
        const double lq = std::log(t.q);
        const double before = b.amount;
        bool adopted = false;

        bool same = b.type == t.type && b.slope == slope;
        if (first || (want && !same && b.amount <= 0.0)) {
            // A banda está fora do caminho (ou é o começo do áudio): assume o tipo e os valores direto.
            b.type = want ? t.type : VOLUM_EQ_OFF;
            b.slope = slope;
            b.log_freq = lf;
            b.gain = t.gain_db;
            b.log_q = lq;
            b.dirty = true;
            clear_band(i);
            same = true;
            adopted = true;
            if (first) b.amount = want ? 1.0 : 0.0;
        }

        const double goal = (want && same) ? 1.0 : 0.0;
        if (b.amount < goal) b.amount = std::min(goal, b.amount + m_fade_step);
        else if (b.amount > goal) b.amount = std::max(goal, b.amount - m_fade_step);
        if (b.amount != goal) settled = false;

        if (same && b.type != VOLUM_EQ_OFF) {
            const double df = lf - b.log_freq, dg = t.gain_db - b.gain, dq = lq - b.log_q;
            if (df != 0.0 || dg != 0.0 || dq != 0.0) {
                if (std::fabs(df) < 1e-5 && std::fabs(dg) < 1e-4 && std::fabs(dq) < 1e-5) {
                    b.log_freq = lf;
                    b.gain = t.gain_db;
                    b.log_q = lq;
                } else {
                    b.log_freq += df * m_smooth;
                    b.gain += dg * m_smooth;
                    b.log_q += dq * m_smooth;
                    settled = false;
                }
                b.dirty = true;
            }
        }

        if (b.amount <= 0.0 && before <= 0.0 && b.live) {   // saiu do caminho: zera para voltar limpo
            clear_band(i);
            b.live = false;
        }
        if (b.amount <= 0.0 && !want) b.type = VOLUM_EQ_OFF;
        if (b.dirty) {
            b.prev = b.design;
            redesign(b);
            b.ramp = !adopted && b.prev.count == b.design.count && b.prev.count > 0;
        } else {
            b.ramp = false;
        }
        if (b.design.neutral && !b.ramp && b.live) {   // voltou a 0 dB: deixa de processar
            clear_band(i);
            b.live = false;
        }
        m_amount0[i] = first ? b.amount : before;
        m_amount1[i] = b.amount;
    }

    const double gain_goal = std::exp(m_target.preamp_db * kLn10Over20);
    m_gain0 = m_gain;
    if (first) m_gain = gain_goal;
    else if (m_gain != gain_goal) {
        double next = m_gain + (gain_goal - m_gain) * m_smooth;
        if (std::fabs(next - gain_goal) < 1e-6) next = gain_goal;
        else settled = false;
        m_gain = next;
    }
    m_gain1 = m_gain;
    if (first) m_gain0 = m_gain;

    const double wet_goal = m_target.bypass ? 0.0 : 1.0;
    m_wet0 = m_wet;
    if (first) m_wet = wet_goal;
    else if (m_wet < wet_goal) m_wet = std::min(wet_goal, m_wet + m_fade_step);
    else if (m_wet > wet_goal) m_wet = std::max(wet_goal, m_wet - m_fade_step);
    if (m_wet != wet_goal) settled = false;
    if (first) m_wet0 = m_wet;
    m_wet1 = m_wet;
    if (m_wet0 <= 0.0 && m_wet1 <= 0.0 && m_live) {   // terminou de desligar: zera tudo
        reset();
        m_live = false;
        for (Band& b : m_bands) b.live = false;
    }

    m_settled = settled;
    if (first) m_primed = true;
}

void EqEngine::process(const float* in, float* out, int frames) {
    if (!in || !out || frames <= 0 || m_channels <= 0) return;
    m_frames.fetch_add(static_cast<uint64_t>(frames), std::memory_order_relaxed);

    const unsigned int csr = _mm_getcsr();
    _mm_setcsr(csr | 0x8040);   // FTZ + DAZ

    if (!m_primed) control_step(true);

    const int ch = m_channels;
    const int filt = m_filtered;
    float peak = 0.f;
    uint32_t clips = 0;
    double dry[kMaxChannels][kBlock];
    double wet[kMaxChannels][kBlock];
    double work[kBlock];

    for (int done = 0; done < frames;) {
        const int n = std::min(kBlock, frames - done);
        control_step(false);
        const float* src = in + static_cast<size_t>(done) * ch;
        float* dst = out + static_cast<size_t>(done) * ch;

        if (idle()) {
            if (src != dst) std::memcpy(dst, src, sizeof(float) * static_cast<size_t>(n) * ch);
            for (int i = 0; i < n * ch; i++) {
                float a = std::fabs(dst[i]);
                if (a > peak) peak = a;
            }
            done += n;
            continue;
        }

        for (int f = 0; f < n; f++) {
            for (int c = 0; c < filt; c++) {
                double x = src[static_cast<size_t>(f) * ch + c];
                finite_or_zero(x);
                dry[c][f] = x;
                wet[c][f] = x;
            }
        }

        const double inv_n = 1.0 / n;
        for (int i = 0; i < VOLUM_EQ_MAX_BANDS; i++) {
            const Band& b = m_bands[i];
            const double a0 = m_amount0[i], a1 = m_amount1[i];
            if ((a0 <= 0.0 && a1 <= 0.0) || !b.audible()) continue;
            const bool full = a0 >= 1.0 && a1 >= 1.0;
            m_bands[i].live = true;
            m_live = true;
            for (int c = 0; c < filt; c++) {
                double* x = wet[c];
                if (!full) std::memcpy(work, x, sizeof(double) * n);
                for (int s = 0; s < b.design.count; s++) {
                    const EqStage& st = b.design.stage[s];
                    double ic1 = m_state[i][s][c][0];
                    double ic2 = m_state[i][s][c][1];
                    if (!b.ramp) {
                        for (int f = 0; f < n; f++) {
                            const double v0 = x[f];
                            const double v3 = v0 - ic2;
                            const double v1 = st.a1 * ic1 + st.a2 * v3;
                            const double v2 = ic2 + st.a2 * ic1 + st.a3 * v3;
                            ic1 = 2.0 * v1 - ic1;
                            ic2 = 2.0 * v2 - ic2;
                            x[f] = st.m0 * v0 + st.m1 * v1 + st.m2 * v2;
                        }
                    } else {
                        // Parâmetro andando: coeficientes interpolados amostra a amostra (sem "zipper").
                        const EqStage& p = b.prev.stage[s];
                        const double d1 = st.a1 - p.a1, d2 = st.a2 - p.a2, d3 = st.a3 - p.a3;
                        const double e0 = st.m0 - p.m0, e1 = st.m1 - p.m1, e2 = st.m2 - p.m2;
                        for (int f = 0; f < n; f++) {
                            const double t = (f + 1) * inv_n;
                            const double ca1 = p.a1 + d1 * t, ca2 = p.a2 + d2 * t, ca3 = p.a3 + d3 * t;
                            const double v0 = x[f];
                            const double v3 = v0 - ic2;
                            const double v1 = ca1 * ic1 + ca2 * v3;
                            const double v2 = ic2 + ca2 * ic1 + ca3 * v3;
                            ic1 = 2.0 * v1 - ic1;
                            ic2 = 2.0 * v2 - ic2;
                            x[f] = (p.m0 + e0 * t) * v0 + (p.m1 + e1 * t) * v1 + (p.m2 + e2 * t) * v2;
                        }
                    }
                    if (!std::isfinite(ic1) || !std::isfinite(ic2)) {
                        ic1 = ic2 = 0.0;
                        for (int f = 0; f < n; f++) x[f] = 0.0;
                    }
                    m_state[i][s][c][0] = ic1;
                    m_state[i][s][c][1] = ic2;
                }
                if (!full) {
                    // Transição: mistura linear entre o sinal sem a banda e com a banda.
                    for (int f = 0; f < n; f++) {
                        const double amt = a0 + (a1 - a0) * (f + 1) * inv_n;
                        x[f] = work[f] + amt * (x[f] - work[f]);
                    }
                }
            }
        }

        const double g0 = m_gain0, dg = m_gain1 - m_gain0;
        const double w0 = m_wet0, dw = m_wet1 - m_wet0;
        for (int f = 0; f < n; f++) {
            const double k = (f + 1) * inv_n;
            const double g = g0 + dg * k;
            const double w = w0 + dw * k;
            float* frame = dst + static_cast<size_t>(f) * ch;
            for (int c = 0; c < filt; c++) {
                double y = dry[c][f] + w * (wet[c][f] * g - dry[c][f]);
                if (!std::isfinite(y)) y = 0.0;
                const float o = static_cast<float>(y);
                const float a = std::fabs(o);
                if (a > peak) peak = a;
                if (a > 1.0f) clips++;
                frame[c] = o;
            }
            if (ch > filt && src != dst) {
                const float* sf = src + static_cast<size_t>(f) * ch;
                for (int c = filt; c < ch; c++) frame[c] = sf[c];
            }
        }
        done += n;
    }

    float prev = m_peak.load(std::memory_order_relaxed);
    while (peak > prev && !m_peak.compare_exchange_weak(prev, peak, std::memory_order_relaxed)) {}
    if (clips) m_clips.fetch_add(clips, std::memory_order_relaxed);
    _mm_setcsr(csr);
}

// ---------------------------------------------------------------------------------------------
// eq.bin

int EqEngine::read_file(const wchar_t* path, const wchar_t* endpoint_id, VolumEqParams* out) {
    if (!out || !path) return -2;

    HANDLE file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        DWORD err = GetLastError();
        return (err == ERROR_FILE_NOT_FOUND || err == ERROR_PATH_NOT_FOUND) ? -1 : -2;
    }

    constexpr DWORD kMax = VOLUM_EQ_HEADER_BYTES + VOLUM_EQ_MAX_DEVICES * VOLUM_EQ_RECORD_BYTES;
    static_assert(kMax < 32768, "eq.bin cabe na pilha");
    unsigned char data[kMax + 1];
    DWORD got = 0;
    BOOL ok = ReadFile(file, data, sizeof(data), &got, nullptr);
    CloseHandle(file);
    if (!ok || got < VOLUM_EQ_HEADER_BYTES || got > kMax) return -2;

    uint32_t version = 0, count = 0, record = 0;
    std::memcpy(&version, data + 8, 4);
    std::memcpy(&count, data + 12, 4);
    std::memcpy(&record, data + 16, 4);
    if (std::memcmp(data, VOLUM_EQ_FILE_MAGIC, 8) != 0 || version != VOLUM_EQ_FILE_VERSION
        || record != VOLUM_EQ_RECORD_BYTES || count > VOLUM_EQ_MAX_DEVICES
        || got != VOLUM_EQ_HEADER_BYTES + count * VOLUM_EQ_RECORD_BYTES)
        return -2;

    int chosen = -1, installed = -1, installed_count = 0;
    wchar_t id[VOLUM_EQ_ID_CHARS];
    VolumEqParams params;
    for (uint32_t i = 0; i < count; i++) {
        const unsigned char* rec = data + VOLUM_EQ_HEADER_BYTES + i * VOLUM_EQ_RECORD_BYTES;
        std::memcpy(id, rec, sizeof(id));
        id[VOLUM_EQ_ID_CHARS - 1] = 0;
        std::memcpy(&params, rec + sizeof(id), sizeof(params));
        if (same_id(id, endpoint_id)) chosen = static_cast<int>(i);
        if (params.flags & VOLUM_EQ_FLAG_INSTALLED) {
            installed = static_cast<int>(i);
            installed_count++;
        }
    }
    // Sem o id do endpoint (propriedade apagada por um driver), vale o único dispositivo ativado.
    if (chosen < 0 && (!endpoint_id || !endpoint_id[0]) && installed_count == 1) chosen = installed;
    if (chosen < 0) return 0;

    std::memcpy(out, data + VOLUM_EQ_HEADER_BYTES + chosen * VOLUM_EQ_RECORD_BYTES + VOLUM_EQ_ID_CHARS * sizeof(wchar_t), sizeof(VolumEqParams));
    sanitize(out);
    return 1;
}

static_assert(sizeof(VolumEqBand) == 40, "VolumEqBand");
static_assert(sizeof(VolumEqParams) == 1304, "VolumEqParams");
static_assert(VOLUM_EQ_ID_CHARS * sizeof(wchar_t) + sizeof(VolumEqParams) == VOLUM_EQ_RECORD_BYTES, "registro do eq.bin");
