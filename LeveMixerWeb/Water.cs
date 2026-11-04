using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace Volum;

/// <summary>Lembrete de água: configuração e contador do dia. Fica em %AppData%\Volum\water.json,
/// separado do settings.json (o mixer guarda o dele inteiro e apagaria o que o lembrete mudou).</summary>
sealed class WaterData
{
    public bool Enabled { get; set; }
    public int GoalMl { get; set; } = 2000;      // meta do dia
    public int IntervalMin { get; set; } = 60;   // de quanto em quanto tempo lembrar
    public int CupMl { get; set; } = 250;        // quanto cada "copo" soma no contador
    public int TodayMl { get; set; }             // quanto já bebeu hoje
    public string Day { get; set; } = "";        // dia do contador (zera quando muda)
    public bool Sound { get; set; } = true;      // toca o som de notificação do Windows no aviso
}

/// <summary>
/// Lembra de beber água de tempos em tempos e conta o que a pessoa bebeu no dia.
/// Leve: um único timer que dispara no intervalo escolhido (ex.: 1 vez por hora), só com o lembrete ligado.
/// Insiste: se a pessoa não marcar um copo, lembra de novo em 10 min (ou no intervalo, se for menor).
/// Marcou um copo (no aviso, no mixer ou na ilha), o próximo lembrete volta a ser no intervalo.
/// Não lembra com a meta batida nem com jogo ou vídeo em tela cheia (tenta de novo em 5 min).
/// </summary>
static class Water
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Volum", "water.json");

    static readonly WaterData s_data = Load();
    static readonly Timer s_timer = new();
    static Notice? s_notice;

    /// <summary>O contador ou a configuração mudou (o mixer aberto se atualiza).</summary>
    public static event Action? Changed;

    public static WaterData Data
    {
        get { RollDay(); return s_data; }
    }

    public static void Start()
    {
        s_timer.Tick += (_, _) => Remind();
        Reschedule();
    }

    const int SnoozeMin = 10;   // sem "Bebi um copo", lembra de novo depois disso

    public static void Configure(bool enabled, int goalMl, int intervalMin, int cupMl, bool sound)
    {
        s_data.Enabled = enabled;
        s_data.GoalMl = Math.Clamp(goalMl, 500, 6000);
        s_data.IntervalMin = Math.Clamp(intervalMin, 5, 480);   // livre: de 5 min a 8 h
        s_data.CupMl = Math.Clamp(cupMl, 100, 1000);
        s_data.Sound = sound;
        Save();
        Reschedule();
        if (!enabled) s_notice?.Close();
        Changed?.Invoke();
    }

    /// <summary>Soma (ou tira, com número negativo) do contador de hoje.</summary>
    public static void Add(int ml)
    {
        RollDay();
        s_data.TodayMl = Math.Clamp(s_data.TodayMl + ml, 0, 20000);
        Save();
        if (ml > 0) Reschedule();   // bebeu agora: o próximo lembrete é no intervalo inteiro
        if (s_data.TodayMl >= s_data.GoalMl) s_notice?.Close();
        Changed?.Invoke();
    }

    /// <summary>"2 L", "1,25 L" ou "250 ml", com a vírgula ou o ponto do idioma.</summary>
    public static string Format(int ml, string lang)
    {
        var culture = CultureInfo.GetCultureInfo(lang == "en" ? "en-US" : lang == "es" ? "es-ES" : "pt-BR");
        return ml < 1000 ? $"{ml} ml" : string.Format(culture, "{0:0.##} L", ml / 1000.0);
    }

    static void Reschedule(int? minutes = null)
    {
        s_timer.Stop();
        if (!s_data.Enabled) return;
        s_timer.Interval = (minutes ?? s_data.IntervalMin) * 60_000;
        s_timer.Start();
    }

    static void Remind()
    {
        Reschedule();
        RollDay();
        if (!s_data.Enabled || s_data.TodayMl >= s_data.GoalMl) return;
        if (Fullscreen.ForegroundIsFullscreen()) { Reschedule(5); return; }   // jogo ou vídeo em tela cheia: tenta de novo em 5 min
        Show();
        Reschedule(Math.Min(SnoozeMin, s_data.IntervalMin));   // insiste até marcar um copo
    }

    /// <summary>Botão "Testar aviso" nas configurações: mostra o aviso agora, sem esperar o intervalo.</summary>
    public static void Test()
    {
        RollDay();
        Show();
    }

    // Com a ilha ligada, o aviso sai por ela; senão, na janelinha do canto de baixo
    static void Show()
    {
        var lang = AppSettings.Current.Language;
        int left = Math.Max(0, s_data.GoalMl - s_data.TodayMl);
        string text = string.Format(Strings.Get(lang, "waterLeft"), Format(left, lang), Format(s_data.GoalMl, lang));
        string drank = $"{Strings.Get(lang, "waterDrank")} (+{Format(s_data.CupMl, lang)})";
        DevLog.Write("lembrete de água");
        if (s_data.Sound) DropSound.Play();   // duas gotinhas, bem sutil
        // Na ilha, o aviso é uma faixa fina: texto e botão curtos
        string shortText = string.Format(Strings.Get(lang, "waterLeftShort"), Format(left, lang));
        if (IslandForm.Notify("drop", Strings.Get(lang, "waterTitle"), shortText, $"+{Format(s_data.CupMl, lang)}",
                () => Add(s_data.CupMl), 30_000)) return;

        if (s_notice != null) return;
        s_notice = new Notice(lang);
        s_notice.FormClosed += (_, _) => s_notice = null;
        s_notice.Show();
    }

    static void RollDay()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (s_data.Day == today) return;
        s_data.Day = today;
        s_data.TodayMl = 0;
        Save();
    }

    static WaterData Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<WaterData>(File.ReadAllText(FilePath)) ?? new WaterData();
        }
        catch { }
        return new WaterData();
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s_data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }


    /// <summary>
    /// Aviso "Hora de beber água" com a ilha desligada, na identidade do Volum (a mesma do mixer):
    /// fundo #090909, borda branca, gota azul de traço, Sora, botões em pílula "Acabei de beber +150ml"
    /// (azul) e "Não bebi :(" (branco). Não rouba o foco e some sozinho em 1 minuto
    /// ("Não bebi" e sumir sozinho = lembra de novo em 10 min).
    /// </summary>
    sealed class Notice : Form
    {
        static readonly Color Blue = Color.FromArgb(0x00, 0x84, 0xFF);
        readonly Timer _autoClose = new() { Interval = 60_000 };
        readonly string _title, _text;
        readonly Font _titleFont, _textFont;
        readonly float _k;

        public Notice(string lang)
        {
            _k = DeviceDpi / 96f;
            Ui.SetupPopup(this, 340, 96);

            _title = Strings.Get(lang, "waterTitle");
            int left = Math.Max(0, s_data.GoalMl - s_data.TodayMl);
            _text = string.Format(Strings.Get(lang, "waterLeft"), Compact(left, lang), Compact(s_data.GoalMl, lang));   // "3,1L"
            _titleFont = Sora.Get(FontStyle.Bold, 14 * _k);
            _textFont = Sora.Get(FontStyle.Regular, 11.5f * _k);

            var btnFont = Sora.Get(FontStyle.Regular, 11 * _k);
            var drank = new Pill($"{Strings.Get(lang, "waterJustDrank")} +{Compact(s_data.CupMl, lang)}", Blue, Color.White, btnFont, _k);
            var notYet = new Pill(Strings.Get(lang, "waterNotYet"), Color.White, Ui.Bg, btnFont, _k);
            drank.Location = new Point(Ui.Px(52, _k), Ui.Px(60, _k));
            notYet.Location = new Point(drank.Right + Ui.Px(6, _k), drank.Top);
            drank.Click += (_, _) => { Add(s_data.CupMl); Close(); };
            notYet.Click += (_, _) => Close();   // o lembrete insiste em 10 min
            Controls.AddRange(new Control[] { drank, notYet });

            var wa = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(wa.Right - Width - Ui.Px(12, _k), wa.Bottom - Height - Ui.Px(12, _k));

            _autoClose.Tick += (_, _) => Close();
            _autoClose.Start();
        }

        static string Compact(int ml, string lang) => Format(ml, lang).Replace(" ", "");

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Ui.Prepare(g);
            Ui.DrawDrop(g, Blue, 16 * _k, 16 * _k, _k);
            using var white = new SolidBrush(Color.White);
            using var dim = new SolidBrush(Ui.Dim);
            Sora.DrawAtBaseline(g, _title, _titleFont, white, 52 * _k, 30 * _k);
            Sora.DrawAtBaseline(g, _text, _textFont, dim, 52 * _k, 47 * _k);
            Ui.DrawWin10Border(this, g);
        }

        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams => Ui.PopupParams(base.CreateParams);
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Ui.ApplyFrame(Handle); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _autoClose.Dispose();
            base.Dispose(disposing);
        }
    }
}

