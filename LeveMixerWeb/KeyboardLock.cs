using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace Volum;

/// <summary>
/// Trava o teclado para limpar (o teclado ou a tela). Travado, um gancho de teclado do Windows
/// (WH_KEYBOARD_LL) engole todas as teclas; destravado, o gancho sai e não custa nada.
/// O mouse continua livre: um aviso na tela tem o botão Destravar, e destrava sozinho em 10 min.
/// Ctrl+Alt+Del sempre funciona (o Windows não deixa bloquear) e, se o Volum fechar, o teclado volta.
/// </summary>
static class KeyboardLock
{
    const int Minutes = 10;

    static IntPtr s_hook;
    static HookProc? s_proc;   // guardado: o Windows chama este delegate enquanto o gancho existir
    static Notice? s_notice;
    static DateTime s_until;

    public static bool IsLocked => s_hook != IntPtr.Zero;

    public static void Lock()
    {
        if (IsLocked) return;
        s_proc = (code, wParam, lParam) => code >= 0 ? (IntPtr)1 : CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        s_hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, s_proc, GetModuleHandle(null), 0);
        if (s_hook == IntPtr.Zero) { s_proc = null; return; }
        s_until = DateTime.UtcNow.AddMinutes(Minutes);
        s_notice = new Notice(AppSettings.Current.Language);
        s_notice.Show();
        DevLog.Write("teclado travado");
    }

    public static void Unlock()
    {
        if (!IsLocked) return;
        UnhookWindowsHookEx(s_hook);
        s_hook = IntPtr.Zero;
        s_proc = null;
        var notice = s_notice;
        s_notice = null;
        notice?.Close();
        DevLog.Write("teclado destravado");
    }


    /// <summary>Aviso "Teclado travado" no centro de baixo da tela, com o tempo restante e o botão Destravar.
    /// Mesma identidade dos outros avisos (Ui em Water.cs): fundo #090909, borda branca, Sora, ícone de traço.</summary>
    sealed class Notice : Form
    {
        readonly Timer _tick = new() { Interval = 1000 };
        readonly string _lang, _title;
        readonly Font _titleFont, _textFont;
        readonly float _k;
        string _left = "";

        public Notice(string lang)
        {
            _lang = lang;
            _k = DeviceDpi / 96f;
            Ui.SetupPopup(this, 340, 64);
            _title = Strings.Get(lang, "kbLocked");
            _titleFont = Sora.Get(FontStyle.Bold, 13 * _k);
            _textFont = Sora.Get(FontStyle.Regular, 11 * _k);

            var unlock = new Pill(Strings.Get(lang, "kbUnlock"), Color.White, Ui.Bg, Sora.Get(FontStyle.Regular, 11.5f * _k), _k, 30);
            unlock.Location = new Point(Width - unlock.Width - Ui.Px(16, _k), (Height - unlock.Height) / 2);
            unlock.Click += (_, _) => Unlock();
            Controls.Add(unlock);

            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - Ui.Px(24, _k));

            _tick.Tick += (_, _) => UpdateLeft();
            UpdateLeft();
            _tick.Start();
        }

        // Tempo restante; acabou, destrava sozinho
        void UpdateLeft()
        {
            var left = s_until - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) { Unlock(); return; }
            _left = string.Format(Strings.Get(_lang, "kbLeft"), $"{(int)left.TotalMinutes}:{left.Seconds:00}");
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Ui.Prepare(g);
            Ui.DrawKeyboard(g, Color.White, 16 * _k, 20 * _k, _k);
            using var white = new SolidBrush(Color.White);
            using var dim = new SolidBrush(Ui.Dim);
            Sora.DrawAtBaseline(g, _title, _titleFont, white, 52 * _k, 28 * _k);
            Sora.DrawAtBaseline(g, _left, _textFont, dim, 52 * _k, 45 * _k);
            Ui.DrawWin10Border(this, g);
        }

        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams => Ui.PopupParams(base.CreateParams);
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Ui.ApplyFrame(Handle); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tick.Dispose();
            base.Dispose(disposing);
        }
    }

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr hMod, uint threadId);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? name);

}
