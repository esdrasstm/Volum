using System;
using System.Drawing;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace Volum;

/// <summary>
/// Aviso discreto "Volum atualizado" perto da bandeja, uma vez por versão, depois que a atualização
/// silenciosa reinicia o app. Clicar abre o mixer (e lá dentro aparece o popup de novidades).
/// Com a ilha ligada, sai como faixa fina na ilha. Não aparece na instalação nova (lá tudo é novo)
/// nem com jogo/vídeo em tela cheia (tenta de novo a cada minuto). Sem nada para mostrar, não custa nada.
/// </summary>
static class UpdateNotice
{
    const int ShowMs = 15_000;
    static Card? s_card;

    /// <summary>Chamado ao iniciar. <paramref name="open"/> abre o mixer.</summary>
    public static void Start(Action open)
    {
        if (!ShouldShow() && !Program.TestUpdateNotice) return;
        var t = new Timer { Interval = 3000 };   // deixa o Windows terminar de abrir
        t.Tick += (_, _) =>
        {
            if (Fullscreen.ForegroundIsFullscreen()) { t.Interval = 60_000; return; }
            t.Dispose();
            if (!ShouldShow() && !Program.TestUpdateNotice) return;   // já abriu o mixer e viu as novidades
            Show(open);
        };
        t.Start();
    }

    // Atualizou para uma versão mais nova do que a das últimas novidades vistas, e o aviso dela ainda não saiu
    static bool ShouldShow()
    {
        var s = AppSettings.Current;
        var now = Updater.Version;
        if (s.UpdateNoticeVersion == now) return false;
        if (!Version.TryParse(now, out var current)) return false;
        return !Version.TryParse(s.LastSeenVersion ?? "0.0.0", out var seen) || current > seen;
    }

    static void Show(Action open)
    {
        var s = AppSettings.Current;
        s.UpdateNoticeVersion = Updater.Version;
        s.Save();

        var lang = s.Language;
        string title = Strings.Get(lang, "updTitle");
        string text = string.Format(Strings.Get(lang, "updText"), Updater.Version);
        string action = Strings.Get(lang, "updOpen");
        DevLog.Write("aviso de atualização");
        if (IslandForm.Notify("sparkle", title, text, action, open, ShowMs)) return;

        s_card?.Close();
        s_card = new Card(title, text, action, open);
        s_card.FormClosed += (_, _) => s_card = null;
        s_card.Show();
    }

    /// <summary>Cartão no canto de baixo à direita, na identidade do Volum (Ui em Water.cs).</summary>
    sealed class Card : Form
    {
        readonly Timer _autoClose = new() { Interval = ShowMs };
        readonly string _title, _text;
        readonly Font _titleFont, _textFont;
        readonly float _k;

        public Card(string title, string text, string action, Action open)
        {
            _k = DeviceDpi / 96f;
            Ui.SetupPopup(this, 340, 64);
            _title = title;
            _text = text;
            _titleFont = Sora.Get(FontStyle.Bold, 13 * _k);
            _textFont = Sora.Get(FontStyle.Regular, 11 * _k);
            Cursor = Cursors.Hand;

            void Open() { Close(); open(); }
            var btn = new Pill(action, Color.White, Ui.Bg, Sora.Get(FontStyle.Regular, 11 * _k), _k, 28);
            // Largura pelo texto (muda com o idioma): ícone + texto + 20 px + botão + margem
            using (var g = CreateGraphics())
            {
                float textW = Math.Max(g.MeasureString(_title, _titleFont, PointF.Empty, StringFormat.GenericTypographic).Width,
                                       g.MeasureString(_text, _textFont, PointF.Empty, StringFormat.GenericTypographic).Width);
                Width = Math.Max(Width, (int)Math.Ceiling(52 * _k + textW + 20 * _k + btn.Width + 16 * _k));
            }
            btn.Location = new Point(Width - btn.Width - Ui.Px(16, _k), (Height - btn.Height) / 2);
            btn.Click += (_, _) => Open();
            Controls.Add(btn);
            Click += (_, _) => Open();   // o cartão inteiro também abre

            var wa = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(wa.Right - Width - Ui.Px(12, _k), wa.Bottom - Height - Ui.Px(12, _k));

            _autoClose.Tick += (_, _) => Close();
            _autoClose.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Ui.Prepare(g);
            DrawSparkle(g, 16 * _k, 20 * _k);
            using var white = new SolidBrush(Color.White);
            using var dim = new SolidBrush(Ui.Dim);
            Sora.DrawAtBaseline(g, _title, _titleFont, white, 52 * _k, 28 * _k);
            Sora.DrawAtBaseline(g, _text, _textFont, dim, 52 * _k, 45 * _k);
            Ui.DrawWin10Border(this, g);
        }

        // Brilho de traço (mesmo peso dos outros ícones), caixa de 24 px com o canto de cima em (x, y)
        void DrawSparkle(Graphics g, float x, float y)
        {
            PointF P(float px, float py) => new(x + px * _k, y + py * _k);
            using var pen = Ui.Stroke(Color.White, _k);
            using var star = new System.Drawing.Drawing2D.GraphicsPath();
            star.AddBezier(P(10, 3), P(10.8f, 7.5f), P(12.5f, 9.2f), P(17, 10));
            star.AddBezier(P(17, 10), P(12.5f, 10.8f), P(10.8f, 12.5f), P(10, 17));
            star.AddBezier(P(10, 17), P(9.2f, 12.5f), P(7.5f, 10.8f), P(3, 10));
            star.AddBezier(P(3, 10), P(7.5f, 9.2f), P(9.2f, 7.5f), P(10, 3));
            star.CloseFigure();
            g.DrawPath(pen, star);
            g.DrawLine(pen, P(19, 15), P(19, 21));
            g.DrawLine(pen, P(16, 18), P(22, 18));
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
