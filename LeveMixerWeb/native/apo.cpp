#include "eq_engine.h"
#include "eq_ids.h"

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <propsys.h>
#include <shlobj.h>
#include <mmdeviceapi.h>
#include <audiomediatype.h>
#include <baseaudioprocessingobject.h>
#include <ksmedia.h>

#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>

#pragma comment(lib, "audiobaseprocessingobject.lib")
#pragma comment(lib, "audiomediatypecrt.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "uuid.lib")
#pragma comment(lib, "propsys.lib")
#pragma comment(lib, "shell32.lib")

// Efeito de endpoint (EFX) do Volum: o Windows chama APOProcess no buffer que já ia para o
// dispositivo, depois da mixagem de todos os apps. O equalizador processa no lugar, sem buffer
// novo e sem latência. Se o dispositivo já tinha um EFX do fabricante, ele continua rodando antes.
//
// A curva vem do eq.bin (ProgramData\Volum), que o Volum regrava quando você mexe na janela. Uma
// thread de apoio confere o arquivo a cada 25 ms e entrega os valores ao motor sem trava; a cada
// segundo ela também deixa um "sinal de vida" em ProgramData\Volum\eq-live, que a janela usa para
// mostrar que o efeito está carregado e processando.

static HMODULE g_module = nullptr;
static std::atomic<long> g_instances{ 0 };
static std::atomic<long> g_serial{ 0 };   // numera as cópias do efeito dentro do audiodg

static CRegAPOProperties<1> g_reg(
    CLSID_VolumEqApo,
    L"Volum Equalizer",
    L"Volum",
    1, 0,
    __uuidof(IAudioProcessingObject),
    static_cast<APO_FLAG>(APO_FLAG_DEFAULT | APO_FLAG_INPLACE));

namespace {

struct Paths {
    wchar_t config[MAX_PATH] = L"";
    wchar_t live[MAX_PATH] = L"";
    wchar_t log[MAX_PATH] = L"";
};

Paths g_paths;
INIT_ONCE g_paths_once = INIT_ONCE_STATIC_INIT;

BOOL CALLBACK init_paths(PINIT_ONCE, PVOID, PVOID*) {
    PWSTR data = nullptr;
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_ProgramData, 0, nullptr, &data)) && data) {
        swprintf_s(g_paths.config, L"%s\\Volum\\eq.bin", data);
        swprintf_s(g_paths.live, L"%s\\Volum\\eq-live", data);
        swprintf_s(g_paths.log, L"%s\\Volum\\eq-live\\apo.log", data);
    } else {
        wcscpy_s(g_paths.config, L"C:\\ProgramData\\Volum\\eq.bin");
        wcscpy_s(g_paths.live, L"C:\\ProgramData\\Volum\\eq-live");
        wcscpy_s(g_paths.log, L"C:\\ProgramData\\Volum\\eq-live\\apo.log");
    }
    if (data) CoTaskMemFree(data);
    return TRUE;
}

const Paths& paths() {
    InitOnceExecuteOnce(&g_paths_once, init_paths, nullptr, nullptr);
    return g_paths;
}

