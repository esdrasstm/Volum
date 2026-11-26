#include "eq_engine.h"

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <avrt.h>
#include <mmreg.h>
#include <ks.h>
#include <ksmedia.h>

#include <atomic>
#include <cmath>
#include <cstring>

#pragma comment(lib, "avrt.lib")
#pragma comment(lib, "ole32.lib")

// Funções que o Volum (C#) chama direto na DLL, e a prévia: ruído rosa tocado numa saída, passando
// pelo mesmo motor do efeito do Windows. Serve para ouvir a curva antes de ativar no sistema.

#define VOLUM_EQ_ENGINE_VERSION 3

namespace {

// Ruído rosa (filtro de Paul Kellet sobre ruído branco), por volta de -20 dBFS RMS.
struct Pink {
    double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
    uint32_t state = 0x9e3779b9u;
    double next() {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        const double white = static_cast<int32_t>(state) / 2147483648.0;
        b0 = 0.99886 * b0 + white * 0.0555179;
        b1 = 0.99332 * b1 + white * 0.0750759;
        b2 = 0.96900 * b2 + white * 0.1538520;
        b3 = 0.86650 * b3 + white * 0.3104856;
        b4 = 0.55000 * b4 + white * 0.5329522;
        b5 = -0.7616 * b5 - white * 0.0168980;
        const double pink = b0 + b1 + b2 + b3 + b4 + b5 + b6 + white * 0.5362;
        b6 = white * 0.115926;
        return pink * 0.045;
    }
};

struct Preview {
    EqEngine engine;
    std::atomic<bool> run{ false };
    HANDLE thread = nullptr;
    HANDLE ready = nullptr;
    HRESULT hr = E_FAIL;
    wchar_t error[256] = L"";
    wchar_t device[VOLUM_EQ_ID_CHARS] = L"";
};

Preview g_preview;

void set_error(const wchar_t* text) {
    if (g_preview.error[0] == 0) wcsncpy_s(g_preview.error, text, _TRUNCATE);
}

template <class T> void release(T*& p) {
    if (p) {
        p->Release();
        p = nullptr;
    }
}

DWORD WINAPI preview_thread(LPVOID) {
    const HRESULT co = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    DWORD task = 0;
    HANDLE mmcss = AvSetMmThreadCharacteristicsW(L"Audio", &task);

    IMMDeviceEnumerator* en = nullptr;
    IMMDevice* dev = nullptr;
    IAudioClient* client = nullptr;
    IAudioRenderClient* render = nullptr;
    WAVEFORMATEX* mix = nullptr;
    HANDLE event = nullptr;
    bool started = false;
    int channels = 0;
    UINT32 buffer = 0;

    HRESULT hr = CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&en));
    if (FAILED(hr)) set_error(L"audio-enumerator");
    if (SUCCEEDED(hr)) {
        hr = g_preview.device[0] ? en->GetDevice(g_preview.device, &dev) : en->GetDefaultAudioEndpoint(eRender, eConsole, &dev);
        if (FAILED(hr)) set_error(L"device-missing");
    }
    if (SUCCEEDED(hr)) {
        hr = dev->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(&client));
        if (FAILED(hr)) set_error(L"device-busy");
    }
    if (SUCCEEDED(hr)) hr = client->GetMixFormat(&mix);
    if (SUCCEEDED(hr) && mix) {
        channels = mix->nChannels;
        bool is_float = mix->wFormatTag == WAVE_FORMAT_IEEE_FLOAT && mix->wBitsPerSample == 32;
        if (mix->wFormatTag == WAVE_FORMAT_EXTENSIBLE) {
            auto* ext = reinterpret_cast<WAVEFORMATEXTENSIBLE*>(mix);
            is_float = ext->SubFormat == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT && mix->wBitsPerSample == 32;
        }
        if (!is_float || channels < 1) {
            hr = AUDCLNT_E_UNSUPPORTED_FORMAT;
            set_error(L"format");
        }
    }
    if (SUCCEEDED(hr)) {
        hr = client->Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_EVENTCALLBACK, 0, 0, mix, nullptr);
        if (FAILED(hr)) set_error(hr == AUDCLNT_E_DEVICE_IN_USE ? L"exclusive" : L"device-busy");
    }
    if (SUCCEEDED(hr)) {
        event = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        if (!event) hr = E_OUTOFMEMORY;
    }
    if (SUCCEEDED(hr)) hr = client->SetEventHandle(event);
    if (SUCCEEDED(hr)) hr = client->GetService(IID_PPV_ARGS(&render));
    if (SUCCEEDED(hr)) hr = client->GetBufferSize(&buffer);
    if (SUCCEEDED(hr)) {
        g_preview.engine.prepare(channels, static_cast<double>(mix->nSamplesPerSec));
        hr = client->Start();
    }
    if (SUCCEEDED(hr)) {
        started = true;
        g_preview.hr = S_OK;
    } else {
        g_preview.hr = hr;
        set_error(L"device-busy");
    }
    SetEvent(g_preview.ready);

    // Entra e sai com rampa de 40 ms: ligar e desligar a prévia não estala.
    Pink pink;
    const double ramp_step = 1.0 / (0.040 * (mix ? mix->nSamplesPerSec : 48000));
    double level = 0.0;
    bool stopping = false;
    while (started) {
        if (WaitForSingleObject(event, 200) != WAIT_OBJECT_0) {
            if (!g_preview.run.load(std::memory_order_acquire)) break;
            continue;
        }
        if (!g_preview.run.load(std::memory_order_acquire)) stopping = true;
        UINT32 padding = 0;
        if (FAILED(client->GetCurrentPadding(&padding))) break;
        const UINT32 want = buffer - padding;
        if (want == 0) continue;
        BYTE* data = nullptr;
        if (FAILED(render->GetBuffer(want, &data)) || !data) break;
        auto* samples = reinterpret_cast<float*>(data);
        for (UINT32 i = 0; i < want; i++) {
            if (stopping) level = level > ramp_step ? level - ramp_step : 0.0;
            else level = level + ramp_step < 1.0 ? level + ramp_step : 1.0;
            const float s = static_cast<float>(pink.next() * level);
            float* frame = samples + static_cast<size_t>(i) * channels;
            for (int c = 0; c < channels; c++) frame[c] = c < 2 ? s : 0.f;
        }
        g_preview.engine.process(samples, samples, static_cast<int>(want));
        for (UINT32 i = 0; i < want * static_cast<UINT32>(channels); i++) {
            const float s = samples[i];
            samples[i] = s > 1.f ? 1.f : (s < -1.f ? -1.f : s);
        }
        render->ReleaseBuffer(want, 0);
        if (stopping && level <= 0.0) {
            Sleep(static_cast<DWORD>(buffer * 1000ull / (mix ? mix->nSamplesPerSec : 48000)) + 10);
            break;
        }
    }

    if (started) client->Stop();
    release(render);
    release(client);
    if (mix) CoTaskMemFree(mix);
    release(dev);
    release(en);
    if (event) CloseHandle(event);
    if (mmcss) AvRevertMmThreadCharacteristics(mmcss);
    if (SUCCEEDED(co)) CoUninitialize();
    return 0;
}