/// <summary>
/// Desenho dos avisos sem WebView (água, teclado travado), na identidade do Volum: fundo #090909,
/// borda branca, Sora, ícones de traço de 1,6 px (os mesmos do mixer).
/// </summary>
static class Ui
{
    public static readonly Color Bg = Color.FromArgb(9, 9, 9);
    public static readonly Color Dim = Color.FromArgb(140, 140, 140);   // texto secundário (branco a ~50%)

    public static int Px(float v, float k) => (int)Math.Round(v * k);

    /// <summary>Janela sem borda, sempre por cima, fora da barra de tarefas, no tamanho dado (em px de 96 dpi).</summary>
    public static void SetupPopup(Form f, int w, int h)
    {
        float k = f.DeviceDpi / 96f;
        f.FormBorderStyle = FormBorderStyle.None;
        f.StartPosition = FormStartPosition.Manual;
        f.ShowInTaskbar = false;
        f.TopMost = true;
        f.Text = "Volum";
        f.BackColor = Bg;
        f.Size = new Size(Px(w, k), Px(h, k));
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(f, true);
    }

    public static CreateParams PopupParams(CreateParams cp)
    {
        cp.ExStyle |= 0x80 | 0x08000000;   // WS_EX_TOOLWINDOW (fora do Alt+Tab) | WS_EX_NOACTIVATE
        return cp;
    }

