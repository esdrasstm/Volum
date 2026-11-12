using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Timer = System.Windows.Forms.Timer;

namespace Volum;

/// <summary>
/// Janela sem borda que hospeda a interface (HTML/CSS/JS) via WebView2.
/// C# ⇄ JS conversam por mensagens JSON (PostWebMessageAsJson / chrome.webview.postMessage).
/// </summary>
sealed class MixerForm : Form
{
    // Largura da janela em "pixels CSS". Se mudar aqui, mude também em style.css (.card { width })
    const int WidthDip = 360;   // igual ao .card { width } do style.css
    const int MarginDip = 12;      // distância da borda da área de trabalho
    const int CloseMs = 130;       // acompanha --exit-ms do CSS (120ms)
    const string SiteUrl = "https://esdrasstm.github.io/Volum/";

    /// <summary>Abre no navegador padrão: o site, o formulário de sugestões ("feedback", levando a versão
    /// do app e do Windows, que ajuda nos bugs) ou o código no GitHub ("github").</summary>
    internal static void OpenSite(string? section)
    {
        var url = section switch
        {
            "feedback" => $"{SiteUrl}?v={Uri.EscapeDataString(Updater.Version)}&w={Environment.OSVersion.Version.Build}#sugestoes",
            "github" => "https://github.com/esdrasstm/Volum",
            "store" => "ms-windows-store://pdp/?productid=9PKPDGQ211L4",   // abre o app da Microsoft Store na página do Volum
            "kofi" => "https://ko-fi.com/volum",                            // apoiar o Volum (quem está fora do Brasil)
            // pesquisa sobre os temas (novidades e Configurações → Temas): leva a versão, o tema em uso e o idioma
            "survey" => $"{SiteUrl}temas.html?v={Uri.EscapeDataString(Updater.Version)}&skin={Uri.EscapeDataString(AppSettings.Current.Skin)}&lang={Uri.EscapeDataString(AppSettings.Current.Language)}",
            _ => SiteUrl,
        };
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    static string? s_testNewsSeen;   // cópia de teste: novidades vistas só nesta execução

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    readonly WebView2 _web = new();
    readonly HashSet<string> _iconsSent = new();
    readonly Timer _sync = new() { Interval = 1000 };
    readonly Timer _meter = new() { Interval = 60 };
    readonly Timer _idle = new() { Interval = 45_000 };   // depois de fechado, descarta o WebView para liberar RAM
    readonly AppSettings _settings = AppSettings.Current;   // a mesma da janela de configurações
    AudioService? _audio;

    bool _initStarted, _ready, _wantOpen, _pendingPresent, _shown, _closing, _contentShown;
    // Última altura conhecida: a janela já aparece no tamanho certo, mesmo depois de ser descartada
    static int s_heightDip = 420;
    // Quanto o WebView desenha a mais que a escala do monitor (ex.: "Tamanho do texto" do Windows em 125%); vem do JS ("dpr")
    static float _webScale = 1f;
    int _slideOffsetPx;
    Point _anchor;                 // canto inferior direito (em pixels) onde a janela assenta
    DateTime _presentedAt, _contentAt;

    public DateTime LastHiddenUtc { get; private set; } = DateTime.MinValue;
    /// <summary>Visível e não está fechando.</summary>
    public bool IsOpen => _shown && !_closing;

    /// <summary>
    /// Um clique na bandeja só fecha depois que o conteúdo apareceu (e não logo em seguida):
    /// assim um segundo clique de impaciência durante o carregamento não fecha a janela.
    /// </summary>
    public bool CanCloseByClick => _contentShown && (DateTime.UtcNow - _contentAt).TotalMilliseconds > 400;

    public MixerForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Text = "Volum";
        Size = new Size((int)Math.Round(WidthDip * DeviceDpi / 96f * _webScale), 300);
        _web.Dock = DockStyle.Fill;
        ApplyBackdrop();
        Controls.Add(_web);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Water.Changed += OnWaterChanged;
        if (_settings.LightMode) _meter.Interval = MeterLiteMs;

        _sync.Tick += (_, _) => PushState();
        _meter.Tick += (_, _) => PushPeaks();
        _idle.Tick += (_, _) =>
        {
            _idle.Stop();
            if (_shown) return;
            if (KeepWarm) _ = SleepAsync();   // fica "dormindo": abre na hora da próxima vez
            else Dispose();                  // modo leve: descarta tudo e libera a memória
        };

        Deactivate += (_, _) =>
        {
            if (Program.DevMode || !_shown || _closing) return;
            if ((DateTime.UtcNow - _presentedAt).TotalMilliseconds < 300) return;
            DevLog.Write("perdeu o foco: fechando"); _ = CloseAnimatedAsync();
        };
    }

