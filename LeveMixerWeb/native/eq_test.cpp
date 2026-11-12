#include "eq_engine.h"

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <complex>
#include <cstdio>
#include <cstring>
#include <limits>
#include <thread>
#include <vector>

// Testes do motor do equalizador. O build para se qualquer um falhar.

namespace {

int g_fails = 0;
int g_checks = 0;

void check(bool ok, const char* text, int line) {
    g_checks++;
    if (ok) return;
    std::printf("FALHOU (linha %d): %s\n", line, text);
    g_fails++;
}
#define CHECK(cond) check((cond), #cond, __LINE__)

constexpr double kPi = 3.14159265358979323846;

VolumEqBand band(int type, double freq, double gain, double q, int slope = 1) {
    VolumEqBand b = {};
    b.type = type;
    b.enabled = 1;
    b.slope = slope;
    b.freq_hz = freq;
    b.gain_db = gain;
    b.q = q;
    return b;
}

VolumEqParams one(const VolumEqBand& b, double preamp = 0) {
    VolumEqParams p = {};
    p.band_count = 1;
    p.bands[0] = b;
    p.preamp_db = preamp;
    EqEngine::sanitize(&p);
    return p;
}

// Referência: "Audio EQ Cookbook" (Robert Bristow-Johnson), a mesma conta do AutoEq e do Equalizer APO.
double rbj_db(const VolumEqBand& b, double fs, double f) {
    const double w0 = 2 * kPi * b.freq_hz / fs, cw = std::cos(w0), sw = std::sin(w0);
    const double A = std::pow(10.0, b.gain_db / 40.0), alpha = sw / (2 * b.q);
    double b0, b1, b2, a0, a1, a2;
    switch (b.type) {
    case VOLUM_EQ_BELL:
        b0 = 1 + alpha * A; b1 = -2 * cw; b2 = 1 - alpha * A; a0 = 1 + alpha / A; a1 = -2 * cw; a2 = 1 - alpha / A; break;
    case VOLUM_EQ_LOWSHELF: {
        const double s = 2 * std::sqrt(A) * alpha;
        b0 = A * ((A + 1) - (A - 1) * cw + s); b1 = 2 * A * ((A - 1) - (A + 1) * cw); b2 = A * ((A + 1) - (A - 1) * cw - s);
        a0 = (A + 1) + (A - 1) * cw + s; a1 = -2 * ((A - 1) + (A + 1) * cw); a2 = (A + 1) + (A - 1) * cw - s; break;
    }
    case VOLUM_EQ_HIGHSHELF: {
        const double s = 2 * std::sqrt(A) * alpha;
        b0 = A * ((A + 1) + (A - 1) * cw + s); b1 = -2 * A * ((A - 1) + (A + 1) * cw); b2 = A * ((A + 1) + (A - 1) * cw - s);
        a0 = (A + 1) - (A - 1) * cw + s; a1 = 2 * ((A - 1) - (A + 1) * cw); a2 = (A + 1) - (A - 1) * cw - s; break;
    }
    case VOLUM_EQ_HIGHPASS:
        b0 = (1 + cw) / 2; b1 = -(1 + cw); b2 = (1 + cw) / 2; a0 = 1 + alpha; a1 = -2 * cw; a2 = 1 - alpha; break;
    case VOLUM_EQ_LOWPASS:
        b0 = (1 - cw) / 2; b1 = 1 - cw; b2 = (1 - cw) / 2; a0 = 1 + alpha; a1 = -2 * cw; a2 = 1 - alpha; break;
    case VOLUM_EQ_NOTCH:
        b0 = 1; b1 = -2 * cw; b2 = 1; a0 = 1 + alpha; a1 = -2 * cw; a2 = 1 - alpha; break;
    default:
        return 0;
    }
    const std::complex<double> z1 = std::polar(1.0, -2 * kPi * f / fs), z2 = z1 * z1;
    const std::complex<double> h = (b0 + b1 * z1 + b2 * z2) / (a0 + a1 * z1 + a2 * z2);
    return 20 * std::log10(std::abs(h));
}

std::vector<float> sine(int rate, int frames, int ch, double freq, double amp) {
    std::vector<float> x(static_cast<size_t>(frames) * ch);
    for (int i = 0; i < frames; i++) {
        const float s = static_cast<float>(amp * std::sin(2 * kPi * freq * i / rate));
        for (int c = 0; c < ch; c++) x[static_cast<size_t>(i) * ch + c] = s;
    }
    return x;
}

std::vector<float> noise(int frames, int ch, double amp, uint32_t seed = 1) {
    std::vector<float> x(static_cast<size_t>(frames) * ch);
    for (float& s : x) {
        seed = seed * 1664525u + 1013904223u;
        s = static_cast<float>(amp * ((static_cast<int>(seed >> 8) & 0xFFFF) - 32768) / 32768.0);
    }
    return x;
}

double rms(const std::vector<float>& x, int ch, int c, int from, int to = -1) {
    const int frames = static_cast<int>(x.size()) / ch;
    if (to < 0 || to > frames) to = frames;
    double s = 0;
    for (int i = from; i < to; i++) {
        const double v = x[static_cast<size_t>(i) * ch + c];
        s += v * v;
    }
    return to > from ? std::sqrt(s / (to - from)) : 0;
}

std::vector<float> render(const VolumEqParams& p, int rate, int ch, const std::vector<float>& in, int block = 480) {
    EqEngine e;
    e.prepare(ch, rate);
    e.publish(p);
    std::vector<float> out(in.size());
    const int frames = static_cast<int>(in.size()) / ch;
    for (int i = 0; i < frames; i += block) {
        const int n = std::min(block, frames - i);
        e.process(in.data() + static_cast<size_t>(i) * ch, out.data() + static_cast<size_t>(i) * ch, n);
    }
    return out;
}

// Maior salto entre amostras vizinhas: um estalo aparece como um degrau muito maior que o do sinal.
double max_step(const std::vector<float>& x, int ch, int c, int from = 1) {
    double m = 0;
    const int frames = static_cast<int>(x.size()) / ch;
    for (int i = std::max(1, from); i < frames; i++)
        m = std::max(m, static_cast<double>(std::fabs(x[static_cast<size_t>(i) * ch + c] - x[static_cast<size_t>(i - 1) * ch + c])));
    return m;
}

void test_layout() {
    CHECK(sizeof(VolumEqBand) == 40);
    CHECK(sizeof(VolumEqParams) == 1304);
    CHECK(VOLUM_EQ_RECORD_BYTES == 512 + 1304);
}

void test_matches_cookbook() {
    // Mesma resposta do RBJ (dentro de 0,01 dB) para todos os tipos, várias taxas, ganhos e Qs.
    const double rates[] = { 44100, 48000, 96000, 192000 };
    const int types[] = { VOLUM_EQ_BELL, VOLUM_EQ_LOWSHELF, VOLUM_EQ_HIGHSHELF, VOLUM_EQ_HIGHPASS, VOLUM_EQ_LOWPASS, VOLUM_EQ_NOTCH };
    const double freqs[] = { 40, 250, 1000, 6000, 15000 };
    const double gains[] = { -12, -3, 4.5, 12 };
    const double qs[] = { 0.4, 0.707, 1.41, 4.0 };
    double worst = 0;
    for (double fs : rates)
        for (int type : types)
            for (double fc : freqs)
                for (double g : gains)
                    for (double q : qs) {
                        const VolumEqBand b = band(type, fc, g, q);
                        EqDesign d;
                        EqEngine::design(b, fs, &d);
                        for (double f = 20; f < 20000; f *= 1.15) {
                            const double ref = rbj_db(b, fs, f);
                            if (ref < -60) continue;   // fundo do notch/corte: os dois vão a -inf
                            worst = std::max(worst, std::fabs(EqEngine::design_db(d, fs, f) - ref));
                        }
                    }
    if (worst >= 0.01) std::printf("  diferença máxima para o RBJ: %.5f dB\n", worst);
    CHECK(worst < 0.01);
}

void test_bell_real_signal() {
    // Um seno de 1 kHz passando de verdade pelo filtro sai com o ganho pedido.
    const double rates[] = { 44100, 48000, 96000 };
    for (double fs : rates)
        for (double g : { -12.0, -6.0, 6.0, 12.0 }) {
            const auto p = one(band(VOLUM_EQ_BELL, 1000, g, 1.41));
            const auto in = sine(static_cast<int>(fs), static_cast<int>(fs / 2), 2, 1000, 0.1);
            const auto out = render(p, static_cast<int>(fs), 2, in);
            const int skip = static_cast<int>(fs / 10);
            const double got = 20 * std::log10(rms(out, 2, 1, skip) / rms(in, 2, 1, skip));
            CHECK(std::fabs(got - g) < 0.05);
        }
}

void test_shelf_q() {
    // Shelf com Q acima de 0,707 (presets do AutoEq usam) não pode ser cortado.
    const VolumEqBand b = band(VOLUM_EQ_LOWSHELF, 105, 6.5, 1.2);
    EqDesign d;
    EqEngine::design(b, 48000, &d);
    double bump = -100;
    for (double f = 20; f < 1000; f *= 1.02) bump = std::max(bump, EqEngine::design_db(d, 48000, f));
    CHECK(bump > 6.6);   // Q alto no shelf faz um pequeno "ombro" acima do ganho
    CHECK(std::fabs(EqEngine::design_db(d, 48000, 105) - rbj_db(b, 48000, 105)) < 0.01);
}

void test_slopes() {
    for (int slope = 1; slope <= 4; slope++) {
        const auto p = one(band(VOLUM_EQ_HIGHPASS, 100, 0, 0.7071, slope));
        CHECK(std::fabs(EqEngine::magnitude_db(p, 48000, 100) + 3.0103) < 0.05);   // Butterworth: -3 dB no corte
        const double octave = EqEngine::magnitude_db(p, 48000, 25) - EqEngine::magnitude_db(p, 48000, 50);
        CHECK(std::fabs(octave + 12.0 * slope) < 0.6);                             // 12 dB por oitava por estágio
        CHECK(std::fabs(EqEngine::magnitude_db(p, 48000, 2000)) < 0.01);
        const auto lp = one(band(VOLUM_EQ_LOWPASS, 2000, 0, 0.7071, slope));
        CHECK(std::fabs(EqEngine::magnitude_db(lp, 48000, 2000) + 3.0103) < 0.05);
        CHECK(std::fabs(EqEngine::magnitude_db(lp, 48000, 100)) < 0.01);
    }
}

void test_transparent() {
    VolumEqParams flat = {};
    flat.band_count = 10;
    for (int i = 0; i < 10; i++) flat.bands[i] = band(VOLUM_EQ_BELL, 31.25 * std::pow(2.0, i), 0, 1.41);
    const auto in = noise(9600, 2, 0.3);
    auto out = render(flat, 48000, 2, in);
    CHECK(std::memcmp(in.data(), out.data(), in.size() * sizeof(float)) == 0);   // bit a bit

    VolumEqParams by = flat;
    by.bands[3].gain_db = 9;
    by.preamp_db = -4;
    by.bypass = 1;
    out = render(by, 48000, 2, in);
    CHECK(std::memcmp(in.data(), out.data(), in.size() * sizeof(float)) == 0);

    VolumEqParams pre = flat;
    pre.preamp_db = 6;
    const auto s = sine(48000, 24000, 1, 440, 0.1);
    out = render(pre, 48000, 1, s);
    CHECK(std::fabs(20 * std::log10(rms(out, 1, 0, 4800) / rms(s, 1, 0, 4800)) - 6) < 0.02);
}

void test_inplace_equals_copy() {
    const auto p = one(band(VOLUM_EQ_BELL, 3000, 8, 2));
    const auto in = noise(12000, 2, 0.2);
    const auto split = render(p, 48000, 2, in);
    EqEngine e;
    e.prepare(2, 48000);
    e.publish(p);
    auto inplace = in;
    for (int i = 0; i < 12000; i += 480) e.process(inplace.data() + i * 2, inplace.data() + i * 2, 480);
    CHECK(std::memcmp(split.data(), inplace.data(), split.size() * sizeof(float)) == 0);
}

void test_all_channels_processed() {
    // 7.1: todos os canais, inclusive o LFE, recebem a mesma curva.
    const auto p = one(band(VOLUM_EQ_BELL, 60, -10, 1));
    const auto in = sine(48000, 24000, 8, 60, 0.2);
    const auto out = render(p, 48000, 8, in);
    for (int c = 0; c < 8; c++) CHECK(std::fabs(20 * std::log10(rms(out, 8, c, 9600) / rms(in, 8, c, 9600)) + 10) < 0.1);
}

void test_no_clicks() {
    // Mudanças bruscas de parâmetro com o som tocando não podem gerar degraus maiores que os do próprio sinal.
    const int fs = 48000;
    const auto in = sine(fs, fs, 1, 220, 0.3);
    const double natural = max_step(in, 1, 0);

    struct Case { const char* name; VolumEqParams a, b; };
    VolumEqParams base = one(band(VOLUM_EQ_BELL, 1000, 0, 1));
    VolumEqParams loud = one(band(VOLUM_EQ_BELL, 220, 12, 1));
    VolumEqParams moved = one(band(VOLUM_EQ_BELL, 3000, -12, 4));
    VolumEqParams cut = one(band(VOLUM_EQ_HIGHPASS, 400, 0, 0.7, 4));
    VolumEqParams off = loud; off.bands[0].enabled = 0;
    VolumEqParams byp = loud; byp.bypass = 1;
    VolumEqParams pre = loud; pre.preamp_db = -18;
    const Case cases[] = {
        { "liga +12 dB", base, loud }, { "pula de frequência/ganho/Q", loud, moved }, { "troca bell por corte 48 dB", loud, cut },
        { "desliga a banda", loud, off }, { "bypass", loud, byp }, { "preamp -18 dB", loud, pre }, { "corte de volta", cut, loud },
    };
    for (const Case& c : cases) {
        EqEngine e;
        e.prepare(1, fs);
        e.publish(c.a);
        std::vector<float> out(in.size());
        for (int i = 0; i < fs; i += 441) {
            if (i == fs / 2 - fs / 2 % 441) e.publish(c.b);
            const int n = std::min(441, fs - i);
            e.process(in.data() + i, out.data() + i, n);
        }
        const double step = max_step(out, 1, 0, fs / 10);
        if (step > natural * 4.5 + 1e-3) std::printf("  estalo em '%s': degrau %.4f (sinal %.4f)\n", c.name, step, natural);
        CHECK(step <= natural * 4.5 + 1e-3);   // +12 dB = 4x a amplitude; a margem cobre só o ganho, não um estalo
    }
}

void test_smoothing_settles() {
    // A suavização chega exatamente no valor pedido (sem ficar recalculando para sempre).
    const int fs = 48000;
    EqEngine e;
    e.prepare(2, fs);
    e.publish(one(band(VOLUM_EQ_BELL, 100, 0, 1)));
    const auto in = sine(fs, fs, 2, 1000, 0.1);
    std::vector<float> out(in.size());
    e.process(in.data(), out.data(), 4800);
    e.publish(one(band(VOLUM_EQ_BELL, 1000, 6, 1.41)));
    e.process(in.data() + 4800 * 2, out.data() + 4800 * 2, fs - 4800);
    const double got = 20 * std::log10(rms(out, 2, 0, fs / 2) / rms(in, 2, 0, fs / 2));
    CHECK(std::fabs(got - 6) < 0.05);
}

void test_robustness() {
    const auto p = one(band(VOLUM_EQ_BELL, 1000, 12, 4));
    auto in = sine(48000, 9600, 2, 1000, 0.2);
    in[100] = std::numeric_limits<float>::quiet_NaN();
    in[101] = std::numeric_limits<float>::infinity();
    in[3000] = -std::numeric_limits<float>::infinity();
    const auto out = render(p, 48000, 2, in);
    bool finite = true;
    for (float s : out) finite = finite && std::isfinite(s);
    CHECK(finite);
    CHECK(rms(out, 2, 0, 6000) > 0.1);   // o filtro não ficou mudo depois do NaN

    // Lixo nos parâmetros vira valores válidos.
    VolumEqParams junk = {};
    std::memset(&junk, 0xFF, sizeof(junk));
    junk.band_count = 999;
    junk.bands[0].freq_hz = std::numeric_limits<double>::quiet_NaN();
    junk.bands[1].q = -5;
    EqEngine::sanitize(&junk);
    CHECK(junk.band_count == VOLUM_EQ_MAX_BANDS);
    CHECK(std::isfinite(junk.bands[0].freq_hz) && junk.bands[1].q >= VOLUM_EQ_MIN_Q);
    const auto j = render(junk, 48000, 2, noise(4800, 2, 0.2));
    finite = true;
    for (float s : j) finite = finite && std::isfinite(s);
    CHECK(finite);

    // Grave extremo em taxa alta e agudo perto de Nyquist em taxa baixa.
    const auto deep = render(one(band(VOLUM_EQ_BELL, 10, 24, 24)), 192000, 1, noise(192000, 1, 0.1));
    const auto top = render(one(band(VOLUM_EQ_HIGHSHELF, 22000, 24, 24)), 44100, 1, noise(44100, 1, 0.1));
    finite = true;
    for (float s : deep) finite = finite && std::isfinite(s);
    for (float s : top) finite = finite && std::isfinite(s);
    CHECK(finite);

    // Automação rápida e contínua (alguém arrastando um ponto sem parar).
    EqEngine e;
    e.prepare(2, 48000);
    const auto nz = noise(48000, 2, 0.25, 7);
    std::vector<float> o(nz.size());
    double worst = 0;
    for (int blk = 0; blk < 100; blk++) {
        VolumEqParams m = {};
        m.band_count = 4;
        for (int b = 0; b < 4; b++)
            m.bands[b] = band(1 + (blk / 25 + b) % 6, 30 * std::pow(2.6, b) * (1 + 0.5 * std::sin(blk * 0.7)), -18 + (blk * 7 % 36), 0.3 + (blk % 9), 1 + blk % 4);
        e.publish(m);
        e.process(nz.data() + blk * 480 * 2, o.data() + blk * 480 * 2, 480);
    }
    for (float s : o) worst = std::max(worst, static_cast<double>(std::fabs(s)));
    CHECK(std::isfinite(worst) && worst < 40);
}

void test_param_box_threads() {
    // Uma thread publica sem parar, a outra lê: nunca pode aparecer um bloco misturado.
    EqParamBox box;
    std::atomic<bool> stop{ false };
    std::thread writer([&] {
        VolumEqParams p = {};
        for (int n = 1; !stop.load(); n++) {
            p.band_count = n % 32;
            for (auto& b : p.bands) b.gain_db = n;
            p.preamp_db = n;
            box.publish(p);
        }
    });
    int torn = 0, reads = 0;
    const ULONGLONG until = GetTickCount64() + 300;
    VolumEqParams got;
    while (GetTickCount64() < until) {
        if (!box.take(&got)) continue;
        reads++;
        for (const auto& b : got.bands) if (b.gain_db != got.preamp_db) { torn++; break; }
    }
    stop.store(true);
    writer.join();
    CHECK(reads > 100);
    CHECK(torn == 0);
}

void write_file(const wchar_t* path, const std::vector<std::pair<std::wstring, VolumEqParams>>& recs) {
    HANDLE f = CreateFileW(path, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_TEMPORARY, nullptr);
    DWORD w = 0;
    uint32_t head[4] = { VOLUM_EQ_FILE_VERSION, static_cast<uint32_t>(recs.size()), VOLUM_EQ_RECORD_BYTES, 0 };
    WriteFile(f, VOLUM_EQ_FILE_MAGIC, 8, &w, nullptr);
    WriteFile(f, head, sizeof(head), &w, nullptr);
    for (const auto& r : recs) {
        wchar_t id[VOLUM_EQ_ID_CHARS] = {};
        wcsncpy_s(id, r.first.c_str(), _TRUNCATE);
        WriteFile(f, id, sizeof(id), &w, nullptr);
        WriteFile(f, &r.second, sizeof(r.second), &w, nullptr);
    }
    CloseHandle(f);
}

void test_file() {
    wchar_t dir[MAX_PATH], path[MAX_PATH];
    GetTempPathW(MAX_PATH, dir);
    swprintf_s(path, L"%svolum-eq-test-%lu.bin", dir, GetCurrentProcessId());
    const wchar_t* id = L"{0.0.0.00000000}.{ca9e7e11-1a17-4595-9d51-9c727501c305}";

    VolumEqParams a = one(band(VOLUM_EQ_BELL, 1000, 4.5, 1.1), -3.5);
    a.flags = VOLUM_EQ_FLAG_INSTALLED;
    VolumEqParams b = one(band(VOLUM_EQ_LOWSHELF, 100, -2, 0.7));
    write_file(path, { { id, a }, { L"outro", b } });

    VolumEqParams got = {};
    CHECK(EqEngine::read_file(path, L"{0.0.0.00000000}.{CA9E7E11-1A17-4595-9D51-9C727501C305}", &got) == 1);   // id sem diferença de caixa
    CHECK(got.preamp_db == -3.5 && got.bands[0].gain_db == 4.5);
    CHECK(EqEngine::read_file(path, L"outro", &got) == 1 && got.bands[0].type == VOLUM_EQ_LOWSHELF);
    CHECK(EqEngine::read_file(path, L"{ca9e7e11-1a17-4595-9d51-9c727501c305}", &got) == 1 && got.bands[0].gain_db == 4.5);   // só o GUID (como o Windows entrega)
    CHECK(EqEngine::read_file(path, L"nenhum", &got) == 0);
    CHECK(EqEngine::read_file(path, L"", &got) == 1 && got.bands[0].gain_db == 4.5);   // sem id: o único ativado

    // Arquivo truncado ou de outra versão: erro (o efeito mantém a curva que tinha).
    HANDLE f = CreateFileW(path, GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
    SetFilePointer(f, 100, nullptr, FILE_BEGIN);
    SetEndOfFile(f);
    CloseHandle(f);
    CHECK(EqEngine::read_file(path, id, &got) == -2);
    DeleteFileW(path);
    CHECK(EqEngine::read_file(path, id, &got) == -1);

    // Arquivo aberto por outro processo para escrita (o Volum trocando): não pode travar nem quebrar.
    write_file(path, { { id, a } });
    HANDLE hold = CreateFileW(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr);
    CHECK(EqEngine::read_file(path, id, &got) == 1);
    CloseHandle(hold);
    DeleteFileW(path);
}

void test_dll() {
    wchar_t path[MAX_PATH];
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    wchar_t* slash = wcsrchr(path, L'\\');
    if (!slash) return;
    wcscpy_s(slash + 1, MAX_PATH - static_cast<size_t>(slash + 1 - path), L"VolumEqApo.dll");
    HMODULE dll = LoadLibraryW(path);
    CHECK(dll != nullptr);
    if (!dll) return;
    using Smoke = int (*)(wchar_t*, int);
    auto smoke = reinterpret_cast<Smoke>(GetProcAddress(dll, "volum_eq_apo_smoke"));
    CHECK(smoke != nullptr);
    if (smoke) {
        wchar_t error[256] = {};
        const int rc = smoke(error, 256);
        if (rc != 0) std::wprintf(L"  APO: %s\n", error);
        CHECK(rc == 0);
    }
    CHECK(GetProcAddress(dll, "DllRegisterServer") != nullptr);
    CHECK(GetProcAddress(dll, "volum_eq_preview_start") != nullptr);
    FreeLibrary(dll);
}

void bench() {
    // Pior caso realista: 32 bandas, cortes de 48 dB, 8 canais. Mede quanto de um núcleo isso pede.
    VolumEqParams p = {};
    p.band_count = 32;
    for (int i = 0; i < 32; i++) p.bands[i] = band(i % 2 ? VOLUM_EQ_BELL : VOLUM_EQ_HIGHPASS, 20 * std::pow(1.24, i), 3, 1, 4);
    EqEngine e;
    e.prepare(8, 48000);
    e.publish(p);
    auto buf = noise(480, 8, 0.1);
    LARGE_INTEGER t0, t1, fq;
    QueryPerformanceFrequency(&fq);
    QueryPerformanceCounter(&t0);
    for (int i = 0; i < 1000; i++) e.process(buf.data(), buf.data(), 480);   // 10 s de áudio
    QueryPerformanceCounter(&t1);
    const double secs = double(t1.QuadPart - t0.QuadPart) / fq.QuadPart;
    std::printf("  pior caso (32 bandas, 48 dB/oit, 7.1): %.2f%% de um núcleo\n", secs / 10.0 * 100);

    VolumEqParams typical = {};
    typical.band_count = 10;
    for (int i = 0; i < 10; i++) typical.bands[i] = band(VOLUM_EQ_BELL, 31.25 * std::pow(2.0, i), i % 3 - 1.0, 1.41);
    EqEngine t;
    t.prepare(2, 48000);
    t.publish(typical);
    auto st = noise(480, 2, 0.1);
    QueryPerformanceCounter(&t0);
    for (int i = 0; i < 1000; i++) t.process(st.data(), st.data(), 480);
    QueryPerformanceCounter(&t1);
    std::printf("  uso típico (10 bandas, estéreo): %.3f%% de um núcleo\n", double(t1.QuadPart - t0.QuadPart) / fq.QuadPart / 10.0 * 100);
}

} // namespace

int main() {
    std::printf("Volum — testes do equalizador\n");
    test_layout();
    test_matches_cookbook();
    test_bell_real_signal();
    test_shelf_q();
    test_slopes();
    test_transparent();
    test_inplace_equals_copy();
    test_all_channels_processed();
    test_no_clicks();
    test_smoothing_settles();
    test_robustness();
    test_param_box_threads();
    test_file();
    test_dll();
    bench();
    if (g_fails) {
        std::printf("%d de %d verificações falharam\n", g_fails, g_checks);
        return 1;
    }
    std::printf("ok (%d verificações)\n", g_checks);
    return 0;
}
