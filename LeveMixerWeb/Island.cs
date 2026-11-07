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
    readonly Timer _petTimer = new() { Interval = 30_000 }; // mascote: o PC está parado? (uma pergunta ao Windows a cada 30 s)
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
        _petTimer.Tick += (_, _) => CheckPet();
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
        if (m.Msg == 0x00FF /* WM_INPUT */) OnRawKey(m.LParam);   // Caps Lock / Num Lock (indicadores)
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
        _petTimer.Start();
        if (AppSettings.Current.IslandOsd) StartOsd();
    }

    // ---------- tamanho e posição: centro do topo da tela principal, grudada na borda ----------

    void Place(Mode m)
    {
        float k = DeviceDpi / 96f;
        bool greet = m == Mode.Notice && _noticeKind == "greet";   // "bom dia": do mesmo tamanho da ilha com o mascote
        bool tall = m == Mode.Notice && _noticeKind == "walk";     // levantar: largura do aviso, altura da ilha ociosa (cabe a cena)
        bool osd = m == Mode.Notice && _noticeKind == "osd";       // indicador rápido (volume, Caps Lock...): estreito
        int w = (int)Math.Round(((greet || osd ? ClosedW : m switch { Mode.Open => OpenW, Mode.Notice => NoticeW, Mode.Tucked => TuckW, _ => ClosedW }) + 2 * Ear) * k);
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

    void ShowNotice(string icon, string title, string text, string? actionLabel, Action? action, int ms, double level = -1, string? kind = null)
    {
        _notice = true;
        kind ??= icon;
        bool resize = _mode == Mode.Notice && _noticeKind != kind;   // trocou de um aviso para outro de outro tamanho
        _noticeKind = kind;
        if (resize) Place(Mode.Notice);
        _noticeAction = action;
        Post(new { type = "notice", icon, kind, title, text, action = actionLabel, level });
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

    // ---------- indicadores: Caps Lock, Num Lock, volume e microfone (como no iPhone) ----------
    // Teclado: "raw input" do Windows (a ilha recebe as teclas de qualquer app sem gancho no teclado),
    // só para ver Caps Lock e Num Lock. Volume e microfone: o Windows avisa quando mudam (sem ficar checando).
    // Liga/desliga em Configurações → Ilha (AppSettings.IslandOsd). Com o mixer aberto, não mostra (ele já mostra).

    const int OsdMs = 1600;
    NAudio.CoreAudioApi.MMDeviceEnumerator? _osdEnum;
    NAudio.CoreAudioApi.MMDevice? _osdOut, _osdMic;
    bool _osdOn, _capsDown, _numDown, _capsOn, _numOn, _lastMute, _lastMicMute;
    float _lastVol = -1;

    /// <summary>Ligou/desligou os indicadores nas configurações.</summary>
    public static void ApplyOsd()
    {
        if (!IsOn) return;
        if (AppSettings.Current.IslandOsd) s_island!.StartOsd(); else s_island!.StopOsd();
    }

    void StartOsd()
    {
        if (_osdOn || !IsHandleCreated) return;
        _osdOn = true;
        _capsOn = Control.IsKeyLocked(Keys.CapsLock);
        _numOn = Control.IsKeyLocked(Keys.NumLock);
        RegisterKeyboard(true);
        HookAudio();
    }

    void StopOsd()
    {
        if (!_osdOn) return;
        _osdOn = false;
        RegisterKeyboard(false);
        UnhookAudio();
    }

    void RegisterKeyboard(bool on)
    {
        try
        {
            var dev = new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 6, dwFlags = on ? 0x100u /* INPUTSINK */ : 0x1u /* REMOVE */, hwndTarget = on ? Handle : IntPtr.Zero };
            RegisterRawInputDevices(new[] { dev }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        }
        catch { }
    }

    // Uma tecla foi apertada em qualquer app: só interessam Caps Lock (0x14) e Num Lock (0x90)
    void OnRawKey(IntPtr hRaw)
    {
        if (!_osdOn) return;
        try
        {
            uint size = 0, header = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
            GetRawInputData(hRaw, 0x10000003 /* RID_INPUT */, IntPtr.Zero, ref size, header);
            if (size == 0 || size > 256) return;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(hRaw, 0x10000003, buf, ref size, header) != size) return;
                if (Marshal.ReadInt32(buf) != 1 /* RIM_TYPEKEYBOARD */) return;
                int off = (int)header;
                ushort flags = (ushort)Marshal.ReadInt16(buf, off + 2);
                ushort vkey = (ushort)Marshal.ReadInt16(buf, off + 6);
                bool up = (flags & 1) != 0;   // RI_KEY_BREAK
                if (vkey == 0x14) OnLockKey(ref _capsDown, up, caps: true);
                else if (vkey == 0x90) OnLockKey(ref _numDown, up, caps: false);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { }
    }

    void OnLockKey(ref bool down, bool up, bool caps)
    {
        if (up) { down = false; return; }
        if (down) return;   // tecla segurada (repetição): ignora
        down = true;
        // Espera o Windows trocar o estado e lê; se ainda não trocou, conta como trocado (apertou, trocou)
        var t = new Timer { Interval = 80 };
        t.Tick += (_, _) =>
        {
            t.Dispose();
            if (IsDisposed) return;
            bool now = Control.IsKeyLocked(caps ? Keys.CapsLock : Keys.NumLock);
            ref bool state = ref (caps ? ref _capsOn : ref _numOn);
            state = now != state ? now : !state;
            var lang = AppSettings.Current.Language;
            string key = caps ? (state ? "osdCapsOn" : "osdCapsOff") : (state ? "osdNumOn" : "osdNumOff");
            ShowOsd(caps ? "caps" : "num", Strings.Get(lang, key), "", -1);
        };
        t.Start();
    }

    void HookAudio()
    {
        UnhookAudio();
        try
        {
            _osdEnum ??= new NAudio.CoreAudioApi.MMDeviceEnumerator();
            _osdOut = _osdEnum.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            _lastVol = _osdOut.AudioEndpointVolume.MasterVolumeLevelScalar;
            _lastMute = _osdOut.AudioEndpointVolume.Mute;
            _osdOut.AudioEndpointVolume.OnVolumeNotification += OnOutVolume;
        }
        catch { _osdOut = null; }
        try
        {
            _osdMic = _osdEnum!.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Console);
            _lastMicMute = _osdMic.AudioEndpointVolume.Mute;
            _osdMic.AudioEndpointVolume.OnVolumeNotification += OnMicVolume;
        }
        catch { _osdMic = null; }
    }

    void UnhookAudio()
    {
        if (_osdOut != null) { try { _osdOut.AudioEndpointVolume.OnVolumeNotification -= OnOutVolume; } catch { } _osdOut.Dispose(); _osdOut = null; }
        if (_osdMic != null) { try { _osdMic.AudioEndpointVolume.OnVolumeNotification -= OnMicVolume; } catch { } _osdMic.Dispose(); _osdMic = null; }
    }

    // A cada 30 s: se a saída ou o microfone padrão mudou, passa a ouvir o novo
    void RefreshAudioHook()
    {
        try
        {
            using var o = _osdEnum?.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            string? micId = null;
            try { using var mi = _osdEnum?.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Console); micId = mi?.ID; } catch { }
            if (o?.ID != _osdOut?.ID || micId != _osdMic?.ID) HookAudio();
        }
        catch { }
    }

    // Avisos do Windows chegam em outra thread: passa para a da janela
    void OnOutVolume(NAudio.CoreAudioApi.AudioVolumeNotificationData d)
    {
        float v = d.MasterVolume; bool muted = d.Muted;
        try { BeginInvoke(new Action(() => ShowVolume(v, muted))); } catch { }
    }

    void OnMicVolume(NAudio.CoreAudioApi.AudioVolumeNotificationData d)
    {
        bool muted = d.Muted;
        try { BeginInvoke(new Action(() => ShowMic(muted))); } catch { }
    }

    void ShowVolume(float v, bool muted)
    {
        if (Math.Abs(v - _lastVol) < 0.004f && muted == _lastMute) return;   // nada mudou de verdade
        _lastVol = v; _lastMute = muted;
        var lang = AppSettings.Current.Language;
        bool silent = muted || v < 0.005f;
        ShowOsd(silent ? "volmute" : "vol", Strings.Get(lang, muted ? "osdMuted" : "osdVolume"),
            muted ? "" : $"{Math.Round(v * 100)}%", muted ? 0 : v);
    }

    void ShowMic(bool muted)
    {
        if (muted == _lastMicMute) return;   // só interessa quando muta ou desmuta
        _lastMicMute = muted;
        var lang = AppSettings.Current.Language;
        ShowOsd(muted ? "micoff" : "mic", Strings.Get(lang, muted ? "osdMicOff" : "osdMicOn"), "", -1);
    }

    void ShowOsd(string icon, string title, string text, double level)
    {
        if (!_osdOn || !_ready || _hiddenByFullscreen) return;
        if (_notice && _noticeKind != "osd") return;   // não atropela um aviso de verdade (água, Pomodoro...)
        foreach (Form f in Application.OpenForms)
            if (f is MixerForm mf && mf.IsOpen) return;   // com o mixer aberto, ele mesmo já mostra
        ShowNotice(icon, title, text, null, null, OsdMs, level, kind: "osd");
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTHEADER { public uint dwType, dwSize; public IntPtr hDevice, wParam; }

    [DllImport("user32.dll")] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    // ---------- mascote: aparece com o PC parado e numa passadinha a cada 30 min ----------

    const int PetIdleMin = 10, PetEveryMin = 30, PetPeekMs = 15_000;
    bool _petIdle;
    DateTime _lastPeek = DateTime.UtcNow;

    void CheckPet()
    {
        if (_osdOn) RefreshAudioHook();   // trocou a saída ou o microfone padrão? (aproveita o mesmo relógio de 30 s)
        if (!_ready || _hiddenByFullscreen) return;
        bool idle = IdleMs() >= PetIdleMin * 60_000L;
        if (idle != _petIdle)
        {
            _petIdle = idle;
            Post(new { type = "pet", on = idle });          // parado: fica até alguém mexer
            if (!idle) _lastPeek = DateTime.UtcNow;          // voltou agora: a próxima passadinha conta daqui
            return;
        }
        if (!idle && (DateTime.UtcNow - _lastPeek).TotalMinutes >= PetEveryMin)
        {
            _lastPeek = DateTime.UtcNow;
            Post(new { type = "pet", on = true, ms = PetPeekMs });   // passadinha rápida
        }
    }

    // Há quanto tempo ninguém mexe no mouse nem no teclado
    static long IdleMs()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return 0;
        return (uint)Environment.TickCount - info.dwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

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
            _petTimer.Dispose();
            StopOsd();
            _osdEnum?.Dispose();
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