// Registro curto do que deu errado (nunca na thread de áudio). Fica pequeno: passou de 64 KB, recomeça.
void apo_log(const wchar_t* fmt, ...) {
    wchar_t text[400];
    va_list args;
    va_start(args, fmt);
    vswprintf_s(text, fmt, args);
    va_end(args);

    SYSTEMTIME now;
    GetLocalTime(&now);
    wchar_t line[480];
    swprintf_s(line, L"%04u-%02u-%02u %02u:%02u:%02u [%lu] %s\r\n", now.wYear, now.wMonth, now.wDay,
        now.wHour, now.wMinute, now.wSecond, GetCurrentProcessId(), text);
    char utf8[1024];
    int n = WideCharToMultiByte(CP_UTF8, 0, line, -1, utf8, sizeof(utf8), nullptr, nullptr);
    if (n <= 1) return;

    HANDLE file = CreateFileW(paths().log, FILE_APPEND_DATA | FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        // Sem acesso ao ProgramData\Volum\eq-live: vai para a pasta temporária do serviço de áudio
        // (o instalador, elevado, lê de lá na verificação).
        wchar_t tmp[MAX_PATH];
        DWORD n_tmp = GetTempPathW(MAX_PATH, tmp);
        if (n_tmp == 0 || n_tmp > MAX_PATH - 20) return;
        wcscat_s(tmp, L"VolumEqApo.log");
        file = CreateFileW(tmp, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS,
            FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
        if (file == INVALID_HANDLE_VALUE) return;
    }
    LARGE_INTEGER size = {};
    if (GetFileSizeEx(file, &size) && size.QuadPart > 64 * 1024) {
        CloseHandle(file);
        file = CreateFileW(paths().log, GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS,
            FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
        if (file == INVALID_HANDLE_VALUE) return;
    }
    DWORD wrote = 0;
    WriteFile(file, utf8, static_cast<DWORD>(n - 1), &wrote, nullptr);
    CloseHandle(file);
}

bool call_child(IAudioProcessingObjectRT* child, UINT32 nin, APO_CONNECTION_PROPERTY** in_conn,
    UINT32 nout, APO_CONNECTION_PROPERTY** out_conn) {
    __try {
        child->APOProcess(nin, in_conn, nout, out_conn);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

const wchar_t* endpoint_key(const wchar_t* id) {
    const wchar_t* dot = wcsrchr(id, L'.');
    return dot ? dot + 1 : id;
}

} // namespace

class VolumEqApo : public CBaseAudioProcessingObject {
public:
    // O Windows cria os efeitos do áudio de verdade por agregação COM (o objeto dele "envolve" este):
    // com outer, todo IUnknown público delega para ele; o IUnknown próprio (m_inner) é o que conta as referências.
    explicit VolumEqApo(IUnknown* outer) : CBaseAudioProcessingObject(g_reg), m_sysfx(this), m_inner(this) {
        m_outer = outer ? outer : static_cast<IUnknown*>(&m_inner);
        g_instances.fetch_add(1);
        m_stop = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    }
    ~VolumEqApo() override {
        StopWatch();
        DropChild();
        if (m_silence) AERT_Free(m_silence);
        if (m_stop) CloseHandle(m_stop);
        g_instances.fetch_sub(1);
    }

    STDMETHOD(QueryInterface)(REFIID riid, void** ppv);
    STDMETHOD_(ULONG, AddRef)();
    STDMETHOD_(ULONG, Release)();
    STDMETHOD_(void, APOProcess)(UINT32 nin, APO_CONNECTION_PROPERTY** in_conn, UINT32 nout, APO_CONNECTION_PROPERTY** out_conn) override;
    STDMETHOD(LockForProcess)(UINT32 nin, APO_CONNECTION_DESCRIPTOR** in_conn, UINT32 nout, APO_CONNECTION_DESCRIPTOR** out_conn) override;
    STDMETHOD(UnlockForProcess)() override;
    STDMETHOD(Reset)() override;
    STDMETHOD(Initialize)(UINT32 cb, BYTE* data) override;
    STDMETHOD(IsInputFormatSupported)(IAudioMediaType* out_fmt, IAudioMediaType* requested, IAudioMediaType** supported) override;
    STDMETHOD(IsOutputFormatSupported)(IAudioMediaType* in_fmt, IAudioMediaType* requested, IAudioMediaType** supported) override;

    HRESULT GetEffectsList(LPGUID* ids, UINT* count, HANDLE);
    IUnknown* Inner() { return &m_inner; }

private:
    class InnerUnknown : public IUnknown {
    public:
        explicit InnerUnknown(VolumEqApo* self) : m_self(self) {}
        STDMETHOD(QueryInterface)(REFIID riid, void** ppv) override { return m_self->InnerQueryInterface(riid, ppv); }
        STDMETHOD_(ULONG, AddRef)() override { return static_cast<ULONG>(m_self->m_ref.fetch_add(1) + 1); }
        STDMETHOD_(ULONG, Release)() override {
            long left = m_self->m_ref.fetch_sub(1) - 1;
            if (left == 0) delete m_self;
            return static_cast<ULONG>(left);
        }
    private:
        VolumEqApo* m_self;
    };
    HRESULT InnerQueryInterface(REFIID riid, void** ppv);

    class SysFx : public IAudioSystemEffects2 {
    public:
        explicit SysFx(VolumEqApo* outer) : m_outer(outer) {}
        STDMETHOD(QueryInterface)(REFIID riid, void** ppv) override { return m_outer->QueryInterface(riid, ppv); }
        STDMETHOD_(ULONG, AddRef)() override { return m_outer->AddRef(); }
        STDMETHOD_(ULONG, Release)() override { return m_outer->Release(); }
        STDMETHOD(GetEffectsList)(LPGUID* ids, UINT* count, HANDLE event) override { return m_outer->GetEffectsList(ids, count, event); }
    private:
        VolumEqApo* m_outer;
    };

    void ReadProps(IPropertyStore* fx, IPropertyStore* endpoint);
    void TryChild(REFCLSID child, UINT32 cb, const BYTE* data);
    void DropChild();
    void StartWatch();
    void StopWatch();
    bool LoadConfig(bool first);
    void WriteLive();
    void ProcessSilence(float* dst, UINT32 frames);
    static DWORD WINAPI WatchThread(LPVOID self);

    SysFx m_sysfx;
    InnerUnknown m_inner;
    IUnknown* m_outer = nullptr;
    EqEngine m_engine;
    std::atomic<long> m_ref{ 1 };
    wchar_t m_endpoint[VOLUM_EQ_ID_CHARS] = {};
    wchar_t m_live_file[MAX_PATH] = {};

    IAudioProcessingObject* m_child = nullptr;
    IAudioProcessingObjectRT* m_child_rt = nullptr;
    IAudioProcessingObjectConfiguration* m_child_cfg = nullptr;
    GUID m_child_clsid = GUID_NULL;
    std::atomic<int> m_child_broken{ 0 };

    HANDLE m_watch = nullptr;
    HANDLE m_stop = nullptr;
    float* m_silence = nullptr;
    UINT32 m_silence_frames = 256;
    UINT32 m_channels = 0;
    UINT32 m_frame_bytes = 0;
    bool m_float = false;
    bool m_discovery = false;
    uint64_t m_seen_stamp = 0;
    uint64_t m_seen_size = 0;
    bool m_live_ok = true;
    ULONGLONG m_live_retry = 0;
};

STDMETHODIMP VolumEqApo::QueryInterface(REFIID riid, void** ppv) { return m_outer->QueryInterface(riid, ppv); }
STDMETHODIMP_(ULONG) VolumEqApo::AddRef() { return m_outer->AddRef(); }
STDMETHODIMP_(ULONG) VolumEqApo::Release() { return m_outer->Release(); }

HRESULT VolumEqApo::InnerQueryInterface(REFIID riid, void** ppv) {
    if (!ppv) return E_POINTER;
    *ppv = nullptr;
    if (riid == IID_IUnknown) {
        *ppv = static_cast<IUnknown*>(&m_inner);
        m_inner.AddRef();
        return S_OK;
    }
    if (riid == __uuidof(IAudioProcessingObject))
        *ppv = static_cast<IAudioProcessingObject*>(this);
    else if (riid == __uuidof(IAudioProcessingObjectRT))
        *ppv = static_cast<IAudioProcessingObjectRT*>(this);
    else if (riid == __uuidof(IAudioProcessingObjectConfiguration))
        *ppv = static_cast<IAudioProcessingObjectConfiguration*>(this);
    else if (riid == __uuidof(IAudioSystemEffects) || riid == __uuidof(IAudioSystemEffects2))
        *ppv = static_cast<IAudioSystemEffects2*>(&m_sysfx);
    else
        return E_NOINTERFACE;
    static_cast<IUnknown*>(*ppv)->AddRef();   // delega: com agregação, quem conta é o objeto de fora
    return S_OK;
}

HRESULT VolumEqApo::GetEffectsList(LPGUID* ids, UINT* count, HANDLE) {
    if (!ids || !count) return E_POINTER;
    *ids = static_cast<LPGUID>(CoTaskMemAlloc(sizeof(GUID)));
    if (!*ids) return E_OUTOFMEMORY;
    **ids = CLSID_VolumEqApo;
    *count = 1;
    return S_OK;
}

void VolumEqApo::ReadProps(IPropertyStore* store, IPropertyStore* endpoint) {
    m_endpoint[0] = 0;
    // Quem é este dispositivo: o GUID do endpoint, que o Windows sempre entrega (é o fim do id do IMMDevice).
    if (endpoint) {
        PROPVARIANT guid;
        PropVariantInit(&guid);
        const PROPERTYKEY guid_key = { { 0x1da5d803, 0xd492, 0x4edd, { 0x8c, 0x23, 0xe0, 0xc0, 0xff, 0xee, 0x7f, 0x0e } }, 4 };
        if (SUCCEEDED(endpoint->GetValue(guid_key, &guid)) && guid.vt == VT_LPWSTR && guid.pwszVal)
            wcsncpy_s(m_endpoint, guid.pwszVal, _TRUNCATE);
        PropVariantClear(&guid);
    }
    if (!store) return;
    const PROPERTYKEY id_key = { VOLUM_EQ_PROP, VOLUM_EQ_PROP_ENDPOINT };
    const PROPERTYKEY child_key = { VOLUM_EQ_PROP, VOLUM_EQ_PROP_CHILD };
    PROPVARIANT value;
    PropVariantInit(&value);
    if (!m_endpoint[0] && SUCCEEDED(store->GetValue(id_key, &value)) && value.vt == VT_LPWSTR && value.pwszVal)
        wcsncpy_s(m_endpoint, value.pwszVal, _TRUNCATE);
    PropVariantClear(&value);
    PropVariantInit(&value);
    if (SUCCEEDED(store->GetValue(child_key, &value)) && value.vt == VT_LPWSTR && value.pwszVal && value.pwszVal[0]) {
        GUID child = {};
        if (SUCCEEDED(CLSIDFromString(value.pwszVal, &child)) && child != GUID_NULL && child != CLSID_VolumEqApo)
            m_child_clsid = child;
    }
    PropVariantClear(&value);
}

void VolumEqApo::TryChild(REFCLSID child, UINT32 cb, const BYTE* data) {
    if (cb > 2048 || !data || cb < sizeof(APOInitBaseStruct)) return;
    IAudioProcessingObject* obj = nullptr;
    HRESULT hr = CoCreateInstance(child, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&obj));
    if (FAILED(hr) || !obj) {
        apo_log(L"efeito anterior do dispositivo não abriu (0x%08X); o equalizador segue sozinho", static_cast<unsigned>(hr));
        return;
    }
    BYTE copy[2048];
    std::memcpy(copy, data, cb);
    reinterpret_cast<APOInitBaseStruct*>(copy)->clsid = child;
    hr = obj->Initialize(cb, copy);
    if (FAILED(hr)) {
        obj->Release();
        apo_log(L"efeito anterior recusou a inicialização (0x%08X); o equalizador segue sozinho", static_cast<unsigned>(hr));
        return;
    }
    obj->QueryInterface(IID_PPV_ARGS(&m_child_rt));
    obj->QueryInterface(IID_PPV_ARGS(&m_child_cfg));
    if (!m_child_rt || !m_child_cfg) {
        if (m_child_rt) m_child_rt->Release();
        if (m_child_cfg) m_child_cfg->Release();
        m_child_rt = nullptr;
        m_child_cfg = nullptr;
        obj->Release();
        return;
    }
    m_child = obj;
}

void VolumEqApo::DropChild() {
    if (m_child_cfg) { m_child_cfg->Release(); m_child_cfg = nullptr; }
    if (m_child_rt) { m_child_rt->Release(); m_child_rt = nullptr; }
    if (m_child) { m_child->Release(); m_child = nullptr; }
    m_child_broken.store(0, std::memory_order_relaxed);
}

STDMETHODIMP VolumEqApo::Initialize(UINT32 cb, BYTE* data) {
    if (m_bIsInitialized) return APOERR_ALREADY_INITIALIZED;
    m_discovery = false;
    IPropertyStore* fx = nullptr;
    IPropertyStore* ep = nullptr;
    HRESULT hr = ValidateInitializeParameters(cb, data, CLSID_VolumEqApo, sizeof(APOInitSystemEffects2));
    if (SUCCEEDED(hr)) {
        auto* init = reinterpret_cast<APOInitSystemEffects2*>(data);
        fx = init->pAPOSystemEffectsProperties;
        ep = init->pAPOEndpointProperties;
        m_discovery = init->InitializeForDiscoveryOnly != FALSE;
    } else {
        hr = ValidateInitializeParameters(cb, data, CLSID_VolumEqApo, sizeof(APOInitSystemEffects));
        if (SUCCEEDED(hr)) {
            fx = reinterpret_cast<APOInitSystemEffects*>(data)->pAPOSystemEffectsProperties;
            ep = reinterpret_cast<APOInitSystemEffects*>(data)->pAPOEndpointProperties;
        }
        else {
            // Estrutura de outro tamanho (versão nova do Windows): roda sem as propriedades do dispositivo
            // (vale a curva do único dispositivo ativado) em vez de recusar e ficar fora do áudio.
            auto* base = reinterpret_cast<APOInitBaseStruct*>(data);
            if (!data || cb < sizeof(APOInitBaseStruct) || base->cbSize != cb || base->clsid != CLSID_VolumEqApo) {
                apo_log(L"Initialize recusado (cb=%u)", cb);
                return E_INVALIDARG;
            }
        }
    }
    ReadProps(fx, ep);
    if (!m_discovery && m_child_clsid != GUID_NULL) TryChild(m_child_clsid, cb, data);
    // O Windows cria mais de uma cópia do efeito por dispositivo (uma por cadeia de áudio): cada uma tem o
    // próprio arquivo de sinal de vida, "<guid>.<pid>-<n>.txt", senão elas brigam pelo mesmo arquivo.
    swprintf_s(m_live_file, L"%s\\%s.%lu-%ld.txt", paths().live, m_endpoint[0] ? endpoint_key(m_endpoint) : L"sem-id",
        GetCurrentProcessId(), g_serial.fetch_add(1) + 1);
    if (!m_discovery)
        apo_log(L"carregado pelo Windows (cb=%u, dispositivo %s, efeito anterior: %s)", cb,
            m_endpoint[0] ? endpoint_key(m_endpoint) : L"sem id", m_child ? L"sim" : L"não");
    m_bIsInitialized = true;
    return S_OK;
}

STDMETHODIMP VolumEqApo::IsInputFormatSupported(IAudioMediaType* out_fmt, IAudioMediaType* requested, IAudioMediaType** supported) {
    if (m_child) return m_child->IsInputFormatSupported(out_fmt, requested, supported);
    return CBaseAudioProcessingObject::IsInputFormatSupported(out_fmt, requested, supported);
}

STDMETHODIMP VolumEqApo::IsOutputFormatSupported(IAudioMediaType* in_fmt, IAudioMediaType* requested, IAudioMediaType** supported) {
    if (m_child) return m_child->IsOutputFormatSupported(in_fmt, requested, supported);
    return CBaseAudioProcessingObject::IsOutputFormatSupported(in_fmt, requested, supported);
}

bool VolumEqApo::LoadConfig(bool first) {
    VolumEqParams params;
    int rc = EqEngine::read_file(paths().config, m_endpoint, &params);
    if (rc == 1) {
        m_engine.publish(params);
        return true;
    }
    if (rc == 0 || rc == -1 || first) {
        // Sem curva para este dispositivo (ou sem arquivo): passa o som intacto.
        std::memset(&params, 0, sizeof(params));
        params.bypass = 1;
        m_engine.publish(params);
        return rc != -2;
    }
    return false;   // erro passageiro (arquivo sendo trocado): mantém a curva atual e tenta de novo
}

STDMETHODIMP VolumEqApo::LockForProcess(UINT32 nin, APO_CONNECTION_DESCRIPTOR** in_conn, UINT32 nout, APO_CONNECTION_DESCRIPTOR** out_conn) {
    HRESULT hr = CBaseAudioProcessingObject::LockForProcess(nin, in_conn, nout, out_conn);
    if (FAILED(hr)) return hr;

    UNCOMPRESSEDAUDIOFORMAT fmt = {};
    m_float = false;
    m_channels = 0;
    if (nin > 0 && in_conn && in_conn[0] && in_conn[0]->pFormat && SUCCEEDED(in_conn[0]->pFormat->GetUncompressedAudioFormat(&fmt))) {
        m_channels = fmt.dwSamplesPerFrame;
        m_frame_bytes = fmt.dwSamplesPerFrame * fmt.dwBytesPerSampleContainer;
        m_float = fmt.guidFormatType == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT && fmt.dwBytesPerSampleContainer == sizeof(float) && m_channels > 0;
    }
    if (!m_float) apo_log(L"formato que não é float 32 (%u canais); o som passa sem equalizar", m_channels);

    if (m_float) {
        m_engine.prepare(static_cast<int>(m_channels), fmt.fFramesPerSecond);
        m_seen_stamp = m_seen_size = 0;
        LoadConfig(true);
        if (m_silence) {
            AERT_Free(m_silence);
            m_silence = nullptr;
        }
        if (FAILED(AERT_Allocate(sizeof(float) * m_channels * m_silence_frames, reinterpret_cast<void**>(&m_silence))))
            m_silence = nullptr;
    }

    if (m_child_cfg) {
        HRESULT child = m_child_cfg->LockForProcess(nin, in_conn, nout, out_conn);
        if (FAILED(child)) {
            apo_log(L"efeito anterior não aceitou o formato (0x%08X); o equalizador segue sozinho", static_cast<unsigned>(child));
            DropChild();
        }
    }
    if (!m_discovery) {
        apo_log(L"processando: %.0f Hz, %u canais, float=%d", m_float ? fmt.fFramesPerSecond : 0.f, m_channels, m_float ? 1 : 0);
        StartWatch();
    }
    return S_OK;
}

STDMETHODIMP VolumEqApo::UnlockForProcess() {
    StopWatch();
    if (m_child_cfg && m_bIsLocked) m_child_cfg->UnlockForProcess();
    if (m_silence) {
        AERT_Free(m_silence);
        m_silence = nullptr;
    }
    return CBaseAudioProcessingObject::UnlockForProcess();
}

STDMETHODIMP VolumEqApo::Reset() {
    m_engine.reset();
    if (m_child) m_child->Reset();
    return CBaseAudioProcessingObject::Reset();
}

void VolumEqApo::ProcessSilence(float* dst, UINT32 frames) {
    // Silêncio na entrada, mas o filtro ainda tem cauda (um grave ressonante, por exemplo): deixa ela acabar.
    UINT32 done = 0;
    while (done < frames) {
        UINT32 n = frames - done;
        if (n > m_silence_frames) n = m_silence_frames;
        std::memset(m_silence, 0, sizeof(float) * n * m_channels);
        m_engine.process(m_silence, dst + static_cast<size_t>(done) * m_channels, static_cast<int>(n));
        done += n;
    }
}

STDMETHODIMP_(void) VolumEqApo::APOProcess(UINT32 nin, APO_CONNECTION_PROPERTY** in_conn, UINT32 nout, APO_CONNECTION_PROPERTY** out_conn) {
    if (nin < 1 || nout < 1 || !in_conn || !out_conn || !in_conn[0] || !out_conn[0]) return;
    APO_CONNECTION_PROPERTY* in = in_conn[0];
    APO_CONNECTION_PROPERTY* out = out_conn[0];
    const UINT32 frames = in->u32ValidFrameCount;

    const bool had_child = m_child_rt && m_child_broken.load(std::memory_order_relaxed) == 0;
    if (had_child) {
        if (!call_child(m_child_rt, nin, in_conn, nout, out_conn)) {
            m_child_broken.store(1, std::memory_order_relaxed);
            out->u32ValidFrameCount = frames;
            out->u32BufferFlags = in->u32BufferFlags;
        }
    } else {
        out->u32ValidFrameCount = frames;
        out->u32BufferFlags = in->u32BufferFlags;
        if (in->pBuffer != out->pBuffer && in->pBuffer && out->pBuffer && m_frame_bytes && in->u32BufferFlags == BUFFER_VALID && !m_float)
            std::memcpy(reinterpret_cast<void*>(out->pBuffer), reinterpret_cast<const void*>(in->pBuffer), static_cast<size_t>(frames) * m_frame_bytes);
    }
    if (!m_float || frames == 0 || m_channels == 0 || out->u32BufferFlags == BUFFER_INVALID) return;

    float* dst = reinterpret_cast<float*>(out->pBuffer);
    if (!dst) return;
    const bool child_alive = m_child_rt && m_child_broken.load(std::memory_order_relaxed) == 0;
    const float* src = child_alive ? dst : reinterpret_cast<const float*>(in->pBuffer);
    if (out->u32BufferFlags == BUFFER_SILENT) {
        if (m_engine.idle() || !m_silence) return;
        ProcessSilence(dst, frames);
        out->u32BufferFlags = BUFFER_VALID;
        return;
    }
    if (!src) return;
    m_engine.process(src, dst, static_cast<int>(frames));
}

void VolumEqApo::WriteLive() {
    if (!m_live_file[0]) return;
    const ULONGLONG now = GetTickCount64();
    if (!m_live_ok && now < m_live_retry) return;

    char text[512];
    const int n = std::snprintf(text, sizeof(text),
        "version=%d\r\npid=%lu\r\nrate=%.0f\r\nchannels=%u\r\nfloat=%d\r\nframes=%llu\r\nclips=%u\r\npeak=%.6f\r\nchild=%d\r\n",
        VOLUM_EQ_FILE_VERSION, GetCurrentProcessId(), m_engine.sample_rate(), m_channels, m_float ? 1 : 0,
        static_cast<unsigned long long>(m_engine.frames_done()), m_engine.clips(), m_engine.take_peak(),
        m_child_rt ? (m_child_broken.load() ? 2 : 1) : 0);
    if (n <= 0) return;

    wchar_t tmp[MAX_PATH];
    swprintf_s(tmp, L"%s.tmp", m_live_file);
    HANDLE file = CreateFileW(tmp, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, nullptr);
    bool ok = file != INVALID_HANDLE_VALUE;
    if (ok) {
        DWORD wrote = 0;
        ok = WriteFile(file, text, static_cast<DWORD>(n), &wrote, nullptr) && wrote == static_cast<DWORD>(n);
        CloseHandle(file);
        ok = ok && MoveFileExW(tmp, m_live_file, MOVEFILE_REPLACE_EXISTING);
    }
    if (!ok && m_live_ok) apo_log(L"não deu para gravar o sinal de vida (%lu)", GetLastError());
    m_live_ok = ok;
    if (!ok) m_live_retry = now + 3000;   // arquivo aberto por quem lê: tenta de novo logo
}

DWORD WINAPI VolumEqApo::WatchThread(LPVOID self) {
    auto* apo = static_cast<VolumEqApo*>(self);
    ULONGLONG next_live = 0;
    bool reported_child = false;
    for (;;) {
        if (apo->m_child_broken.load(std::memory_order_relaxed) == 1 && !reported_child) {
            apo_log(L"o efeito anterior do dispositivo falhou; o equalizador segue sozinho");
            reported_child = true;
        }

        WIN32_FILE_ATTRIBUTE_DATA info;
        uint64_t stamp = 0, size = 0;
        if (GetFileAttributesExW(paths().config, GetFileExInfoStandard, &info)) {
            stamp = (static_cast<uint64_t>(info.ftLastWriteTime.dwHighDateTime) << 32) | info.ftLastWriteTime.dwLowDateTime;
            size = (static_cast<uint64_t>(info.nFileSizeHigh) << 32) | info.nFileSizeLow;
        }
        if (stamp != apo->m_seen_stamp || size != apo->m_seen_size) {
            // Só marca como visto se leu direito; se o arquivo estava sendo trocado, tenta no próximo ciclo.
            if (apo->LoadConfig(false)) {
                apo->m_seen_stamp = stamp;
                apo->m_seen_size = size;
            }
        }

        const ULONGLONG now = GetTickCount64();
        if (now >= next_live) {
            apo->WriteLive();
            next_live = now + 1000;
        }
        if (WaitForSingleObject(apo->m_stop, 25) == WAIT_OBJECT_0) break;
    }
    return 0;
}

void VolumEqApo::StartWatch() {
    StopWatch();
    if (!m_stop || !m_float) return;
    ResetEvent(m_stop);
    m_watch = CreateThread(nullptr, 0, WatchThread, this, 0, nullptr);
    if (!m_watch) apo_log(L"não deu para criar a thread de apoio (%lu)", GetLastError());
}

void VolumEqApo::StopWatch() {
    if (!m_watch) return;
    SetEvent(m_stop);
    WaitForSingleObject(m_watch, INFINITE);
    CloseHandle(m_watch);
    m_watch = nullptr;
    if (m_live_file[0]) {
        // Parou de processar: o sinal de vida some na hora em vez de envelhecer.
        DeleteFileW(m_live_file);
    }
}

// ---------------------------------------------------------------------------------------------
// COM

struct Factory : IClassFactory {
    std::atomic<long> ref{ 1 };
    STDMETHOD(QueryInterface)(REFIID riid, void** ppv) override {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IClassFactory) {
            *ppv = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHOD_(ULONG, AddRef)() override { return static_cast<ULONG>(ref.fetch_add(1) + 1); }
    STDMETHOD_(ULONG, Release)() override {
        long n = ref.fetch_sub(1) - 1;
        if (n == 0) delete this;
        return static_cast<ULONG>(n);
    }
    STDMETHOD(CreateInstance)(IUnknown* outer, REFIID riid, void** ppv) override {
        if (!ppv) return E_POINTER;
        *ppv = nullptr;
        if (outer && riid != IID_IUnknown) return CLASS_E_NOAGGREGATION;   // regra do COM para agregação
        auto* obj = new (std::nothrow) VolumEqApo(outer);
        if (!obj) return E_OUTOFMEMORY;
        IUnknown* inner = obj->Inner();
        HRESULT hr = inner->QueryInterface(riid, ppv);
        inner->Release();
        return hr;
    }
    STDMETHOD(LockServer)(BOOL) override { return S_OK; }
};

BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}

static HRESULT set_string(HKEY root, const wchar_t* path, const wchar_t* name, const wchar_t* value) {
    HKEY key = nullptr;
    LONG rc = RegCreateKeyExW(root, path, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &key, nullptr);
    if (rc != ERROR_SUCCESS) return HRESULT_FROM_WIN32(rc);
    rc = RegSetValueExW(key, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value), static_cast<DWORD>((wcslen(value) + 1) * sizeof(wchar_t)));
    RegCloseKey(key);
    return HRESULT_FROM_WIN32(rc);
}

static HRESULT RegisterClassKey() {
    wchar_t guid[64];
    if (!StringFromGUID2(CLSID_VolumEqApo, guid, 64)) return E_FAIL;
    wchar_t dll[MAX_PATH];
    if (!GetModuleFileNameW(g_module, dll, MAX_PATH)) return HRESULT_FROM_WIN32(GetLastError());
    wchar_t path[256];
    swprintf_s(path, L"SOFTWARE\\Classes\\CLSID\\%s", guid);
    HRESULT hr = set_string(HKEY_LOCAL_MACHINE, path, nullptr, L"Volum Equalizer");
    if (FAILED(hr)) return hr;
    swprintf_s(path, L"SOFTWARE\\Classes\\CLSID\\%s\\InprocServer32", guid);
    hr = set_string(HKEY_LOCAL_MACHINE, path, nullptr, dll);
    if (FAILED(hr)) return hr;
    return set_string(HKEY_LOCAL_MACHINE, path, L"ThreadingModel", L"Both");
}

static void DeleteClassKey() {
    wchar_t guid[64];
    if (!StringFromGUID2(CLSID_VolumEqApo, guid, 64)) return;
    wchar_t path[256];
    swprintf_s(path, L"SOFTWARE\\Classes\\CLSID\\%s", guid);
    RegDeleteTreeW(HKEY_LOCAL_MACHINE, path);
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** ppv) {
    if (!ppv) return E_POINTER;
    *ppv = nullptr;
    if (clsid != CLSID_VolumEqApo) return CLASS_E_CLASSNOTAVAILABLE;
    auto* factory = new (std::nothrow) Factory();
    if (!factory) return E_OUTOFMEMORY;
    HRESULT hr = factory->QueryInterface(riid, ppv);
    factory->Release();
    return hr;
}

STDAPI DllCanUnloadNow() { return S_FALSE; }

STDAPI DllRegisterServer() {
    HRESULT hr = RegisterClassKey();
    if (FAILED(hr)) return hr;
    return RegisterAPO(g_reg);
}

STDAPI DllUnregisterServer() {
    UnregisterAPO(CLSID_VolumEqApo);
    DeleteClassKey();
    return S_OK;
}

// ---------------------------------------------------------------------------------------------
// Teste de fumaça (o eq_test e o Volum chamam): cria o efeito como o Windows cria e processa um bloco.

extern "C" __declspec(dllexport) long volum_eq_apo_instances(void) { return g_instances.load(); }

// Como o motor de áudio do Windows cria o efeito: agregado dentro de um objeto dele.
namespace {
struct FakeOuter : IUnknown {
    std::atomic<long> ref{ 1 };
    IUnknown* inner = nullptr;
    STDMETHOD(QueryInterface)(REFIID riid, void** ppv) override {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown) { *ppv = static_cast<IUnknown*>(this); AddRef(); return S_OK; }
        return inner ? inner->QueryInterface(riid, ppv) : E_NOINTERFACE;
    }
    STDMETHOD_(ULONG, AddRef)() override { return static_cast<ULONG>(ref.fetch_add(1) + 1); }
    STDMETHOD_(ULONG, Release)() override {
        long n = ref.fetch_sub(1) - 1;
        if (n == 0) { if (inner) inner->Release(); delete this; }
        return static_cast<ULONG>(n);
    }
};

const wchar_t* smoke_aggregated(HRESULT* out_hr) {
    *out_hr = S_OK;
    IClassFactory* factory = nullptr;
    HRESULT hr = DllGetClassObject(CLSID_VolumEqApo, IID_PPV_ARGS(&factory));
    if (FAILED(hr)) { *out_hr = hr; return L"agregado: DllGetClassObject"; }
    auto* outer = new FakeOuter();
    hr = factory->CreateInstance(outer, IID_IUnknown, reinterpret_cast<void**>(&outer->inner));
    IAudioProcessingObject* bad = nullptr;
    HRESULT rule = factory->CreateInstance(outer, __uuidof(IAudioProcessingObject), reinterpret_cast<void**>(&bad));
    factory->Release();
    if (FAILED(hr) || !outer->inner) { outer->Release(); *out_hr = hr; return L"agregado: CreateInstance recusou"; }
    if (rule != CLASS_E_NOAGGREGATION || bad) { if (bad) bad->Release(); outer->Release(); *out_hr = rule; return L"agregado: aceitou IID errado"; }

    IAudioProcessingObject* apo = nullptr;
    IAudioProcessingObjectRT* rt = nullptr;
    IAudioProcessingObjectConfiguration* cfg = nullptr;
    outer->QueryInterface(IID_PPV_ARGS(&apo));
    outer->QueryInterface(IID_PPV_ARGS(&rt));
    outer->QueryInterface(IID_PPV_ARGS(&cfg));
    const wchar_t* fail = nullptr;
    if (!apo || !rt || !cfg) fail = L"agregado: QueryInterface";
    // Um QueryInterface feito pelo efeito precisa voltar para o objeto de fora (regra da agregação).
    IUnknown* back = nullptr;
    if (!fail && (FAILED(apo->QueryInterface(IID_PPV_ARGS(&back))) || back != static_cast<IUnknown*>(outer))) fail = L"agregado: identidade";
    if (back) back->Release();
    if (!fail) {
        APOInitSystemEffects2 init = {};
        init.APOInit.cbSize = sizeof(init);
        init.APOInit.clsid = CLSID_VolumEqApo;
        init.InitializeForDiscoveryOnly = TRUE;
        hr = apo->Initialize(sizeof(init), reinterpret_cast<BYTE*>(&init));
        if (FAILED(hr)) { fail = L"agregado: Initialize"; *out_hr = hr; }
    }
    if (!fail) {
        UNCOMPRESSEDAUDIOFORMAT fmt = {};
        fmt.guidFormatType = KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
        fmt.dwSamplesPerFrame = 2;
        fmt.dwBytesPerSampleContainer = 4;
        fmt.dwValidBitsPerSample = 32;
        fmt.fFramesPerSecond = 48000;
        fmt.dwChannelMask = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT;
        IAudioMediaType* media = nullptr;
        hr = CreateAudioMediaTypeFromUncompressedAudioFormat(&fmt, &media);
        static float buffer[256 * 2];
        APO_CONNECTION_DESCRIPTOR desc = {};
        desc.Type = APO_CONNECTION_BUFFER_TYPE_ALLOCATED;
        desc.pBuffer = reinterpret_cast<UINT_PTR>(buffer);
        desc.u32MaxFrameCount = 256;
        desc.pFormat = media;
        desc.u32Signature = APO_CONNECTION_DESCRIPTOR_SIGNATURE;
        APO_CONNECTION_DESCRIPTOR* descs[] = { &desc };
        if (SUCCEEDED(hr)) hr = cfg->LockForProcess(1, descs, 1, descs);
        if (FAILED(hr)) { fail = L"agregado: LockForProcess"; *out_hr = hr; }
        else {
            APO_CONNECTION_PROPERTY prop = {};
            prop.pBuffer = reinterpret_cast<UINT_PTR>(buffer);
            prop.u32ValidFrameCount = 256;
            prop.u32BufferFlags = BUFFER_VALID;
            prop.u32Signature = APO_CONNECTION_PROPERTY_SIGNATURE;
            APO_CONNECTION_PROPERTY* props[] = { &prop };
            rt->APOProcess(1, props, 1, props);
            cfg->UnlockForProcess();
        }
        if (media) media->Release();
    }
    if (apo) apo->Release();
    if (rt) rt->Release();
    if (cfg) cfg->Release();
    if (outer->ref.load() != 1 && !fail) fail = L"agregado: contagem de referências";
    outer->Release();
    return fail;
}
} // namespace

