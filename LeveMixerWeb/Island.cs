using System;
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
/// Ilha (estilo Dynamic Island): pílula preta grudada no topo da tela principal, no centro.
/// - Fechada: bateria (ou a capa, com música), hora e a onda de áudio.
/// - Recolhida: vira um risquinho quando uma janela encosta no lugar dela (ex.: navegador maximizado).
/// - Aberta (mouse em cima): data e hora, player (capa, título, artista - álbum, tempo, ⏮ ⏯ ⏭) e a água.
/// Os avisos do Volum (ex.: lembrete de água) abrem a ilha por alguns segundos.
/// A janela é transparente (frame do DWM em toda a janela + WebView2 transparente): a pílula é desenhada
/// pelo CSS (island.html), com bordas suaves e animação de crescer/encolher.
/// Opcional (configurações → Ilha). Some com um app em tela cheia. Não rouba o foco (WS_EX_NOACTIVATE).
/// </summary>
sealed class IslandForm : Form
{
    // Tamanhos em "pixels CSS". A janela tem a pílula + as "orelhas" (curvas que ligam a pílula à borda da tela).
    // Precisam bater com o island.css (--closed-w, --tuck-w, --open-w, --ear...).
    const int Ear = 10;
    const int ClosedW = 250, ClosedH = 32, IdleH = 54, TuckW = 130, TuckH = 6, OpenW = 440, NoticeW = 400, NoticeH = 36;
    const int AnimMs = 500;            // acompanha a transição do CSS (--anim 480 ms, encolher) + uma folga
    const int NoticeMs = 12_000;       // quanto tempo um aviso fica aberto, se ninguém disser outro

    enum Mode { Tucked, Closed, Idle, Notice, Open }   // Notice = faixa fina com um aviso do Volum; Idle = ociosa, com o mascote maior

    static IslandForm? s_island;
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>O que fazer no alto-falante da ilha (o TrayController abre o mixer).</summary>
    public static Action? OpenMixer { get; set; }

    readonly WebView2 _web = new();
    readonly Timer _collapse = new() { Interval = 400 };   // espera o mouse sair de vez antes de fechar
    readonly Timer _noticeEnd = new() { Interval = NoticeMs };
    readonly Timer _watch = new() { Interval = 1500 };      // janela maximizada encostando? (2 chamadas ao Windows, nada pesado)
    readonly Timer _soundTimer = new() { Interval = 400 };  // tem som saindo? (lê o medidor do Windows; só com a ilha ligada)
    MediaService? _media;
    Mode _mode = Mode.Closed;
    bool _ready, _hover, _notice, _tuck, _idle, _hiddenByFullscreen;
    string _noticeKind = "";   // "greet" (bom dia) e "walk" (levantar, com a cena animada) usam a ilha mais alta
    int _openH = 160, _watchTicks;
    IntPtr _winEventHook;
    WinEventProc? _winEventProc;   // guardado: o Windows chama este delegate enquanto o gancho existir
    Action? _noticeAction;

    /// <summary>Liga ou desliga a ilha (configurações → Ilha).</summary>
    public static void Apply(bool enabled)
    {
        if (enabled && (s_island == null || s_island.IsDisposed))
        {
            s_island = new IslandForm();
            s_island.Show();
        }
        else if (!enabled && s_island != null)
        {
            s_island.Close();
            s_island = null;
            Pomodoro.Stop();   // Pomodoro e o aviso de levantar só existem com a ilha
        }
        StandUp.Apply();
    }

    /// <summary>Mudou algo nas configurações que a ilha mostra (ex.: Pomodoro ligado, tempos).</summary>
    public static void SettingsChanged()
    {
        if (!IsOn) return;
        s_island!.PostSettings();
        s_island.PostPomodoro();
    }

    public static bool IsOn => s_island != null && !s_island.IsDisposed;

    /// <summary>Mostra um aviso do Volum na ilha (abre sozinha por alguns segundos).
    /// <paramref name="action"/> roda se a pessoa clicar no botão principal. Falso se a ilha não pode mostrar.</summary>
    public static bool Notify(string icon, string title, string text, string? actionLabel, Action? action, int ms = NoticeMs)
    {
        if (!IsOn || s_island!._hiddenByFullscreen || !s_island._ready) return false;
        s_island.ShowNotice(icon, title, text, actionLabel, action, ms);
        return true;
    }

    IslandForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Text = "Volum";
        BackColor = Color.Black;                          // preto = transparente para o DWM (frame em toda a janela)
        _web.DefaultBackgroundColor = Color.Transparent;  // só a pílula do CSS aparece
        _web.Dock = DockStyle.Fill;
        Controls.Add(_web);
        Place(Mode.Closed);

        _collapse.Tick += (_, _) => { _collapse.Stop(); if (!_hover && !_notice) SetMode(Resting); };
        _noticeEnd.Tick += (_, _) => EndNotice();
        _watch.Tick += (_, _) => OnWatch();
        _soundTimer.Tick += (_, _) => CheckSound();
        Water.Changed += OnWaterChanged;
        Pomodoro.Changed += PostPomodoro;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerChanged;
        _ = InitAsync();
    }

    Mode Resting => _tuck ? Mode.Tucked : _idle ? Mode.Idle : Mode.Closed;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80 | 0x08000000;   // WS_EX_TOOLWINDOW (fora do Alt+Tab) | WS_EX_NOACTIVATE
            return cp;
        }
    }

    // Clicar na ilha não tira o foco do app que está na frente
    protected override void WndProc(ref Message m)
    {
        const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
        if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };   // janela inteira "de vidro" = transparente
            DwmExtendFrameIntoClientArea(Handle, ref margins);
            int none = 1, square = 1;
            if (Environment.OSVersion.Version.Build >= 22621)
                DwmSetWindowAttribute(Handle, 38, ref none, sizeof(int));   // DWMWA_SYSTEMBACKDROP_TYPE: nenhum (sem acrílico)
            DwmSetWindowAttribute(Handle, 33, ref square, sizeof(int));     // sem o canto arredondado do Windows 11 (o CSS desenha)
        }
        catch { }
        // App da frente mudou: confere tela cheia e se alguém encosta na ilha (o Windows avisa, sem ficar checando)
        _winEventProc = (_, _, _, _, _, _, _) => BeginInvoke(new Action(CheckScreen));
        _winEventHook = SetWinEventHook(3 /* EVENT_SYSTEM_FOREGROUND */, 3, IntPtr.Zero, _winEventProc, 0, 0, 0);
        _watch.Start();
        _soundTimer.Start();
    }

    // ---------- tamanho e posição: centro do topo da tela principal, grudada na borda ----------

    void Place(Mode m)
    {
        float k = DeviceDpi / 96f;
        bool greet = m == Mode.Notice && _noticeKind == "greet";   // "bom dia": do mesmo tamanho da ilha com o mascote
        bool tall = m == Mode.Notice && _noticeKind == "walk";     // levantar: largura do aviso, altura da ilha ociosa (cabe a cena)
        int w = (int)Math.Round(((greet ? ClosedW : m switch { Mode.Open => OpenW, Mode.Notice => NoticeW, Mode.Tucked => TuckW, _ => ClosedW }) + 2 * Ear) * k);
        int h = (int)Math.Round((greet || tall ? IdleH : m switch { Mode.Open => _openH, Mode.Notice => NoticeH, Mode.Tucked => TuckH, Mode.Idle => IdleH, _ => ClosedH }) * k);
        var screen = Screen.PrimaryScreen!.Bounds;
        SetBounds(screen.Left + (screen.Width - w) / 2, screen.Top, w, h);
    }

    static int Rank(Mode m) => m switch { Mode.Open => 5, Mode.Notice => 4, Mode.Idle => 3, Mode.Closed => 2, _ => 1 };

    // Crescendo: a janela cresce antes e o CSS anima a pílula dentro dela.
    // Encolhendo: o CSS anima primeiro e a janela encolhe no fim (senão a animação seria cortada).
    void SetMode(Mode m)
    {
        if (m == _mode) return;
        var old = _mode;
        _mode = m;
        if (Rank(m) > Rank(old))
        {
            Place(m);
            PostMode();
        }
        else
        {
            PostMode();
            var t = new Timer { Interval = AnimMs };
            t.Tick += (_, _) => { t.Dispose(); if (_mode == m && !IsDisposed) Place(m); };
            t.Start();
        }
    }

    void PostMode() => Post(new { type = "mode", mode = _mode.ToString().ToLowerInvariant() });

    // ---------- tela cheia e janelas encostando ----------

    // ---------- tem som saindo? (a onda da ilha só aparece com som de verdade, de qualquer app) ----------

    NAudio.CoreAudioApi.MMDeviceEnumerator? _enum;
    NAudio.CoreAudioApi.MMDevice? _out;
    bool _soundOn;
    DateTime _lastSound;
    int _soundTicks;

    void CheckSound()
    {
        try
        {
            if (++_soundTicks % 75 == 0) { _out?.Dispose(); _out = null; }   // a cada ~30 s pega a saída padrão de novo (pode ter mudado)
            _enum ??= new NAudio.CoreAudioApi.MMDeviceEnumerator();
            _out ??= _enum.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            if (_out.AudioMeterInformation.MasterPeakValue > 0.003f) _lastSound = DateTime.UtcNow;
        }
        catch { _out?.Dispose(); _out = null; }   // sem saída de áudio (ou trocou agora): tenta de novo no próximo
        bool on = (DateTime.UtcNow - _lastSound).TotalMilliseconds < 1500;   // segura 1,5 s (pausas curtas entre músicas)
        if (on == _soundOn) return;
        _soundOn = on;
        Post(new { type = "sound", on });
    }

    void OnWatch()
    {
        CheckScreen();
        if (++_watchTicks % 40 == 0) PostBattery();   // bateria a cada ~1 min (e quando o Windows avisa)
    }

    void CheckScreen()
    {
        if (IsDisposed) return;
        bool full = Fullscreen.ForegroundIsFullscreen(primaryOnly: true);
        if (full != _hiddenByFullscreen)
        {
            _hiddenByFullscreen = full;
            if (full) Hide();
            else ShowWindow(Handle, 4 /* SW_SHOWNOACTIVATE */);
        }
        var fg = full ? IntPtr.Zero : ForegroundTouchingIsland();
        if (fg == IntPtr.Zero) { SetTuck(false); return; }   // ninguém encosta: ilha fechada normal
        CheckObstruction(fg);
    }

    void SetTuck(bool tuck)
    {
        if (tuck == _tuck) return;
        _tuck = tuck;
        if (_mode is Mode.Closed or Mode.Tucked or Mode.Idle) SetMode(Resting);
    }

    // Lugar da ilha fechada na tela (pixels de verdade)
    Rectangle ClosedRect()
    {
        float k = DeviceDpi / 96f;
        var screen = Screen.PrimaryScreen!.Bounds;
        int w = (int)((ClosedW + 2 * Ear) * k), h = (int)(ClosedH * k);
        return new Rectangle(screen.Left + (screen.Width - w) / 2, screen.Top, w, h);
    }

    // A janela da frente ocupa o lugar da ilha fechada? Ex.: navegador maximizado. Devolve a janela (ou zero).
    IntPtr ForegroundTouchingIsland()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == Handle) return IntPtr.Zero;
        GetWindowThreadProcessId(fg, out uint pid);
        if (pid == Environment.ProcessId) return IntPtr.Zero;
        var cls = new System.Text.StringBuilder(64);
        GetClassName(fg, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return IntPtr.Zero;
        if (!GetWindowRect(fg, out var r)) return IntPtr.Zero;
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom).IntersectsWith(ClosedRect()) ? fg : IntPtr.Zero;
    }

    // Encostar não basta: só recolhe se a ilha estiver em cima de abas, menus ou botões do app (Obstruction.cs).
    // A conferência roda em segundo plano e só quando algo muda na janela (outra janela, título ou tamanho
    // diferente: abriu/trocou de aba, mexeu em menus) ou a cada 10 s, para não pesar.
    bool _obstructionBusy;
    string _lastKey = "";
    DateTime _lastCheck;

    void CheckObstruction(IntPtr fg)
    {
        GetWindowRect(fg, out var r);
        var title = new System.Text.StringBuilder(256);
        GetWindowText(fg, title, title.Capacity);
        string key = $"{fg}|{title}|{r.Left},{r.Top},{r.Right},{r.Bottom}";
        if (_obstructionBusy || (key == _lastKey && (DateTime.UtcNow - _lastCheck).TotalSeconds < 10)) return;
        _obstructionBusy = true;
        _lastKey = key;
        _lastCheck = DateTime.UtcNow;

        float k = DeviceDpi / 96f;
        var area = ClosedRect();
        area.Inflate((int)(24 * k), 0);                                // "chegar perto" também conta
        int bandBottom = area.Bottom + (int)(6 * k);
        System.Threading.Tasks.Task.Run(() => Obstruction.Check(fg, area, bandBottom)).ContinueWith(t =>
        {
            _obstructionBusy = false;
            if (IsDisposed || t.IsFaulted) return;
            bool tuck = t.Result;
            try { BeginInvoke(new Action(() => SetTuck(tuck))); } catch { }
        });
    }

    // ---------- bateria ----------

    void OnPowerChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (!IsDisposed) BeginInvoke(new Action(PostBattery));
    }

    void PostBattery()
    {
        var p = SystemInformation.PowerStatus;
        bool present = p.BatteryChargeStatus is not (BatteryChargeStatus.NoSystemBattery or BatteryChargeStatus.Unknown)
                       && p.BatteryLifePercent <= 1;
        Post(new
        {
            type = "battery",
            present,
            percent = (int)Math.Round(p.BatteryLifePercent * 100),
            charging = p.PowerLineStatus == PowerLineStatus.Online
        });
    }

    // ---------- avisos ----------

    void ShowNotice(string icon, string title, string text, string? actionLabel, Action? action, int ms)
    {
        _notice = true;
        bool resize = _mode == Mode.Notice && _noticeKind != icon;   // trocou de um aviso para outro de outro tamanho
        _noticeKind = icon;
        if (resize) Place(Mode.Notice);
        _noticeAction = action;
        Post(new { type = "notice", icon, title, text, action = actionLabel });
        SetMode(Mode.Notice);   // faixa fina, sem abrir a ilha inteira
        _noticeEnd.Stop();
        _noticeEnd.Interval = ms;
        _noticeEnd.Start();
    }

    void EndNotice()
    {
        _noticeEnd.Stop();
        if (!_notice) return;
        _notice = false;
        _noticeKind = "";
        _noticeAction = null;
        Post(new { type = "notice", icon = (string?)null });
        SetMode(_hover ? Mode.Open : Resting);
    }

    // ---------- WebView2 ----------

    async Task InitAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, MixerForm.WebViewDataFolder);
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = Program.DevMode;
            core.Settings.AreDevToolsEnabled = Program.DevMode;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith("https://app.volum/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            core.WebMessageReceived += OnWebMessage;
            var sourceRoot = Program.DevMode ? MixerForm.FindSourceWwwroot() : null;
            core.SetVirtualHostNameToFolderMapping("app.volum",
                sourceRoot ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Allow);
            core.Navigate("https://app.volum/island.html");

            _media = await MediaService.CreateAsync();
            _media.Changed += (info) => { if (!IsDisposed) BeginInvoke(new Action(() => Post(new { type = "media", media = info }))); };
        }
        catch (Exception ex) { Debug.WriteLine(ex); Close(); }
    }

    void Post(object message)
    {
        if (!_ready) return;
        try { _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json)); }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    // ---------- mascote: oi quando o PC liga ----------

    static bool s_greeted;   // uma vez por vez que o Volum abre

    // Abriu junto com o Windows (o PC ligou há pouco): o mascote dá bom dia / boa tarde / boa noite.
    // Abrir o Volum à mão, ou ele reiniciar para atualizar, não conta.
    void Greet()
    {
        bool justBooted = Environment.TickCount64 < 10 * 60_000;   // PC ligado há menos de 10 min
        if (s_greeted || !(justBooted || Program.TestGreeting)) return;
        s_greeted = true;
        int h = DateTime.Now.Hour;
        string key = h is >= 5 and < 12 ? "hiMorning" : h is >= 12 and < 18 ? "hiAfternoon" : "hiEvening";
        var lang = AppSettings.Current.Language;
        // Espera a ilha aparecer e o Windows assentar (no início, tudo está abrindo ao mesmo tempo)
        var t = new Timer { Interval = Program.TestGreeting ? 800 : 2500 };
        t.Tick += (_, _) =>
        {
            t.Dispose();
            if (IsDisposed || _hiddenByFullscreen) return;
            ShowNotice("greet", Strings.Get(lang, key), "", null, null, 4000);   // só a frase; depois a ilha vira o mascote
        };
        t.Start();
    }

    void PostSettings()
    {
        var s = AppSettings.Current;
        Post(new { type = "settings", language = Strings.Normalize(s.Language), lite = s.LightMode, pomodoro = s.PomodoroEnabled });
    }

    // Estado do Pomodoro: a ilha conta o tempo sozinha a partir de "leftMs" (nada vem do C# a cada segundo)
    void PostPomodoro()
    {
        if (IsDisposed) return;
        var s = AppSettings.Current;
        Post(new
        {
            type = "pomodoro",
            phase = Pomodoro.Current.ToString().ToLowerInvariant(),
            paused = Pomodoro.Paused,
            leftMs = (long)Pomodoro.Left.TotalMilliseconds,
            totalMs = Pomodoro.TotalSec * 1000L,
            done = Pomodoro.Done,
            every = s.LongBreakEvery,
            focusMin = s.FocusMin
        });
    }

    void OnWaterChanged()
    {
        var w = Water.Data;
        Post(new { type = "water", water = new { enabled = w.Enabled, goalMl = w.GoalMl, cupMl = w.CupMl, todayMl = w.TodayMl } });
    }

    void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "ready":
                    _ready = true;
                    PostSettings();
                    PostPomodoro();
                    PostMode();
                    PostBattery();
                    OnWaterChanged();
                    _ = _media?.RefreshAsync();
                    CheckScreen();
                    Greet();
                    break;

                case "height":   // altura da ilha aberta (muda com a página e com a água)
                    _openH = Math.Clamp((int)Math.Ceiling(root.GetProperty("h").GetDouble()), 80, 400);
                    if (_mode == Mode.Open) Place(Mode.Open);
                    break;

                case "hover":    // mouse entrou/saiu da ilha
                    _hover = root.GetProperty("on").GetBoolean();
                    if (_hover && !_notice) { _collapse.Stop(); SetMode(Mode.Open); }   // com aviso na tela, fica a faixa (os botões não saem do lugar)
                    else if (!_notice) { _collapse.Stop(); _collapse.Start(); }
                    break;

                case "idle":     // a ilha ficou um tempo sem uso: cresce um pouco para baixo e o mascote aparece maior
                    _idle = root.GetProperty("on").GetBoolean();
                    if (_mode is Mode.Closed or Mode.Idle) SetMode(Resting);
                    break;

                case "playPause": _media?.PlayPause(); break;
                case "next": _media?.Next(); break;
                case "prev": _media?.Previous(); break;
                case "openMixer": OpenMixer?.Invoke(); break;

                case "waterAdd":
                    Water.Add(root.GetProperty("ml").GetInt32());
                    TickSound.Play();
                    break;

                case "noticeAction":   // botão principal do aviso (ex.: "Bebi um copo")
                    var act = _noticeAction;
                    EndNotice();
                    act?.Invoke();
                    break;

                case "noticeClose":
                    EndNotice();
                    break;

                // Pomodoro (página Foco da ilha)
                case "pomoStart": Pomodoro.Start(); break;
                case "pomoPause": Pomodoro.Pause(); break;
                case "pomoResume": Pomodoro.Resume(); break;
                case "pomoSkip": Pomodoro.Skip(); break;
                case "pomoStop": Pomodoro.Stop(); break;
            }
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_winEventHook != IntPtr.Zero) UnhookWinEvent(_winEventHook);
            Water.Changed -= OnWaterChanged;
            Pomodoro.Changed -= PostPomodoro;
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerChanged;
            _media?.Dispose();
            _collapse.Dispose();
            _noticeEnd.Dispose();
            _watch.Dispose();
            _soundTimer.Dispose();
            _out?.Dispose();
            _enum?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---------- Windows ----------

    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int max);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
