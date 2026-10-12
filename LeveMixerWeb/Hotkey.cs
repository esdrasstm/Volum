using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Volum;

/// <summary>
/// Atalho global para abrir o mixer de qualquer lugar, inclusive no meio de um jogo.
/// Leve: nada fica checando o teclado; o Windows avisa (WM_HOTKEY) só quando as teclas são apertadas.
/// As mensagens chegam numa janela invisível só de mensagens, que vive enquanto o app roda
/// (o MixerForm pode ser descartado no modo leve).
/// </summary>
static class GlobalHotkey
{
    // Modificadores do Windows (os mesmos números ficam salvos em AppSettings.HotkeyMods)
    public const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4;
    const uint NoRepeat = 0x4000;   // segurar as teclas não dispara de novo
    const int Id = 1;

    static Receiver? s_window;
    static uint s_mods, s_key;
    static bool s_paused;

    public static event Action? Pressed;

    /// <summary>Registra o atalho (key 0 = desligado). Falso se outro app já usa essas teclas.</summary>
    public static bool Set(uint mods, uint key)
    {
        s_window ??= new Receiver();
        UnregisterHotKey(s_window.Handle, Id);
        s_mods = mods;
        s_key = key;
        s_paused = false;
        return key == 0 || RegisterHotKey(s_window.Handle, Id, mods | NoRepeat, key);
    }

    /// <summary>Solta o atalho enquanto a pessoa grava um novo nas configurações (senão ele abriria/fecharia o mixer).</summary>
    public static void Pause()
    {
        if (s_window == null || s_paused) return;
        UnregisterHotKey(s_window.Handle, Id);
        s_paused = true;
    }

    /// <summary>Volta o atalho que estava antes da pausa (gravação cancelada ou mixer fechado no meio).</summary>
    public static void Resume()
    {
        if (s_paused) Set(s_mods, s_key);
    }

    sealed class Receiver : NativeWindow
    {
        public Receiver() => CreateHandle(new CreateParams { Parent = new IntPtr(-3) });   // HWND_MESSAGE

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam == (IntPtr)Id) Pressed?.Invoke();   // WM_HOTKEY
            base.WndProc(ref m);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