    /// <summary>Cantos arredondados, tema escuro e borda branca (Windows 11).</summary>
    public static void ApplyFrame(IntPtr hwnd)
    {
        try
        {
            int round = 2, dark = 1, border = 0x00FFFFFF;   // COLORREF branco
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));   // DWMWA_BORDER_COLOR
        }
        catch { }
    }

    /// <summary>No Windows 10 não há borda colorida do sistema: desenha aqui.</summary>
    public static void DrawWin10Border(Control c, Graphics g)
    {
        if (Environment.OSVersion.Version.Build >= 22000) return;
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        using var pen = new Pen(Color.White, 1);
        g.DrawRectangle(pen, 0, 0, c.Width - 1, c.Height - 1);
        g.SmoothingMode = mode;
    }

    public static void Prepare(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;   // GridFit come pedaços de letras da Sora
    }

    public static Pen Stroke(Color color, float k) => new(color, 1.6f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Gota de traço (mesmo desenho do mixer, caixa de 24 px) com o canto de cima em (x, y).</summary>
    public static void DrawDrop(Graphics g, Color color, float x, float y, float k)
    {
        PointF P(float px, float py) => new(x + px * k, y + py * k);
        using var path = new GraphicsPath();
        path.AddBezier(P(12, 2.8f), P(15.7f, 7.1f), P(18.6f, 10.8f), P(18.6f, 14.2f));
        path.AddArc(x + 5.4f * k, y + 7.6f * k, 13.2f * k, 13.2f * k, 0, 180);
        path.AddBezier(P(5.4f, 14.2f), P(5.4f, 10.8f), P(8.3f, 7.1f), P(12, 2.8f));
        path.CloseFigure();
        using var pen = Stroke(color, k);
        g.DrawPath(pen, path);
    }

    /// <summary>Teclado de traço (mesmo desenho do mixer, caixa de 24 px) com o canto de cima em (x, y).</summary>
    public static void DrawKeyboard(Graphics g, Color color, float x, float y, float k)
    {
        using var pen = Stroke(color, k);
        using var body = RoundRect(new RectangleF(x + 2.5f * k, y + 6 * k, 19 * k, 12 * k), 3 * k);
        g.DrawPath(pen, body);
        using var dot = new SolidBrush(color);
        foreach (var dx in new[] { 6.5f, 10f, 14f, 17.5f })
            g.FillEllipse(dot, x + (dx - 1.1f) * k, y + (10 - 1.1f) * k, 2.2f * k, 2.2f * k);
        g.DrawLine(pen, x + 8.5f * k, y + 14 * k, x + 15.5f * k, y + 14 * k);
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}

/// <summary>Botão em pílula com bordas suaves (o botão do WinForms não arredonda), texto em Sora.</summary>
sealed class Pill : Control
{
    readonly Color _back, _fore;
    bool _hover;

    public Pill(string text, Color back, Color fore, Font font, float k, int height = 22)
    {
        Text = text;
        _back = back;
        _fore = fore;
        Font = font;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        using var g = CreateGraphics();
        float textW = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic).Width;
        Size = new Size((int)Math.Ceiling(textW + 24 * k), Ui.Px(height, k));
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Ui.Prepare(g);
        g.Clear(Parent?.BackColor ?? Ui.Bg);
        var back = _hover ? (_back.GetBrightness() > .9f ? Color.FromArgb(225, 225, 225) : ControlPaint.Light(_back, .15f)) : _back;
        using var path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) / 2f);
        using var brush = new SolidBrush(back);
        g.FillPath(brush, path);
        // Centralizado pela linha de base (altura das maiúsculas da Sora ≈ 0,7 do tamanho)
        using var fore = new SolidBrush(_fore);
        float textW = g.MeasureString(Text, Font, PointF.Empty, StringFormat.GenericTypographic).Width;
        Sora.DrawAtBaseline(g, Text, Font, fore, (Width - textW) / 2, (Height + Font.Size * .7f) / 2);
    }
}

