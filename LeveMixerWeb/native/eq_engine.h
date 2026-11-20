#pragma once

#include "eq_params.h"

#include <atomic>
#include <cstdint>

// Troca de parâmetros sem trava entre uma thread que escreve e a thread de áudio que lê
// (triple buffer clássico: quem escreve nunca espera e quem lê nunca vê um bloco pela metade).
class EqParamBox {
public:
    EqParamBox();
    void publish(const VolumEqParams& params);   // uma única thread escreve
    bool take(VolumEqParams* out);               // só a thread de áudio lê; false = nada novo

private:
    VolumEqParams m_buf[3];
    std::atomic<uint32_t> m_middle;
    uint32_t m_back = 0;
    uint32_t m_front = 2;
};

// Um estágio SVF (state variable filter) linear trapezoidal, forma de Andrew Simper (Cytomic).
// Mesma resposta dos biquads do RBJ, mas estável e sem estalo quando os parâmetros mudam com o som tocando.
struct EqStage {
    double g, k, a1, a2, a3, m0, m1, m2;
};

struct EqDesign {
    int count;              // estágios em uso (0 = banda desligada)
    int neutral;            // 1 = ganho 0 dB: a banda existe, mas não muda o som
    EqStage stage[VOLUM_EQ_MAX_SLOPE];
};

class EqEngine {
public:
    static constexpr int kMaxChannels = 16;
    static constexpr int kBlock = 32;          // parâmetros andam a cada 32 amostras (0,67 ms a 48 kHz)

    EqEngine();

    void prepare(int channels, double sample_rate);
    void publish(const VolumEqParams& params) { m_box.publish(params); }
    void process(const float* in, float* out, int frames);
    void reset();

    // Verdadeiro quando o processamento deixaria o sinal intacto (dá para devolver o buffer como veio).
    bool idle() const;

    float take_peak() { return m_peak.exchange(0.f, std::memory_order_relaxed); }
    uint64_t frames_done() const { return m_frames.load(std::memory_order_relaxed); }
    uint32_t clips() const { return m_clips.load(std::memory_order_relaxed); }
    int channels() const { return m_channels; }
    double sample_rate() const { return m_rate; }

    static void sanitize(VolumEqParams* params);
    static bool design(const VolumEqBand& band, double sample_rate, EqDesign* out);
    static double design_db(const EqDesign& design, double sample_rate, double freq_hz);
    static double magnitude_db(const VolumEqParams& params, double sample_rate, double freq_hz);

    // 1 = achou o registro do dispositivo; 0 = arquivo sem esse dispositivo; -1 = arquivo não existe; -2 = erro de leitura.
    static int read_file(const wchar_t* path, const wchar_t* endpoint_id, VolumEqParams* out);

private:
    struct Band {
        int type = VOLUM_EQ_OFF;
        int slope = 1;
        double log_freq = 6.907755278982137;   // ln(1000)
        double gain = 0.0;
        double log_q = 0.0;
        double amount = 0.0;                   // 0 = banda fora do caminho, 1 = efeito inteiro
        bool dirty = true;
        bool live = false;                     // tem estado de filtro guardado
        bool ramp = false;                     // neste bloco os coeficientes vão de prev até design
        EqDesign design = {};
        EqDesign prev = {};
        bool audible() const { return design.count > 0 && (!design.neutral || ramp); }
    };

    void control_step(bool first);
    void redesign(Band& band);
    void clear_band(int index);

    EqParamBox m_box;
    VolumEqParams m_target = {};

    int m_channels = 0;
    int m_filtered = 0;
    double m_rate = 48000.0;
    bool m_primed = false;

    double m_smooth = 0.0;          // fração por bloco da suavização (constante de ~12 ms)
    double m_fade_step = 0.0;       // passo por bloco das transições liga/desliga (~10 ms)

    Band m_bands[VOLUM_EQ_MAX_BANDS];
    double m_state[VOLUM_EQ_MAX_BANDS][VOLUM_EQ_MAX_SLOPE][kMaxChannels][2] = {};
    double m_gain = 1.0;            // preamp linear, já suavizado
    double m_wet = 1.0;             // 0 = desligado (bypass), 1 = ligado
    bool m_settled = false;
    bool m_live = false;

    double m_amount0[VOLUM_EQ_MAX_BANDS] = {};
    double m_amount1[VOLUM_EQ_MAX_BANDS] = {};
    double m_gain0 = 1.0, m_gain1 = 1.0, m_wet0 = 1.0, m_wet1 = 1.0;

    std::atomic<float> m_peak{ 0.f };
    std::atomic<uint64_t> m_frames{ 0 };
    std::atomic<uint32_t> m_clips{ 0 };
};