    // ---------- janela: sem entrada no Alt+Tab, cantos arredondados e sombra do Windows ----------

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int square = 1; // DWMWCP_DONOTROUND: os cantos (raio 15, como no Figma) são desenhados pelo CSS
            DwmSetWindowAttribute(Handle, 33, ref square, sizeof(int));
        }
        catch { }
        ApplyBackdrop();
    }

    // Acrílico nativo só existe a partir do Windows 11 22H2 (build 22621); antes disso, visual sólido.
    static readonly bool GlassSupported = Environment.OSVersion.Version.Build >= 22621;
    // Sem acrílico desde o visual do Figma (cartão sólido); volta só no tema Vidro (Windows 11 22H2+, fora do modo leve)
    bool UseGlass => GlassSupported && _settings.Skin == "glass" && !_settings.LightMode;

    const int MeterMs = 60, MeterLiteMs = 150;   // medidores: normal / modo leve

    /// <summary>
    /// Vidro (acrílico do Windows 11 atrás da interface) ou fundo sólido (modo leve / Windows antigo).
    /// Pode ser chamado a qualquer momento: troca na hora quando a chave "Modo leve" muda.
    /// </summary>
    void ApplyBackdrop()
    {
        bool dark = IsDarkTheme();

        // Visual do Figma: o cartão (fundo #111111, borda em degradê, cantos de 15) é desenhado pelo CSS.
        // A janela fica transparente (preto = transparente para o DWM, WebView transparente), sem acrílico.
        BackColor = Color.Black;
        _web.DefaultBackgroundColor = Color.Transparent;

        if (!IsHandleCreated) return;
        try
        {
            int d = dark ? 1 : 0;
            DwmSetWindowAttribute(Handle, 20, ref d, sizeof(int));          // DWMWA_USE_IMMERSIVE_DARK_MODE

            var m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };   // janela inteira transparente
            DwmExtendFrameIntoClientArea(Handle, ref m);

            if (GlassSupported)
            {
                int backdrop = UseGlass ? 3 : 1;                              // 3 = acrílico (tema Vidro); 1 = nenhum
                DwmSetWindowAttribute(Handle, 38, ref backdrop, sizeof(int)); // DWMWA_SYSTEMBACKDROP_TYPE
                int corner = UseGlass ? 2 : 1;                                // vidro: o Windows arredonda (o acrílico é da janela inteira)
                DwmSetWindowAttribute(Handle, 33, ref corner, sizeof(int));   // DWMWA_WINDOW_CORNER_PREFERENCE
            }
        }
        catch { }
    }

    bool IsDarkTheme() =>
        _settings.Theme == ThemeMode.System ? WindowsIsDark() : _settings.Theme == ThemeMode.Dark;

    /// <summary>
    /// Tema "Sistema": o modo do Windows (o da barra de tarefas e do Iniciar, onde o mixer aparece).
    /// É a única fonte: o C# manda o resultado para a interface (settings.systemDark).
    /// </summary>
    internal static bool WindowsIsDark()
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var v = k?.GetValue("SystemUsesLightTheme") ?? k?.GetValue("AppsUseLightTheme");
        return v is int i && i == 0;
    }

    // Trocou o tema do Windows com o mixer aberto: atualiza vidro e interface na hora
    void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.General || IsDisposed) return;
        BeginInvoke(new Action(() =>
        {
            ApplyBackdrop();
            Post(new { type = "systemTheme", dark = WindowsIsDark() });
        }));
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [StructLayout(LayoutKind.Sequential)]
    struct MARGINS { public int Left, Right, Top, Bottom; }

    // ---------- abrir / fechar ----------

    // ---------- WebView "dormindo" ----------
    // Iniciar o WebView2 do zero leva ~1 s (era o que deixava a abertura lenta). Fora do modo leve,
    // ele não é descartado: fica suspenso (0% de CPU) e com a memória no mínimo, e acorda no clique.
    bool KeepWarm => !_settings.LightMode && !Program.DevMode;
    bool _asleep;

    async Task SleepAsync()
    {
        var core = _web.CoreWebView2;
        if (core == null || _shown || _asleep) return;
        try
        {
            _web.Visible = false;   // o WebView2 só aceita suspender quando está marcado como invisível
            core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            _asleep = await core.TrySuspendAsync();
            DevLog.Write($"WebView2 dormindo: {_asleep}");
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
        BeginInvoke(new Action(MemoryTrim.Run));
    }

    void Wake()
    {
        var core = _web.CoreWebView2;
        if (core == null) return;
        try
        {
            if (_asleep) core.Resume();
            core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
            _web.Visible = true;
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
        _asleep = false;
    }

    /// <summary>Inicia o WebView2 escondido logo depois que o app abre, para o 1º clique já ser rápido.</summary>
    public void Prewarm()
    {
        if (IsDisposed || _initStarted || !KeepWarm) return;
        _initStarted = true;
        DevLog.Write("pré-aquecendo o WebView2");
        _ = InitWebViewAsync();
    }

    async Task SleepAfterPrewarmAsync()
    {
        await Task.Delay(1500);   // deixa a página terminar de pintar antes de suspender
        if (!_shown && !_wantOpen && !IsDisposed) await SleepAsync();
    }

    public async void Open(bool fromHotkey = false)
    {
        if (IsDisposed) return;
        _idle.Stop();
        Wake();
        _wantOpen = true;
        // Pelo atalho, abre na tela do app que está na frente (o jogo), não onde o mouse estiver
        _targetScreen = fromHotkey ? Screen.FromHandle(GetForegroundWindow()) : null;

        // A janela (vidro vazio) aparece na hora do clique; o conteúdo surge quando estiver pronto.
        // A frio (WebView2 descartado), iniciar o WebView2 leva ~1 s.
        bool wasShown = _shown;
        if (!wasShown) ShowShell();

        if (!_initStarted)
        {
            _initStarted = true;
            await InitWebViewAsync();
        }
        else if (_ready && !wasShown)
        {
            BeginOpen();
        }
    }

    Screen? _targetScreen;

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    void ShowShell()
    {
        float k = DeviceDpi / 96f * _webScale;
        var wa = (_targetScreen ?? Screen.FromPoint(Cursor.Position)).WorkingArea;
        _anchor = new Point(wa.Right - (int)(MarginDip * k), wa.Bottom - (int)(MarginDip * k));

        _shown = true;
        _closing = false;
        _contentShown = false;
        _slideOffsetPx = 0;
        _presentedAt = DateTime.UtcNow;
        Reposition();
        DevLog.Write("janela visível (vazia)");
        Show();
        Activate();
    }

    async Task InitWebViewAsync()
    {
        try
        {
            _ = Handle; // cria a janela (ainda invisível) para o WebView2 poder inicializar

            DevLog.Write("WebView2: criando ambiente");
            var env = await CoreWebView2Environment.CreateAsync(null, WebViewDataFolder);
            DevLog.Write("WebView2: ambiente pronto");
            await _web.EnsureCoreWebView2Async(env);
            DevLog.Write("WebView2: controle pronto");

            var core = _web.CoreWebView2;
            var st = core.Settings;
            st.AreDefaultContextMenusEnabled = Program.DevMode;
            st.AreDevToolsEnabled = Program.DevMode;
            st.AreBrowserAcceleratorKeysEnabled = Program.DevMode;
            st.IsStatusBarEnabled = false;
            st.IsZoomControlEnabled = false;

            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith("https://app.volum/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            core.WebMessageReceived += OnWebMessage;

            // https://app.volum/  ->  pasta "wwwroot" ao lado do .exe
            // No --dev usa a wwwroot do projeto (a que você edita no VS Code) e recarrega ao salvar.
            var sourceRoot = Program.DevMode ? FindSourceWwwroot() : null;
            core.SetVirtualHostNameToFolderMapping(
                "app.volum",
                sourceRoot ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"),
                CoreWebView2HostResourceAccessKind.Allow);
            if (sourceRoot != null) WatchForChanges(sourceRoot);

            // Sem cache só no --dev (editou o CSS/JS, recarregou, já vale). Na versão instalada
            // a interface não muda entre aberturas, e essas duas chamadas atrasavam a abertura.
            if (Program.DevMode)
            {
                await core.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
                await core.CallDevToolsProtocolMethodAsync("Network.setCacheDisabled", "{\"cacheDisabled\":true}");
            }

            DevLog.Write("carregando a interface");
            core.Navigate("https://app.volum/index.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Strings.Get(_settings.Language, "webviewError") + "\n\n" + ex.Message,
                "Volum", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Dispose();
        }
    }

    // ---------- --dev: recarregar a interface ao salvar no VS Code ----------

    FileSystemWatcher? _watcher;
    Timer? _reloadDebounce;

    // Sobe a partir de bin\Release\net8.0-windows\ até achar a pasta do projeto (onde está o Volum.csproj)
    /// <summary>Cache do WebView2 (o mesmo para o mixer e a janela de configurações). A cópia de teste
    /// (preview / --dev / pasta bin) usa outra pasta: se usasse %LocalAppData%\Volum, o instalador acharia
    /// que o Volum já está instalado.</summary>
    internal static string WebViewDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Updater.IsRelease ? "Volum" : "VolumDev", "WebView2");

    internal static string? FindSourceWwwroot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
        {
            var www = Path.Combine(dir.FullName, "wwwroot");
            if (File.Exists(Path.Combine(dir.FullName, "Volum.csproj")) && Directory.Exists(www)) return www;
        }
        return null;
    }

    void WatchForChanges(string folder)
    {
        // Salvar costuma gerar vários eventos seguidos: espera 200 ms de calma e recarrega uma vez
        _reloadDebounce = new Timer { Interval = 200 };
        _reloadDebounce.Tick += (_, _) =>
        {
            _reloadDebounce.Stop();
            DevLog.Write("arquivo salvo: recarregando a interface");
            _web.CoreWebView2?.Reload();
        };

        _watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = true };
        FileSystemEventHandler changed = (_, _) =>
        {
            if (IsDisposed) return;
            BeginInvoke(new Action(() => { _reloadDebounce.Stop(); _reloadDebounce.Start(); }));
        };
        _watcher.Changed += changed;
        _watcher.Created += changed;
        _watcher.Renamed += (s, e) => changed(s, e);
        _watcher.EnableRaisingEvents = true;
    }

    void BeginOpen()
    {
        _wantOpen = false;
        _closing = false;

        _audio?.Dispose();
        DevLog.Write("abrindo: lendo o áudio");
        // Sem escutar o microfone: no visual do Figma a linha dele não tem medidor (e o Windows deixaria de mostrar "microfone em uso")
        _audio = new AudioService(_iconsSent, listenMic: false);

        // A interface renderiza e responde com "resize"; só então a janela aparece.
        _pendingPresent = true;
        PostSettings();
        PushState();
        _sync.Start();
        _meter.Interval = _settings.LightMode ? MeterLiteMs : MeterMs;   // o modo leve pode ter mudado na janela de configurações
        _meter.Start();

        DevLog.Write("abrindo: estado enviado, esperando o tamanho");
        _ = FallbackPresentAsync();
    }

    async Task FallbackPresentAsync()
    {
        await Task.Delay(1500);
        if (_pendingPresent && !IsDisposed)
        {
            _pendingPresent = false;
            Present();
        }
    }

    // O conteúdo está pronto: a interface toca a animação de entrada (CSS) dentro da janela já visível.
    // A janela não desliza mais (mover a janela com o WebView a cada 10 ms engasgava a animação).
    void Present()
    {
        if (IsDisposed) return;
        if (!_shown) ShowShell();
        _contentShown = true;
        _contentAt = DateTime.UtcNow;
        DevLog.Write("conteúdo visível");
        Post(new { type = "open" });
    }

    public async Task CloseAnimatedAsync()
    {
        if (!_shown || _closing || IsDisposed) return;
        _closing = true;
        _wantOpen = false;            // fechou durante o carregamento: não reabre quando terminar
        _pendingPresent = false;
        _sync.Stop();
        _meter.Stop();
        Post(new { type = "close" });  // a interface toca a animação de saída (CSS)
        if (_settings.LightMode) { HideNow(); return; }

        int to = (int)Math.Round(6 * DeviceDpi / 96f * _webScale);
        await Tween(CloseMs, EaseInCubic, t => { _slideOffsetPx = (int)Math.Round(to * t); Reposition(); });
        HideNow();
    }

    void HideNow()
    {
        if (IsDisposed) return;
        _audio?.Dispose();
        _audio = null;
        GlobalHotkey.Resume();   // fechou no meio da gravação de um atalho novo: volta o antigo
        Hide();
        _shown = false;
        _closing = false;
        _contentShown = false;
        _slideOffsetPx = 0;
        LastHiddenUtc = DateTime.UtcNow;
        _idle.Stop();
        _idle.Start();
        BeginInvoke(new Action(MemoryTrim.Run));
    }

    // ---------- tamanho / posição ----------

    void OnResize(double heightDip)
    {
        s_heightDip = Math.Max(60, (int)Math.Ceiling(heightDip));
        Reposition();
        if (_pendingPresent)
        {
            _pendingPresent = false;
            Present();
        }
    }

    // A janela cresce/encolhe sempre ancorada no canto inferior direito
    void Reposition()
    {
        if (IsDisposed) return;
        float k = DeviceDpi / 96f * _webScale;
        int w = (int)Math.Round(WidthDip * k);
        int h = (int)Math.Round(s_heightDip * k);
        SetBounds(_anchor.X - w, _anchor.Y - h + _slideOffsetPx, w, h);
    }

    static async Task Tween(int ms, Func<double, double> ease, Action<double> step)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            double t = Math.Min(1.0, sw.ElapsedMilliseconds / (double)ms);
            step(ease(t));
            if (t >= 1.0) break;
            await Task.Delay(10);
        }
    }

    static double EaseInCubic(double t) => t * t * t;

    // ---------- conversa com a interface ----------

    void Post(object message)
    {
        try
        {
            _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    void PostSettings()
    {
        ApplyBackdrop();     // o tema do Windows pode ter mudado desde a última abertura
        Post(new
        {
            type = "settings",
            theme = _settings.Theme.ToString().ToLowerInvariant(),
            autostart = AutoStart.IsEnabled,
            glass = false,   // visual do Figma: cartão sólido, sem os estilos de vidro antigos
            acrylic = UseGlass,   // tema Vidro com o acrílico do Windows atrás (sem ele, o CSS usa um vidro mais opaco)
            lite = _settings.LightMode,
            skin = _settings.Skin,
            language = Strings.Normalize(_settings.Language),
            systemDark = WindowsIsDark(),
            hotkey = new { mods = _settings.HotkeyMods, key = _settings.HotkeyKey },
            version = Updater.Version,                 // a interface mostra as novidades se esta versão
            // Cópia de teste (pasta bin, fora da Store/instalação): as novidades aparecem uma vez a cada vez que o app abre,
            // para dar para testar o pop-up sem ficar apagando o que já foi visto
            newsSeen = Updater.IsRelease ? _settings.LastSeenVersion : s_testNewsSeen,
            store = Updater.IsStore,                   // pela Store: sem a novidade "está na Store"      // ainda não foi vista
            water = WaterDto()
        });
    }

    // Lembrete de água: configuração e quanto já bebeu hoje
    static object WaterDto()
    {
        var w = Water.Data;
        return new { enabled = w.Enabled, goalMl = w.GoalMl, intervalMin = w.IntervalMin, cupMl = w.CupMl, todayMl = w.TodayMl };
    }

    // O contador mudou por fora (ex.: "Bebi um copo" no aviso): o mixer aberto se atualiza
    void OnWaterChanged()
    {
        if (!IsDisposed) Post(new { type = "water", water = WaterDto() });
    }

    void PushState()
    {
        if (_audio == null) return;
        try
        {
            Post(_audio.Snapshot());
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            // Sem dispositivo de áudio, por exemplo: manda estado vazio para a janela abrir mesmo assim
            Post(new StateDto("state", new MasterDto(0, false, 0), new List<AppDto>()));
        }
    }

    void PushPeaks()
    {
        try
        {
            if (_audio != null) Post(_audio.Peaks());
        }
        catch { /* sessão pode sumir entre um tick e outro */ }
    }

    void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;

            switch (root.GetProperty("type").GetString())
            {
                case "dpr":   // escala real do WebView (monitor × tamanho do texto do Windows): a janela acompanha
                {
                    double dpr = root.GetProperty("v").GetDouble();
                    float extra = (float)Math.Clamp(dpr / (DeviceDpi / 96.0), 0.5, 4.0);
                    if (Math.Abs(extra - _webScale) > 0.01f) { _webScale = extra; if (_shown) Reposition(); }
                    break;
                }

                case "ready":
                    DevLog.Write("interface pronta (ready)");
                    _ready = true;
                    // Página (re)carregada, por exemplo com F5 no modo --dev: a interface perdeu
                    // os ícones e o estado, então tudo precisa ser enviado de novo.
                    _iconsSent.Clear();
                    if (_wantOpen) BeginOpen();
                    else if (_shown)
                    {
                        PostSettings();
                        PushState();
                        Present();
                    }
                    else _ = SleepAfterPrewarmAsync();   // pré-aquecido e ninguém abriu: vai dormir
                    break;

                case "resize":
                    OnResize(root.GetProperty("height").GetDouble());
                    break;

                case "setVolume":
                    _audio?.SetVolume(
                        root.GetProperty("id").GetString()!,
                        (float)root.GetProperty("value").GetDouble());
                    break;

                case "setMute":
                    _audio?.SetMute(
                        root.GetProperty("id").GetString()!,
                        root.GetProperty("muted").GetBoolean());
                    break;

                case "setMicDevice":   // escolheu outro microfone na lista: vira o padrão do Windows
                    try
                    {
                        _audio?.SetDefaultMic(root.GetProperty("id").GetString()!);
                    }
                    catch (Exception ex) { Debug.WriteLine(ex); }
                    PushState();
                    break;

                case "setOutputDevice":   // escolheu outra saída (fone → soundbar): vira a padrão do Windows
                    try
                    {
                        _audio?.SetDefaultOutput(root.GetProperty("id").GetString()!);
                    }
                    catch (Exception ex) { Debug.WriteLine(ex); }
                    PushState();
                    break;

                case "openSite":   // botão de sugestões: abre no navegador padrão e fecha o mixer
                    OpenSite(root.TryGetProperty("section", out var sec) ? sec.GetString() : null);
                    _ = CloseAnimatedAsync();
                    break;

                case "openSettings":   // engrenagem: abre a janela de configurações (o mixer fecha ao perder o foco)
                    SettingsForm.ShowWindow();
                    break;

                case "openSupport":   // coração: abre as configurações direto em "Apoiar o Volum"
                    SettingsForm.ShowWindow("support");
                    break;

                case "waterAdd":   // botões + / − do contador de água
                    Water.Add(root.GetProperty("ml").GetInt32());
                    break;

                case "newsSeen":   // fechou o popup de novidades: não mostra de novo nesta versão
                    s_testNewsSeen = Updater.Version;
                    _settings.LastSeenVersion = Updater.Version;
                    _settings.Save();
                    break;

                case "lockKeyboard":   // botão de teclado no cabeçalho: fecha o mixer e trava o teclado para limpar
                    _ = CloseAnimatedAsync();
                    KeyboardLock.Lock();
                    break;

                case "hide":   // botão minimizar: fecha com animação (também no modo --dev)
                    _ = CloseAnimatedAsync();
                    break;

                case "tick":   // som sutil ao mudar um volume
                    TickSound.Play();
                    break;
            }
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            Water.Changed -= OnWaterChanged;
            _sync.Dispose();
            _meter.Dispose();
            _idle.Dispose();
            _watcher?.Dispose();
            _reloadDebounce?.Dispose();
            _audio?.Dispose();
            _audio = null;
        }
        base.Dispose(disposing);
    }
}
