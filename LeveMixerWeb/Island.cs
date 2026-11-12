using System;
using System.Diagnostics;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
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
    // Visual do Figma (10/11/2026): maior que antes, para os elementos respirarem. Precisa bater com o island.css.
    const int ClosedW = 270, ClosedH = 50, IdleH = 58, TuckW = 130, TuckH = 6, OpenW = 400, NoticeW = 400, NoticeH = 50;
    const int ActW = 330;              // largura da faixa de uma atividade (carregador, fone, microfone/câmera em uso)
    const int BubbleW = 50, BubbleGap = 8;   // bolha do Pomodoro ao lado da ilha fechada (o JS avisa quando ela aparece)
    const int FloatGap = 8;            // ilha flutuante: distância da borda de cima da tela
    const int AnimMs = 900;            // garantia: se o "animDone" do JS não vier, a janela fica do tamanho exato depois disso
    const int MoveSlack = 10;          // folga embaixo da janela enquanto a forma anima (a mola passa um pouquinho)
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
    readonly Timer _dwell = new() { Interval = 100 };       // recolhida (escondida): o mouse parou no topo? (só roda escondida)
    readonly Timer _autoHide = new();                       // opção "esconder quando não estiver em uso": tempo parada até sumir
    bool _ready, _hover, _notice, _tuck, _idle, _hiddenByFullscreen, _hiddenByTuck, _hiddenByAuto, _dwellOpened;
    Point _dwellPt;
    int _dwellMs;
    string _noticeKind = "";   // "greet" (bom dia) e "walk" (levantar, com a cena animada) usam a ilha mais alta
    int _openH = 160, _watchTicks;
    bool _bubble;   // bolha do Pomodoro na tela: a janela alarga para a direita até ela
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
        Agenda.Apply(enabled);   // a agenda só avisa com a ilha ligada
        Weather.Apply(enabled);  // o clima só é baixado com a ilha ligada
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
        // O WebView NÃO acompanha o tamanho da janela: fica sempre do mesmo tamanho e parado no mesmo lugar da tela
        // (centralizado em cima), e a janela só recorta o pedaço que aparece (PlaceSize). Antes, ao fechar, a janela
        // encolhia e o WebView2 redesenhava com atraso: por um quadro o conteúdo aparecia deslocado (a hora "piscava").
        _web.Dock = DockStyle.None;
        Controls.Add(_web);
        Place(Mode.Closed);

        _collapse.Tick += (_, _) => { _collapse.Stop(); if (!_hover && !_notice) SetMode(Resting); };
        _noticeEnd.Tick += (_, _) => EndNotice();
        _watch.Tick += (_, _) => OnWatch();
        _soundTimer.Tick += (_, _) => CheckSound();
        _petTimer.Tick += (_, _) => CheckPet();
        _dwell.Tick += (_, _) => CheckDwell();
        _autoHide.Tick += (_, _) => { _autoHide.Stop(); if (CanAutoHide) { _hiddenByAuto = true; UpdateShown(); } };
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
        StartActivities();   // carregador, saída de áudio, microfone e câmera em uso
        if (AppSettings.Current.IslandOsd) StartOsd();
    }

    // ---------- tamanho e posição: centro do topo da tela principal, grudada na borda ----------

    // Tamanho da ilha num estado, em "pixels CSS" (sem as orelhas)
    Size SizeOf(Mode m)
    {
        bool greet = m == Mode.Notice && _noticeKind == "greet";   // "bom dia": do mesmo tamanho da ilha com o mascote
        bool tall = m == Mode.Notice && _noticeKind == "walk";     // levantar: largura do aviso, altura da ilha ociosa (cabe a cena)
        bool osd = m == Mode.Notice && _noticeKind == "osd";       // indicador rápido (volume, Caps Lock...): estreito
        bool act = m == Mode.Notice && _noticeKind == "act";       // atividade (carregador, fone, câmera...): um pouco mais larga
        int w = act ? ActW : greet || osd ? ClosedW : m switch { Mode.Open => OpenW, Mode.Notice => NoticeW, Mode.Tucked => TuckW, _ => ClosedW };
        int h = greet || tall ? IdleH : m switch { Mode.Open => _openH, Mode.Notice => NoticeH, Mode.Tucked => TuckH, Mode.Idle => IdleH, _ => ClosedH };
        return new Size(w, h);
    }

    // Janela no tamanho exato do estado (quando a forma já parou)
    // Recolhida (em cima de abas, menus, botões) a ilha some de vez quando a forma termina de encolher
    void Place(Mode m)
    {
        _shown = SizeOf(m);
        PlaceSize(_shown, 0);
        if (m == Mode.Tucked && !_hiddenByTuck) { _hiddenByTuck = true; UpdateShown(); }
    }

    // A forma vai mudar de tamanho (o que está na tela → o novo estado): janela com folga até ela parar
    void MoveTo(Mode m) { var to = SizeOf(m); PlaceMoving(_shown, to); _shown = to; }
    Size _shown = new(ClosedW, ClosedH);
    float _webScale = 1f;   // quanto o WebView desenha a mais que a escala do monitor (ex.: "Tamanho do texto" do Windows em 125%)   // tamanho que a forma tem (ou para onde está indo)

    // Durante a animação: a janela fica com o maior dos dois tamanhos + uma folga embaixo, para a mola
    // (que passa um pouquinho do tamanho) não ser cortada. Quando a forma para, o JS manda "animDone".
    void PlaceMoving(Size from, Size to) =>
        PlaceSize(new Size(Math.Max(from.Width, to.Width), Math.Max(from.Height, to.Height)), MoveSlack);

    void PlaceSize(Size s, int slack)
    {
        float k = DeviceDpi / 96f * _webScale;
        int w = (int)Math.Round((s.Width + 2 * Ear) * k);
        int h = (int)Math.Round((s.Height + slack) * k);
        var box = IslandBox(w, h, k);
        // Bolha do Pomodoro: fica à direita da ilha fechada; a janela cresce só para a direita (a ilha não sai do lugar)
        if (_bubble)
        {
            float right = ClosedW / 2f + BubbleGap + BubbleW, half = s.Width / 2f + Ear;
            if (right > half) box.Width += (int)Math.Ceiling((right - half) * k);
            box.Height = Math.Max(box.Height, (int)Math.Round((ClosedH + slack) * k));
        }
        SetBounds(box.X, box.Y, box.Width, box.Height);
        // WebView do tamanho da maior ilha possível, sempre no mesmo lugar da tela: só a posição dele dentro da janela muda.
        // Embaixo, ele fica preso pela borda de baixo (a ilha cresce para cima).
        int webW = (int)Math.Round((OpenW + 2 * Ear) * k), webH = (int)Math.Round((400 + MoveSlack) * k);
        var web = IslandBox(webW, webH, k);
        if (AtBottom) web.Y = box.Bottom - webH;
        var wb = new Rectangle(web.X - box.X, web.Y - box.Y, webW, webH);
        if (_web.Bounds != wb) _web.Bounds = wb;
    }

    static bool AtBottom => AppSettings.Current.IslandPosV == "bottom";
    static bool Floating => AppSettings.Current.IslandStyle == "float" || AtBottom;   // embaixo não tem como "grudar" na borda: sempre flutuante
    static int TopGap(float k) => Floating ? (int)Math.Round(FloatGap * k) : 0;

    // Onde a ilha fica na tela (pixels de verdade) para um tamanho w×h: centro da tela ou perto do canto escolhido,
    // em cima (na borda) ou embaixo (acima da barra de tarefas). Nos cantos, o centro fica onde a ilha ABERTA encosta
    // na margem, para ela crescer e encolher sempre a partir do mesmo ponto (sem andar de lado).
    Rectangle IslandBox(int w, int h, float k)
    {
        var s = AppSettings.Current;
        var scr = IslandScreen;
        var area = AtBottom ? scr.WorkingArea : scr.Bounds;
        int half = (int)Math.Round((OpenW / 2f + Ear) * k), margin = (int)Math.Round(12 * k);
        int cx = s.IslandPosH switch
        {
            "left" => area.Left + margin + half,
            "right" => area.Right - margin - half,
            _ => area.Left + area.Width / 2,
        };
        int y = AtBottom ? area.Bottom - (int)Math.Round(FloatGap * k) - h : area.Top + TopGap(k);
        return new Rectangle(cx - w / 2, y, w, h);
    }

    /// <summary>Trocou o formato da ilha nas configurações (grudada no topo ou flutuante).</summary>
    public static void ApplyStyle()
    {
        if (!IsOn) return;
        s_island!.PostSettings();
        s_island.Place(s_island._mode);
    }

    static int Rank(Mode m) => m switch { Mode.Open => 5, Mode.Notice => 4, Mode.Idle => 3, Mode.Closed => 2, _ => 1 };

    // A janela vai antes para o maior dos dois tamanhos (+ folga), o JS anima a forma com a mola dentro dela
    // e, quando a forma para ("animDone"), a janela fica do tamanho exato. O temporizador é só por garantia.
    void SetMode(Mode m)
    {
        if (m == _mode) { ArmAutoHide(); return; }
        _mode = m;
        if (m != Mode.Tucked && _hiddenByTuck) { _hiddenByTuck = false; UpdateShown(); }   // aviso, lembrete ou saiu de cima das abas: aparece
        if (m is Mode.Open or Mode.Notice && _hiddenByAuto) { _hiddenByAuto = false; UpdateShown(); }   // escondida pela opção: aviso aparece
        ArmAutoHide();
        MoveTo(m);
        PostMode();
        var t = new Timer { Interval = AnimMs };
        int seq = _modeSeq;
        t.Tick += (_, _) => { t.Dispose(); if (seq == _modeSeq && !IsDisposed) Place(_mode); };
        t.Start();
    }

    int _modeSeq;   // cada troca de estado tem um número: um "animDone" atrasado, de antes da troca, é ignorado
    void PostMode() => Post(new { type = "mode", mode = _mode.ToString().ToLowerInvariant(), seq = ++_modeSeq });

    // ---------- tela cheia e janelas encostando ----------

    // ---------- tem som saindo? (a onda da ilha só aparece com som de verdade, de qualquer app) ----------

    NAudio.CoreAudioApi.MMDeviceEnumerator? _enum;
    NAudio.CoreAudioApi.MMDevice? _out;
    bool _soundOn;
    float _lastLevel;
    DateTime _lastSound;
    int _soundTicks;

    void CheckSound()
    {
        try
        {
            if (++_soundTicks % 75 == 0) { _out?.Dispose(); _out = null; }   // a cada ~30 s pega a saída padrão de novo (pode ter mudado)
            _enum ??= new NAudio.CoreAudioApi.MMDeviceEnumerator();
            _out ??= _enum.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            float peak = _out.AudioMeterInformation.MasterPeakValue;
            if (peak > 0.003f) _lastSound = DateTime.UtcNow;
            // Com som saindo e a ilha na tela: manda o nível ~10 vezes por segundo (a onda acompanha o som de verdade)
            if (_soundOn && Visible && !_hiddenByFullscreen && Math.Abs(peak - _lastLevel) > 0.02f)
            {
                _lastLevel = peak;
                Post(new { type = "level", v = Math.Round(peak, 2) });
            }
        }
        catch { _out?.Dispose(); _out = null; }   // sem saída de áudio (ou trocou agora): tenta de novo no próximo
        bool on = (DateTime.UtcNow - _lastSound).TotalMilliseconds < 1500;   // segura 1,5 s (pausas curtas entre músicas)
        // Ritmo: com som e a ilha na tela, 10 vezes por segundo (a onda acompanha); sem som, devagar;
        // com a ilha escondida (jogo em tela cheia, recolhida em cima de um app), só 1 vez por segundo
        int every = !Visible || _hiddenByFullscreen ? 1000 : on ? 100 : 400;
        if (_soundTimer.Interval != every) _soundTimer.Interval = every;
        if (on == _soundOn) return;
        _soundOn = on;
        Post(new { type = "sound", on });
    }

    void OnWatch()
    {
        CheckScreen();
        if (++_watchTicks % 40 == 0) PostBattery();   // bateria a cada ~1 min (e quando o Windows avisa)
    }

    // Monitor onde a ilha está: o principal; com ele em tela cheia, outro monitor (opção, ligada por padrão)
    Screen IslandScreen => _otherScreen != null && Screen.AllScreens.Any(s => s.DeviceName == _otherScreen.DeviceName)
        ? _otherScreen : Screen.PrimaryScreen!;
    Screen? _otherScreen;   // null = no principal
    IntPtr _fullWnd;        // janela em tela cheia no monitor principal que fez a ilha mudar de monitor

    void CheckScreen()
    {
        if (IsDisposed) return;
        // "Sempre no outro monitor" (opção): a ilha fica no primeiro monitor que não é o principal e some se ele estiver em tela cheia
        var second = ChosenScreen();
        if (second != null)
        {
            var fw = Fullscreen.ForegroundFullscreenWindow();
            bool fullThere = fw != IntPtr.Zero && Screen.FromHandle(fw).DeviceName == second.DeviceName;
            _fullWnd = IntPtr.Zero;
            if (second.DeviceName != _otherScreen?.DeviceName)
            {
                _otherScreen = second;
                Place(_mode);
                BeginInvoke(() => { if (!IsDisposed) Place(_mode); });
            }
            if (fullThere != _hiddenByFullscreen) { _hiddenByFullscreen = fullThere; UpdateShown(); }
            var fg2 = fullThere || AtBottom ? IntPtr.Zero : ForegroundTouchingIsland();
            if (fg2 == IntPtr.Zero) { SetTuck(false); return; }
            CheckObstruction(fg2);
            return;
        }
        var fullWnd = Fullscreen.ForegroundFullscreenWindow(primaryOnly: true);
        // Já mudou de monitor: fica lá enquanto aquela janela continuar em tela cheia no principal,
        // mesmo que a pessoa clique em algo no outro monitor (senão a ilha voltaria para cima do vídeo/jogo)
        if (fullWnd == IntPtr.Zero && _otherScreen != null && Fullscreen.StillFullscreen(_fullWnd, primaryOnly: true)) fullWnd = _fullWnd;
        _fullWnd = fullWnd;
        bool full = fullWnd != IntPtr.Zero;

        var other = full && AppSettings.Current.IslandOtherScreen ? Screen.AllScreens.FirstOrDefault(s => !s.Primary) : null;
        if (other?.DeviceName != _otherScreen?.DeviceName)
        {
            _otherScreen = other;
            Place(_mode);
            BeginInvoke(() => { if (!IsDisposed) Place(_mode); });   // de novo depois do Windows ajustar a escala (DPI) do outro monitor
        }

        bool hide = full && other == null;   // um monitor só (ou opção desligada): some, como antes
        if (hide != _hiddenByFullscreen)
        {
            _hiddenByFullscreen = hide;
            UpdateShown();
        }
        var fg = full || AtBottom ? IntPtr.Zero : ForegroundTouchingIsland();   // embaixo não fica em cima de abas
        if (fg == IntPtr.Zero) { SetTuck(false); return; }   // ninguém encosta: ilha fechada normal
        CheckObstruction(fg);
    }

    // Escondida por tela cheia ou por estar recolhida em cima das abas
    void UpdateShown()
    {
        bool hide = _hiddenByFullscreen || _hiddenByTuck || _hiddenByAuto;
        if (hide && Visible) Hide();
        else if (!hide && !Visible) ShowWindow(Handle, 4 /* SW_SHOWNOACTIVATE */);
        if ((_hiddenByTuck || _hiddenByAuto) && !_hiddenByFullscreen) { _dwellMs = 0; _dwell.Start(); }
        else if (!_dwellOpened) _dwell.Stop();
    }

    // Escondida em cima das abas: abre só com o mouse PARADO na borda de cima, no meio da tela, por 0,6 s,
    // sem botão apertado (clicar e arrastar abas nunca abre). Depois de aberta assim, fecha quando o mouse sai.
    void CheckDwell()
    {
        var p = Cursor.Position;
        if (_dwellOpened)
        {
            var b = Bounds; b.Inflate(4, 4);
            if (_mode != Mode.Open || !b.Contains(p))
            {
                _dwellOpened = false;
                if (_mode == Mode.Open && !b.Contains(p)) { _hover = false; _collapse.Stop(); _collapse.Start(); }
                if (!_hiddenByTuck) _dwell.Stop();
            }
            return;
        }
        if (!_hiddenByTuck && _hiddenByAuto && !_hiddenByFullscreen)
        {
            // Escondida pela opção: volta assim que o mouse passa no lugar dela (sem precisar ficar parado), sem botão apertado
            var zone = ClosedRect();
            var scr = IslandScreen.Bounds;
            var area = AtBottom ? Rectangle.FromLTRB(zone.Left, zone.Top - 2, zone.Right, scr.Bottom)
                                : Rectangle.FromLTRB(zone.Left, scr.Top, zone.Right, zone.Bottom + 2);
            if (!area.Contains(p) || MouseButtons != MouseButtons.None) { _dwellMs = 0; return; }
            if ((_dwellMs += _dwell.Interval) < 200) return;
            _dwellMs = 0;
            _hiddenByAuto = false;
            UpdateShown();
            ArmAutoHide();
            return;
        }
        if (!_hiddenByTuck || _hiddenByFullscreen) { _dwell.Stop(); return; }
        var r = ClosedRect();
        bool atTop = p.X >= r.Left && p.X < r.Right && p.Y <= IslandScreen.Bounds.Top + 1;
        bool still = Math.Abs(p.X - _dwellPt.X) <= 3 && Math.Abs(p.Y - _dwellPt.Y) <= 3;
        _dwellPt = p;
        if (!atTop || !still || MouseButtons != MouseButtons.None) { _dwellMs = 0; return; }
        if ((_dwellMs += _dwell.Interval) < 600) return;
        _dwellMs = 0;
        _dwellOpened = true;   // o timer continua, agora vigiando a saída do mouse
        _hover = true;
        _collapse.Stop();
        SetMode(Mode.Open);
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
        float k = DeviceDpi / 96f * _webScale;
        int w = (int)((ClosedW + 2 * Ear) * k), h = (int)(ClosedH * k);
        return IslandBox(w, h, k);
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

    // Janela encostando no lugar da ilha = a ilha recolhe (some), como em cima das abas do navegador.
    // Antes só recolhia quando a acessibilidade do Windows achava abas/menus/botões ali (Obstruction.cs), mas apps que
    // desenham a própria interface (Figma, Photoshop, Electron) não mostram nada para ela; agora vale para qualquer app
    // que chegue no topo (todos têm barra de título, menus ou abas lá em cima). Tela cheia continua sendo outra regra.
    // Opção "Sair da frente dos apps" (Configurações → Ilha): desligada, a ilha fica à vista mesmo com a janela ali.
    void CheckObstruction(IntPtr fg) => SetTuck(AppSettings.Current.IslandTuck);

    // (conferência antiga pela acessibilidade, guardada caso precise voltar)
    bool _obstructionBusy;
    string _lastKey = "";
    DateTime _lastCheck;

    void CheckObstructionUia(IntPtr fg)
    {
        GetWindowRect(fg, out var r);
        var title = new System.Text.StringBuilder(256);
        GetWindowText(fg, title, title.Capacity);
        string key = $"{fg}|{title}|{r.Left},{r.Top},{r.Right},{r.Bottom}";
        if (_obstructionBusy || (key == _lastKey && (DateTime.UtcNow - _lastCheck).TotalSeconds < 10)) return;
        _obstructionBusy = true;
        _lastKey = key;
        _lastCheck = DateTime.UtcNow;

        float k = DeviceDpi / 96f * _webScale;
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
        if (!IsDisposed) BeginInvoke(new Action(() => { PostBattery(); CheckCharger(); }));
    }

    // ---------- atividades: o que está acontecendo no PC aparece sozinho na ilha ----------
    // Carregador ligado/desligado, saída de áudio trocada (fone conectado), microfone e câmera em uso.
    // Tudo por aviso do Windows (nada fica checando). Aparecem como a faixa rápida dos indicadores.

    PowerLineStatus _lastLine = SystemInformation.PowerStatus.PowerLineStatus;

    void CheckCharger()
    {
        var p = SystemInformation.PowerStatus;
        if (p.PowerLineStatus == _lastLine || p.PowerLineStatus == PowerLineStatus.Unknown) return;
        _lastLine = p.PowerLineStatus;
        if (p.BatteryChargeStatus is BatteryChargeStatus.NoSystemBattery or BatteryChargeStatus.Unknown) return;
        bool on = p.PowerLineStatus == PowerLineStatus.Online;
        int pct = (int)Math.Round(p.BatteryLifePercent * 100);
        var lang = AppSettings.Current.Language;
        ShowActivity(on ? "charging" : "battery", Strings.Get(lang, on ? "actCharging" : "actOnBattery"), $"{pct}%", pct / 100.0);
    }

    // Faixa rápida de uma atividade: não atropela aviso de verdade (água, Pomodoro...) e some com tela cheia
    void ShowActivity(string icon, string title, string text, double level = -1)
    {
        if (!_ready || _hiddenByFullscreen) return;
        if (_notice && _noticeKind is not ("osd" or "act")) return;
        ShowNotice(icon, title, text, null, null, 2600, level, kind: "act");
    }

    // Saída de áudio padrão trocou (ex.: fone Bluetooth conectou): mostra o nome do aparelho
    sealed class OutputWatch : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient
    {
        public Action<string>? DefaultChanged;
        public void OnDefaultDeviceChanged(NAudio.CoreAudioApi.DataFlow flow, NAudio.CoreAudioApi.Role role, string id)
        {
            if (flow == NAudio.CoreAudioApi.DataFlow.Render && role == NAudio.CoreAudioApi.Role.Multimedia) DefaultChanged?.Invoke(id);
        }
        public void OnDeviceStateChanged(string id, NAudio.CoreAudioApi.DeviceState state) { }
        public void OnDeviceAdded(string id) { }
        public void OnDeviceRemoved(string id) { }
        public void OnPropertyValueChanged(string id, NAudio.CoreAudioApi.PropertyKey key) { }
    }

    NAudio.CoreAudioApi.MMDeviceEnumerator? _devEnum;
    OutputWatch? _outputWatch;
    string? _lastOutput;

    void StartActivities()
    {
        try
        {
            _devEnum = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            try { using var d = _devEnum.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia); _lastOutput = d.ID; } catch { }
            _outputWatch = new OutputWatch { DefaultChanged = id => { try { BeginInvoke(new Action(() => OnOutputChanged(id))); } catch { } } };
            _devEnum.RegisterEndpointNotificationCallback(_outputWatch);
        }
        catch { }
        _privacy = new PrivacyWatch();
        _privacy.Changed += (mic, cam) => { try { BeginInvoke(new Action(() => OnPrivacy(mic, cam))); } catch { } };
        Agenda.Changed += OnAgendaChanged;
        Agenda.Alert += OnAgendaAlert;
        Weather.Changed += OnWeatherChanged;
    }

    // ---------- clima (Weather.cs): página na ilha ----------

    void OnWeatherChanged(WeatherNow? w) { if (!IsDisposed) BeginInvoke(new Action(PostWeather)); }

    void PostWeather()
    {
        if (!_ready) return;
        var w = Weather.Current;
        var s = AppSettings.Current;
        // unit: o JS converte (a previsão vem sempre em °C); show: o que aparece na ilha fechada
        Post(new { type = "weather", on = w != null, weather = w, unit = Weather.Unit, show = s.WeatherShow is "icon" or "off" ? s.WeatherShow : "both" });
    }

    /// <summary>Mudaram o que aparece na ilha fechada ou a unidade (aba Clima).</summary>
    public static void WeatherOptionsChanged() => s_island?.PostWeather();

    /// <summary>Salvaram outra cidade nas configurações. Devolve o nome achado ("" = apagou, null = erro).</summary>
    public static async Task<string?> WeatherCityChangedAsync(string city)
    {
        var r = await Weather.SetCityAsync(city, IsOn);
        s_island?.PostWeather();
        return r;
    }

    // ---------- agenda (Agenda.cs): página na ilha + avisos 10 min antes e na hora ----------

    void OnAgendaChanged(IReadOnlyList<CalEvent> list) { if (!IsDisposed) BeginInvoke(new Action(PostAgenda)); }

    void PostAgenda()
    {
        if (!_ready) return;
        var now = DateTime.Now;
        var items = Agenda.Upcoming
            .Where(e => e.End > now && e.Start.Date <= now.Date.AddDays(1))
            .Take(4)
            .Select(e => new { title = e.Title, start = new DateTimeOffset(e.Start).ToUnixTimeMilliseconds(), end = new DateTimeOffset(e.End).ToUnixTimeMilliseconds(), allDay = e.AllDay })
            .ToArray();
        Post(new { type = "agenda", on = !string.IsNullOrWhiteSpace(AppSettings.Current.CalendarUrl), events = items });
    }

    void OnAgendaAlert(CalEvent e, bool started)
    {
        if (IsDisposed) return;
        BeginInvoke(new Action(() =>
        {
            PostAgenda();
            if (_hiddenByFullscreen) return;
            if (_notice && _noticeKind is not ("osd" or "act" or "app")) return;   // não atropela água/Pomodoro
            var lang = AppSettings.Current.Language;
            string title = started ? Strings.Get(lang, "calNow") : Strings.Get(lang, "calSoon").Replace("{0}", Agenda.SoonMin.ToString());
            ShowNotice("calendar", title, e.Title, null, null, started ? 10_000 : 8000, kind: "act");
            ChimeSound.Play();
        }));
    }

    /// <summary>Salvaram outro endereço da agenda nas configurações: baixa de novo. Devolve quantos compromissos achou (-1 = erro).</summary>
    public static async Task<int> CalendarChangedAsync()
    {
        Agenda.Apply(IsOn);
        if (!IsOn || string.IsNullOrWhiteSpace(AppSettings.Current.CalendarUrl)) { s_island?.PostAgenda(); return 0; }
        return await Agenda.FetchAsync();
    }

    // ---------- notificações dos outros apps (AppNotes.cs, só na versão da Store) ----------

    AppNotifications? _appNotes;

    async Task StartAppNotesAsync()
    {
        if (_appNotes != null || !AppNotifications.Supported || !AppSettings.Current.IslandAppNotes) return;
        if (!await AppNotifications.RequestAsync()) return;
        _appNotes = new AppNotifications();
        _appNotes.Arrived += n => { try { BeginInvoke(new Action(() => OnAppNote(n))); } catch { } };
        await _appNotes.StartAsync();
    }

    void StopAppNotes()
    {
        _appNotes?.Dispose();
        _appNotes = null;
    }

    void OnAppNote(AppNote n)
    {
        if (IsDisposed || _hiddenByFullscreen) return;
        if (_notice && _noticeKind is not ("osd" or "act" or "app")) return;   // aviso do Volum na tela: não atropela
        ShowNotice("app", n.App, n.Body.Length > 0 ? (n.Title.Length > 0 ? n.Title + ": " + n.Body : n.Body) : n.Title, null, null, 6000, kind: "app", img: n.Icon);
    }

    /// <summary>Ligou/desligou as notificações dos apps nas configurações. Devolve se o Windows deu a permissão.</summary>
    public static async Task<bool> SetAppNotesAsync(bool on)
    {
        if (!on) { s_island?.StopAppNotes(); return false; }
        if (!AppNotifications.Supported) return false;
        bool ok = await AppNotifications.RequestAsync();
        if (ok && IsOn) await s_island!.StartAppNotesAsync();
        return ok;
    }

    void StopActivities()
    {
        try { if (_outputWatch != null) _devEnum?.UnregisterEndpointNotificationCallback(_outputWatch); } catch { }
        _devEnum?.Dispose();
        _devEnum = null;
        _privacy?.Dispose();
        _privacy = null;
    }

    void OnOutputChanged(string id)
    {
        if (IsDisposed || id == _lastOutput) return;
        _lastOutput = id;
        _out?.Dispose(); _out = null;   // a onda passa a ouvir a saída nova
        if (_osdOn) HookAudio();        // e os indicadores de volume também
        try
        {
            using var dev = _devEnum!.GetDevice(id);
            uint form = 0;
            try { form = Convert.ToUInt32(dev.Properties[NAudio.CoreAudioApi.PropertyKeys.PKEY_AudioEndpoint_FormFactor].Value); } catch { }
            bool phones = form is 3 /* fone */ or 5 /* headset */;
            var lang = AppSettings.Current.Language;
            ShowActivity(phones ? "headphones" : "speaker", Strings.Get(lang, phones ? "actHeadphones" : "actOutput"), dev.FriendlyName);
        }
        catch { }
    }

    PrivacyWatch? _privacy;
    string[] _micApps = Array.Empty<string>(), _camApps = Array.Empty<string>();

    // Microfone/câmera: pontinho na ilha enquanto algum app usa; quando um app começa a usar, avisa quem é
    void OnPrivacy(string[] mic, string[] cam)
    {
        if (IsDisposed) return;
        var lang = AppSettings.Current.Language;
        var newCam = cam.Except(_camApps).FirstOrDefault();
        var newMic = mic.Except(_micApps).FirstOrDefault();
        _micApps = mic; _camApps = cam;
        Post(new { type = "privacy", mic, cam });
        if (newCam != null) ShowActivity("camera", Strings.Get(lang, "actCamInUse"), newCam);
        else if (newMic != null) ShowActivity("mic", Strings.Get(lang, "actMicInUse"), newMic);
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

    void ShowNotice(string icon, string title, string text, string? actionLabel, Action? action, int ms, double level = -1, string? kind = null, string? img = null)
    {
        _notice = true;
        kind ??= icon;
        bool resize = _mode == Mode.Notice && _noticeKind != kind;   // trocou de um aviso para outro de outro tamanho
        _noticeKind = kind;
        if (resize) MoveTo(Mode.Notice);
        _noticeAction = action;
        Post(new { type = "notice", icon, kind, title, text, action = actionLabel, level, img });
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
            _media.Changed += (info) => { if (!IsDisposed) BeginInvoke(new Action(() =>
            {
                _lastMedia = info;
                MediaKeys.PassMedia = info == null;   // sem nada tocando, as teclas de mídia seguem para o Windows (abrir o último player)
                Post(new { type = "media", media = info });
            })); };
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
    // ---------- esconder quando não estiver em uso (opção) ----------

    // Monitor escolhido nas configurações, se não for o principal (e ainda estiver conectado)
    static Screen? ChosenScreen()
    {
        var m = AppSettings.Current.IslandMonitor;
        if (m == "other") return Screen.AllScreens.FirstOrDefault(s => !s.Primary);
        if (string.IsNullOrEmpty(m) || m == "primary") return null;
        return Screen.AllScreens.FirstOrDefault(s => s.DeviceName == m && !s.Primary);
    }

    bool CanAutoHide => AppSettings.Current.IslandAutoHide && _mode is Mode.Closed or Mode.Idle && !_hover && !_notice && !_hiddenByAuto;

    // Começa (ou recomeça) a contar o tempo parada; qualquer outra coisa cancela
    void ArmAutoHide()
    {
        _autoHide.Stop();
        if (!CanAutoHide) return;
        _autoHide.Interval = Math.Clamp(AppSettings.Current.IslandAutoHideSec, 5, 300) * 1000;
        _autoHide.Start();
    }

    /// <summary>Mudaram a opção de esconder ou o monitor da ilha (Configurações → Ilha).</summary>
    public static void ApplyHideAndScreen()
    {
        if (!IsOn) return;
        var isl = s_island!;
        if (!AppSettings.Current.IslandAutoHide && isl._hiddenByAuto) { isl._hiddenByAuto = false; isl.UpdateShown(); }
        isl.ArmAutoHide();
        if (ChosenScreen() == null && isl._otherScreen != null && isl._fullWnd == IntPtr.Zero) isl._otherScreen = null;   // voltou para o principal
        isl.PostSettings();      // formato (embaixo é sempre flutuante) e a posição
        isl.Place(isl._mode);
        isl.CheckScreen();
    }

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
        // Teclas de volume e de mídia: só pela ilha (o aviso do Windows não aparece)
        MediaKeys.PassMedia = _lastMedia == null;
        MediaKeys.Start(vk => { try { BeginInvoke(new Action(() => OnMediaKey(vk))); } catch { } });
        _ = System.Threading.Tasks.Task.Run(StartBrightness);   // conectar no WMI leva um instante: fora da thread da janela
        HookFlyout();   // aviso de brilho do Windows (precisa estar na thread da janela, que recebe os avisos)
    }

    void StopOsd()
    {
        if (!_osdOn) return;
        _osdOn = false;
        RegisterKeyboard(false);
        UnhookAudio();
        MediaKeys.Stop();
        StopBrightness();
    }

    // ---------- teclas de volume e de mídia (MediaKeys.cs) ----------

    MediaInfo? _lastMedia;

    void OnMediaKey(int vk)
    {
        if (IsDisposed) return;
        var lang = AppSettings.Current.Language;
        switch (vk)
        {
            case MediaKeys.VolUp or MediaKeys.VolDown or MediaKeys.VolMute:
                KeyVolume(vk);
                break;
            case MediaKeys.PlayPause:
                _media?.PlayPause();
                bool willPlay = !(_lastMedia?.Playing ?? false);
                ShowMediaOsd(willPlay ? "play" : "pause", Strings.Get(lang, willPlay ? "osdPlay" : "osdPause"));
                break;
            case MediaKeys.Next: _media?.Next(); ShowMediaOsd("next", Strings.Get(lang, "osdNext")); break;
            case MediaKeys.Prev: _media?.Previous(); ShowMediaOsd("prev", Strings.Get(lang, "osdPrev")); break;
            case MediaKeys.StopKey: _media?.Stop(); ShowMediaOsd("pause", Strings.Get(lang, "osdPause")); break;
        }
    }

    // Como o Windows: 2% por toque, mudo liga/desliga, mexer no volume tira do mudo. Mostra sempre (mesmo já no 100%).
    void KeyVolume(int vk)
    {
        try
        {
            _osdEnum ??= new NAudio.CoreAudioApi.MMDeviceEnumerator();
            using var dev = _osdEnum.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            var ep = dev.AudioEndpointVolume;
            if (vk == MediaKeys.VolMute) ep.Mute = !ep.Mute;
            else
            {
                ep.MasterVolumeLevelScalar = Math.Clamp(ep.MasterVolumeLevelScalar + (vk == MediaKeys.VolUp ? 0.02f : -0.02f), 0f, 1f);
                if (ep.Mute) ep.Mute = false;
            }
            _lastVol = -1;   // força mostrar (o aviso do Windows sobre a mudança chega depois e é ignorado)
            ShowVolume(ep.MasterVolumeLevelScalar, ep.Mute);
        }
        catch { }
    }

    void ShowMediaOsd(string icon, string title) => ShowOsd(icon, title, _lastMedia?.Title ?? "", -1);

    // ---------- brilho da tela (notebooks): o Windows avisa pelo WMI quando muda ----------
    // A tecla de brilho não passa pelo teclado (o notebook trata direto; conferido no PC do Esdras), então não dá
    // para segurar a tecla como o volume. O aviso do Windows é uma janela do Explorer (XamlExplorerHostIslandWindow,
    // pequena, no meio, embaixo): quando ela aparece junto com uma mudança de brilho (até 1,5 s de diferença),
    // o Volum a esconde e mostra o brilho na ilha. Outros avisos do Windows nessa janela não são mexidos.
    // Em monitor de mesa o evento de brilho não existe (nada acontece).

    System.Management.ManagementEventWatcher? _bright;
    IntPtr _flyoutHook, _flyout;
    WinEventProc? _flyoutProc;   // guardado: o Windows chama este delegate enquanto o gancho existir
    uint _explorerPid;
    DateTime _lastBright = DateTime.MinValue, _flyoutAt = DateTime.MinValue;

    void OnBrightness(int b)
    {
        _lastBright = DateTime.UtcNow;
        if ((DateTime.UtcNow - _flyoutAt).TotalMilliseconds < 1500) HideFlyout();
        ShowOsd("bright", Strings.Get(AppSettings.Current.Language, "osdBright"), $"{b}%", b / 100.0);
    }

    // Ouve só as janelas que o Explorer mostra (o Windows avisa; nada fica checando)
    void HookFlyout()
    {
        UnhookFlyout();
        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return;
        GetWindowThreadProcessId(tray, out _explorerPid);
        _flyoutProc = OnShellShow;
        _flyoutHook = SetWinEventHook(0x8002 /* EVENT_OBJECT_SHOW */, 0x8002, IntPtr.Zero, _flyoutProc, _explorerPid, 0, 0);
    }

    void UnhookFlyout()
    {
        if (_flyoutHook != IntPtr.Zero) UnhookWinEvent(_flyoutHook);
        _flyoutHook = IntPtr.Zero;
    }

    // O Explorer reiniciou? (a cada 30 s, junto com a conferência do áudio)
    void RefreshFlyoutHook()
    {
        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return;
        GetWindowThreadProcessId(tray, out uint pid);
        if (pid != _explorerPid || _flyoutHook == IntPtr.Zero) HookFlyout();
    }

    void OnShellShow(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
    {
        if (obj != 0 || hwnd == IntPtr.Zero || !IsOsdFlyout(hwnd)) return;
        _flyout = hwnd;
        _flyoutAt = DateTime.UtcNow;
        if ((DateTime.UtcNow - _lastBright).TotalMilliseconds < 1500) HideFlyout();
    }

    // O aviso do Windows: classe do Explorer, pequeno, centralizado e encostado embaixo (acima da barra de tarefas)
    static bool IsOsdFlyout(IntPtr hwnd)
    {
        var cls = new System.Text.StringBuilder(64);
        GetClassName(hwnd, cls, cls.Capacity);
        if (cls.ToString() != "XamlExplorerHostIslandWindow") return false;
        if (!GetWindowRect(hwnd, out var r)) return false;
        float k = Math.Max(1, GetDpiForWindow(hwnd)) / 96f;
        var scr = Screen.FromHandle(hwnd).Bounds;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        return w < 420 * k && h < 140 * k
            && Math.Abs((r.Left + r.Right) / 2 - (scr.Left + scr.Right) / 2) < 60 * k
            && r.Bottom > scr.Bottom - 220 * k;
    }

    void HideFlyout()
    {
        if (_flyout != IntPtr.Zero && IsWindowVisible(_flyout) && IsOsdFlyout(_flyout)) ShowWindow(_flyout, 0 /* SW_HIDE */);
    }

    void StartBrightness()
    {
        try
        {
            var w = new System.Management.ManagementEventWatcher(new System.Management.ManagementScope(@"root\WMI"),
                new System.Management.EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            w.EventArrived += (_, e) =>
            {
                int b;
                try { b = Convert.ToInt32(e.NewEvent["Brightness"]); } catch { return; }
                try { BeginInvoke(new Action(() => OnBrightness(b))); } catch { }
            };
            w.Start();
            if (_osdOn) _bright = w; else w.Dispose();   // desligaram enquanto conectava
        }
        catch { }
    }

    void StopBrightness()
    {
        UnhookFlyout();
        var w = _bright;
        _bright = null;
        if (w == null) return;
        try { w.Stop(); } catch { }
        w.Dispose();
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
        Post(new { type = "mute", muted });
        if (_hover && _mode == Mode.Open) { Post(new { type = "volume", v, muted }); return; }   // ilha aberta: mostra dentro dela
        var lang = AppSettings.Current.Language;
        bool silent = muted || v < 0.005f;
        ShowOsd(silent ? "volmute" : "vol", Strings.Get(lang, muted ? "osdMuted" : "osdVolume"),
            muted ? "" : $"{Math.Round(v * 100)}%", muted ? 0 : v);
    }

    // Botão de alto-falante do player: liga/desliga o mudo do som geral (saída padrão do Windows)
    void ToggleMute()
    {
        try
        {
            _osdEnum ??= new NAudio.CoreAudioApi.MMDeviceEnumerator();
            using var dev = _osdEnum.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            var ep = dev.AudioEndpointVolume;
            ep.Mute = !ep.Mute;
            _lastVol = ep.MasterVolumeLevelScalar; _lastMute = ep.Mute;
            Post(new { type = "mute", muted = ep.Mute });
        }
        catch { }
    }

    // Estado do mudo para o botão do player (ao abrir a interface)
    void PostMute()
    {
        try
        {
            _osdEnum ??= new NAudio.CoreAudioApi.MMDeviceEnumerator();
            using var dev = _osdEnum.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            Post(new { type = "mute", muted = dev.AudioEndpointVolume.Mute });
        }
        catch { }
    }

    // Rodinha do mouse na ilha. Mostra o nível dentro da ilha aberta (mesmo com os indicadores desligados).
    void StepVolume(int d)
    {
        try
        {
            _osdEnum ??= new NAudio.CoreAudioApi.MMDeviceEnumerator();
            using var dev = _osdEnum.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            var ep = dev.AudioEndpointVolume;
            float v = Math.Clamp(ep.MasterVolumeLevelScalar + Math.Sign(d) * 0.02f, 0f, 1f);
            ep.MasterVolumeLevelScalar = v;
            if (ep.Mute && d > 0) ep.Mute = false;   // subir o volume tira do mudo
            _lastVol = v; _lastMute = ep.Mute;
            Post(new { type = "volume", v, muted = ep.Mute });
            TickSound.Play();
        }
        catch { }
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
        if (_notice && _noticeKind is not ("osd" or "act")) return;   // não atropela um aviso de verdade (água, Pomodoro...)
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
        if (_osdOn) { RefreshAudioHook(); RefreshFlyoutHook(); }   // trocou a saída/microfone padrão ou o Explorer reiniciou? (mesmo relógio de 30 s)
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
        Post(new { type = "settings", language = Strings.Normalize(s.Language), lite = s.LightMode, pomodoro = s.PomodoroEnabled, style = Floating ? "float" : "notch", bottom = AtBottom, skin = s.Skin });
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
                case "dpr":   // escala real do WebView (monitor × tamanho do texto do Windows): a janela acompanha
                {
                    double dpr = root.GetProperty("v").GetDouble();
                    float extra = (float)Math.Clamp(dpr / (DeviceDpi / 96.0), 0.5, 4.0);
                    if (Math.Abs(extra - _webScale) > 0.01f) { _webScale = extra; Place(_mode); }
                    break;
                }

                case "bubble":   // a bolha do Pomodoro apareceu/sumiu ao lado da ilha fechada
                {
                    bool on = root.GetProperty("on").GetBoolean();
                    if (on != _bubble) { _bubble = on; PlaceSize(_shown, 0); }
                    break;
                }

                case "ready":
                    _ready = true;
                    PostMute();
                    PostSettings();
                    PostPomodoro();
                    PostMode();
                    PostBattery();
                    OnWaterChanged();
                    _ = _media?.RefreshAsync();
                    CheckScreen();
                    PostAgenda();
                    PostWeather();
                    _ = StartAppNotesAsync();
                    Greet();
                    break;

                case "height":   // altura da ilha aberta (muda com a página e com a água)
                    _openH = Math.Clamp((int)Math.Ceiling(root.GetProperty("h").GetDouble()), 80, 400);
                    if (_mode == Mode.Open) MoveTo(Mode.Open);
                    break;

                case "hover":    // mouse entrou/saiu da ilha
                    _hover = root.GetProperty("on").GetBoolean();
                    if (_hover && !_notice) { _collapse.Stop(); SetMode(Mode.Open); }   // com aviso na tela, fica a faixa (os botões não saem do lugar)
                    else if (!_notice) { _collapse.Stop(); _collapse.Start(); }
                    ArmAutoHide();
                    break;

                case "idle":     // a ilha ficou um tempo sem uso: cresce um pouco para baixo e o mascote aparece maior
                    _idle = root.GetProperty("on").GetBoolean();
                    if (_mode is Mode.Closed or Mode.Idle) SetMode(Resting);
                    break;

                case "playPause": _media?.PlayPause(); break;
                case "next": _media?.Next(); break;
                case "seek": _media?.Seek(root.GetProperty("s").GetDouble()); break;   // arrastou a barra do tempo
                case "showSource": _media?.ShowSource(); break;   // clicou na capa: traz para a frente o app que está tocando
                case "prev": _media?.Previous(); break;
                case "openMixer": OpenMixer?.Invoke(); break;
                case "animDone":   // a forma parou: janela no tamanho exato (sem a folga), se não mudou de estado nesse meio-tempo
                    if (root.TryGetProperty("seq", out var sq) && sq.GetInt32() == _modeSeq) Place(_mode);
                    break;
                case "toggleMute":   // alto-falante do player: silencia / devolve o som
                    ToggleMute();
                    break;

                case "volStep":   // rodinha do mouse em cima da ilha: volume geral de 2 em 2%
                    StepVolume(root.GetProperty("d").GetInt32());
                    break;

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
            _dwell.Dispose();
            _autoHide.Dispose();
            StopOsd();
            StopActivities();
            StopAppNotes();
            Agenda.Changed -= OnAgendaChanged;
            Agenda.Alert -= OnAgendaAlert;
            Weather.Changed -= OnWeatherChanged;
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int max);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
