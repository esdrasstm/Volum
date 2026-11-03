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
        s_notice = new Notice(AppSettings.Load().Language);
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
    /// Não rouba o foco de quem estiver na frente.</summary>
    sealed class Notice : Form
    {
        readonly Timer _tick = new() { Interval = 1000 };
        readonly Label _left;
        readonly string _lang;

        public Notice(string lang)
        {
            _lang = lang;
            float k = DeviceDpi / 96f;
            int pad = (int)(18 * k);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Text = "Volum";
            BackColor = Color.FromArgb(27, 22, 19);   // mesmo fundo sólido do mixer (tema escuro)
            Size = new Size((int)(360 * k), (int)(72 * k));

            var title = new Label
            {
                Text = Strings.Get(lang, "kbLocked"),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 11f),
                ForeColor = Color.FromArgb(247, 242, 239),
                Location = new Point(pad, (int)(13 * k)),
            };
            _left = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(168, 156, 149),
                Location = new Point(pad, (int)(40 * k)),
            };
            var unlock = new Button
            {
                Text = Strings.Get(lang, "kbUnlock"),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(237, 90, 35),   // laranja do Volum
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 10f),
                Size = new Size((int)(116 * k), (int)(38 * k)),
                Cursor = Cursors.Hand,
            };
            unlock.FlatAppearance.BorderSize = 0;
            unlock.FlatAppearance.MouseOverBackColor = Color.FromArgb(245, 137, 90);
            unlock.Location = new Point(Width - unlock.Width - pad, (Height - unlock.Height) / 2);
            unlock.Click += (_, _) => Unlock();
            Controls.AddRange(new Control[] { title, _left, unlock });

            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - (int)(24 * k));

            _tick.Tick += (_, _) => UpdateLeft();
            UpdateLeft();
            _tick.Start();
        }

        // Tempo restante; acabou, destrava sozinho
        void UpdateLeft()
        {
            var left = s_until - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) { Unlock(); return; }
            _left.Text = string.Format(Strings.Get(_lang, "kbLeft"), $"{(int)left.TotalMinutes}:{left.Seconds:00}");
        }

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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int round = 2, dark = 1;                              // cantos arredondados (Windows 11) e borda escura
                DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
                DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            }
            catch { }
        }

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

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