/// <summary>Fonte Sora (a mesma do mixer), lida de wwwroot/fonts. Se o arquivo não existir, usa a Segoe UI.</summary>
static class Sora
{
    static System.Drawing.Text.PrivateFontCollection? s_fonts;
    static bool s_tried;

    static FontFamily? Family(FontStyle style)
    {
        if (!s_tried)
        {
            s_tried = true;
            try
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "wwwroot", "fonts");
                var pfc = new System.Drawing.Text.PrivateFontCollection();
                foreach (var name in new[] { "Sora-Regular.ttf", "Sora-Bold.ttf" })
                {
                    var f = Path.Combine(dir, name);
                    if (File.Exists(f)) pfc.AddFontFile(f);
                }
                if (pfc.Families.Length > 0) s_fonts = pfc;
            }
            catch { }
        }
        if (s_fonts == null) return null;
        foreach (var fam in s_fonts.Families) if (fam.IsStyleAvailable(style)) return fam;
        return null;
    }

    /// <summary>Fonte em pixels.</summary>
    public static Font Get(FontStyle style, float px)
    {
        var fam = Family(style);
        return fam != null ? new Font(fam, px, style, GraphicsUnit.Pixel) : new Font("Segoe UI", px, style, GraphicsUnit.Pixel);
    }

    /// <summary>Escreve com a linha de base em <paramref name="baseline"/> (o DrawString usa o topo da linha).
    /// GDI+, porque o TextRenderer (GDI) não enxerga fontes carregadas pelo programa.</summary>
    public static void DrawAtBaseline(Graphics g, string text, Font font, Brush brush, float x, float baseline)
    {
        var fam = font.FontFamily;
        float ascent = font.Size * fam.GetCellAscent(font.Style) / fam.GetEmHeight(font.Style);
        // Como caminho com preenchimento "winding": a Sora tem contornos sobrepostos (ex.: o T e o l),
        // que o DrawString preenche no modo par-ímpar e viram buracos nas letras.
        using var path = new GraphicsPath(FillMode.Winding);
        path.AddString(text, fam, (int)font.Style, font.Size, new PointF(x, baseline - ascent), StringFormat.GenericTypographic);
        g.FillPath(brush, path);
    }
}