extern "C" __declspec(dllexport) int volum_eq_apo_smoke(wchar_t* error, int error_chars) {
    auto fail = [&](const wchar_t* text, HRESULT hr) {
        if (error && error_chars > 0) swprintf_s(error, static_cast<size_t>(error_chars), L"%s (0x%08X)", text, static_cast<unsigned>(hr));
        return 1;
    };
    const long before = g_instances.load();
    IClassFactory* factory = nullptr;
    HRESULT hr = DllGetClassObject(CLSID_VolumEqApo, IID_PPV_ARGS(&factory));
    if (FAILED(hr)) return fail(L"DllGetClassObject", hr);
    IAudioProcessingObject* apo = nullptr;
    hr = factory->CreateInstance(nullptr, IID_PPV_ARGS(&apo));
    factory->Release();
    if (FAILED(hr)) return fail(L"CreateInstance", hr);

    IAudioProcessingObjectRT* rt = nullptr;
    IAudioProcessingObjectConfiguration* cfg = nullptr;
    IAudioSystemEffects2* fx2 = nullptr;
    apo->QueryInterface(IID_PPV_ARGS(&rt));
    apo->QueryInterface(IID_PPV_ARGS(&cfg));
    apo->QueryInterface(IID_PPV_ARGS(&fx2));
    auto cleanup = [&]() {
        if (rt) rt->Release();
        if (cfg) cfg->Release();
        if (fx2) fx2->Release();
        apo->Release();
    };
    if (!rt || !cfg || !fx2) {
        cleanup();
        return fail(L"QueryInterface", E_NOINTERFACE);
    }

    APOInitSystemEffects2 init = {};
    init.APOInit.cbSize = sizeof(init);
    init.APOInit.clsid = CLSID_VolumEqApo;
    init.InitializeForDiscoveryOnly = TRUE;   // sem thread de apoio nem arquivos
    hr = apo->Initialize(sizeof(init), reinterpret_cast<BYTE*>(&init));
    if (FAILED(hr)) { cleanup(); return fail(L"Initialize", hr); }
    HNSTIME latency = 1;
    apo->GetLatency(&latency);
    if (latency != 0) { cleanup(); return fail(L"latência não é zero", E_FAIL); }

    UNCOMPRESSEDAUDIOFORMAT fmt = {};
    fmt.guidFormatType = KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
    fmt.dwSamplesPerFrame = 2;
    fmt.dwBytesPerSampleContainer = 4;
    fmt.dwValidBitsPerSample = 32;
    fmt.fFramesPerSecond = 48000;
    fmt.dwChannelMask = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT;
    IAudioMediaType* media = nullptr;
    hr = CreateAudioMediaTypeFromUncompressedAudioFormat(&fmt, &media);
    if (FAILED(hr)) { cleanup(); return fail(L"formato", hr); }

    IAudioMediaType* suggested = nullptr;
    hr = apo->IsInputFormatSupported(nullptr, media, &suggested);
    if (suggested) suggested->Release();
    if (hr != S_OK) { media->Release(); cleanup(); return fail(L"float 32 recusado", hr); }

    static float buffer[480 * 2];
    for (int i = 0; i < 480 * 2; i++) buffer[i] = static_cast<float>(std::sin(i * 0.05) * 0.25);
    APO_CONNECTION_DESCRIPTOR desc = {};
    desc.Type = APO_CONNECTION_BUFFER_TYPE_ALLOCATED;
    desc.pBuffer = reinterpret_cast<UINT_PTR>(buffer);
    desc.u32MaxFrameCount = 480;
    desc.pFormat = media;
    desc.u32Signature = APO_CONNECTION_DESCRIPTOR_SIGNATURE;
    APO_CONNECTION_DESCRIPTOR* descs[] = { &desc };
    hr = cfg->LockForProcess(1, descs, 1, descs);
    if (FAILED(hr)) { media->Release(); cleanup(); return fail(L"LockForProcess", hr); }

    APO_CONNECTION_PROPERTY prop = {};
    prop.pBuffer = reinterpret_cast<UINT_PTR>(buffer);
    prop.u32ValidFrameCount = 480;
    prop.u32BufferFlags = BUFFER_VALID;
    prop.u32Signature = APO_CONNECTION_PROPERTY_SIGNATURE;
    APO_CONNECTION_PROPERTY* props[] = { &prop };
    for (int i = 0; i < 4; i++) rt->APOProcess(1, props, 1, props);
    prop.u32BufferFlags = BUFFER_SILENT;
    rt->APOProcess(1, props, 1, props);
    bool finite = true;
    for (float s : buffer) if (!std::isfinite(s)) finite = false;

    cfg->UnlockForProcess();
    media->Release();
    cleanup();

    if (!finite) return fail(L"processou não-finito", E_FAIL);
    if (g_instances.load() != before) return fail(L"vazamento de instância", E_FAIL);
    HRESULT agg_hr = S_OK;
    if (const wchar_t* agg = smoke_aggregated(&agg_hr)) return fail(agg, agg_hr);
    if (g_instances.load() != before) return fail(L"vazamento de instância (agregado)", E_FAIL);
    if (error && error_chars > 0) error[0] = 0;
    return 0;
}
