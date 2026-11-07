using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Volum;

/// <summary>
/// Teclas de volume e de mídia (aumentar, abaixar, mudo, tocar/pausar, próxima, anterior, parar) passam
/// pelo Volum antes do Windows: o Volum faz a ação e mostra na ilha, e o Windows não mostra o aviso dele.
/// Gancho de teclado (WH_KEYBOARD_LL) numa thread só dele, que só olha essas 7 teclas e devolve todas as
/// outras na hora, sem guardar nada. Só existe com a ilha e os indicadores ligados. Se o Volum fechar,
/// o Windows tira o gancho e as teclas voltam ao normal.
/// </summary>
static class MediaKeys
{
    public const int VolMute = 0xAD, VolDown = 0xAE, VolUp = 0xAF, Next = 0xB0, Prev = 0xB1, StopKey = 0xB2, PlayPause = 0xB3;

    static Thread? _thread;
    static uint _threadId;
    static IntPtr _hook;
    static HookProc? _proc;   // guardado: o Windows chama este delegate enquanto o gancho existir
    static Action<int>? _onKey;

    public static bool Running => _thread != null;

    /// <summary>Sem nada tocando: as teclas de mídia seguem para o Windows (ele abre/retoma o último player).</summary>
    public static volatile bool PassMedia;

    /// <summary>Liga o gancho. onKey é chamado (na thread do gancho) a cada tecla apertada; repetir segurando também conta.</summary>
    public static void Start(Action<int> onKey)
    {
        if (_thread != null) return;
        _onKey = onKey;
        var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _proc = Hook;
            _hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, _proc, GetModuleHandle(null), 0);
            ready.Set();
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }   // o gancho precisa de uma fila de mensagens viva
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }) { IsBackground = true, Name = "Volum: teclas de mídia" };
        _thread.Start();
        ready.Wait(2000);
    }

    public static void Stop()
    {
        if (_thread == null) return;
        PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
        _onKey = null;
    }

    // Precisa ser rápido (o Windows desiste do gancho se demorar): só confere a tecla e avisa a ilha
    static IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int vk = Marshal.ReadInt32(lParam);
            bool media = vk is >= Next and <= PlayPause;
            if (vk is >= VolMute and <= PlayPause && !(media && PassMedia))
            {
                int msg = (int)wParam;
                if (msg is 0x0100 /* WM_KEYDOWN */ or 0x0104 /* WM_SYSKEYDOWN */)
                {
                    try { _onKey?.Invoke(vk); } catch { }
                }
                return (IntPtr)1;   // engole a tecla (apertar e soltar): o Windows não faz nada nem mostra o aviso dele
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
}