void preview_stop() {
    if (!g_preview.thread) return;
    g_preview.run.store(false, std::memory_order_release);
    WaitForSingleObject(g_preview.thread, 3000);
    CloseHandle(g_preview.thread);
    g_preview.thread = nullptr;
    if (g_preview.ready) {
        CloseHandle(g_preview.ready);
        g_preview.ready = nullptr;
    }
}

} // namespace

extern "C" {

__declspec(dllexport) int volum_eq_version(void) { return VOLUM_EQ_ENGINE_VERSION; }
__declspec(dllexport) int volum_eq_params_size(void) { return static_cast<int>(sizeof(VolumEqParams)); }
__declspec(dllexport) int volum_eq_record_size(void) { return VOLUM_EQ_RECORD_BYTES; }

__declspec(dllexport) void volum_eq_sanitize(VolumEqParams* params) { EqEngine::sanitize(params); }

__declspec(dllexport) double volum_eq_magnitude_db(const VolumEqParams* params, double sample_rate, double freq_hz) {
    return params ? EqEngine::magnitude_db(*params, sample_rate, freq_hz) : 0.0;
}

__declspec(dllexport) int volum_eq_read_device(const wchar_t* path, const wchar_t* endpoint_id, VolumEqParams* out) {
    return EqEngine::read_file(path, endpoint_id, out) == 1 ? 1 : 0;
}

__declspec(dllexport) void volum_eq_preview_set(const VolumEqParams* params) {
    if (params) g_preview.engine.publish(*params);
}

__declspec(dllexport) float volum_eq_preview_peak(void) { return g_preview.engine.take_peak(); }

__declspec(dllexport) int volum_eq_preview_start(const wchar_t* device_id) {
    preview_stop();
    g_preview.hr = E_FAIL;
    g_preview.error[0] = 0;
    g_preview.device[0] = 0;
    if (device_id) wcsncpy_s(g_preview.device, device_id, _TRUNCATE);
    g_preview.ready = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!g_preview.ready) return E_OUTOFMEMORY;
    g_preview.run.store(true, std::memory_order_release);
    g_preview.thread = CreateThread(nullptr, 0, preview_thread, nullptr, 0, nullptr);
    if (!g_preview.thread) {
        g_preview.run.store(false, std::memory_order_release);
        CloseHandle(g_preview.ready);
        g_preview.ready = nullptr;
        return E_FAIL;
    }
    WaitForSingleObject(g_preview.ready, 4000);
    if (FAILED(g_preview.hr)) {
        const int hr = static_cast<int>(g_preview.hr);
        preview_stop();
        return hr;
    }
    return 0;
}

__declspec(dllexport) void volum_eq_preview_stop(void) { preview_stop(); }

__declspec(dllexport) const wchar_t* volum_eq_preview_error(void) { return g_preview.error; }

} // extern "C"
