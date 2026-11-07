using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace Volum;

/// <summary>
/// Microfone e câmera em uso (como os pontinhos do iPhone). O próprio Windows anota no registro quem está usando:
/// HKCU\...\CapabilityAccessManager\ConsentStore\{microphone|webcam}\[NonPackaged\]&lt;app&gt;, com
/// LastUsedTimeStop = 0 enquanto o app usa. O Windows avisa quando essas chaves mudam (RegNotifyChangeKeyValue),
/// numa thread que só espera (nada fica checando). Avisa a ilha com a lista de apps usando cada um.
/// </summary>
sealed class PrivacyWatch : IDisposable
{
    const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

    public event Action<string[], string[]>? Changed;   // (apps usando o microfone, apps usando a câmera)

    readonly Thread _thread;
    readonly ManualResetEvent _stop = new(false);
    string _last = "";

    public PrivacyWatch()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "Volum: microfone e câmera" };
        _thread.Start();
    }

    void Loop()
    {
        using var mic = Registry.CurrentUser.OpenSubKey(Root + "microphone");
        using var cam = Registry.CurrentUser.OpenSubKey(Root + "webcam");
        if (mic == null && cam == null) return;
        using var micEv = new AutoResetEvent(false);
        using var camEv = new AutoResetEvent(false);
        Scan(mic, cam);
        while (true)
        {
            Arm(mic, micEv);
            Arm(cam, camEv);
            int i = WaitHandle.WaitAny(new WaitHandle[] { _stop, micEv, camEv });
            if (i == 0) return;
            if (_stop.WaitOne(250)) return;   // vários valores mudam de uma vez: espera assentar
            Scan(mic, cam);
        }
    }

    static void Arm(RegistryKey? key, AutoResetEvent ev)
    {
        if (key == null) return;
        // REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET, olhando as subchaves, sem esperar aqui
        RegNotifyChangeKeyValue(key.Handle.DangerousGetHandle(), true, 0x1 | 0x4, ev.SafeWaitHandle.DangerousGetHandle(), true);
    }

    void Scan(RegistryKey? mic, RegistryKey? cam)
    {
        var m = InUse(mic);
        var c = InUse(cam);
        string sig = string.Join("|", m) + "#" + string.Join("|", c);
        if (sig == _last) return;
        _last = sig;
        Changed?.Invoke(m, c);
    }

    // Apps com LastUsedTimeStop = 0 (usando agora); o próprio Volum fica de fora
    static string[] InUse(RegistryKey? key)
    {
        if (key == null) return Array.Empty<string>();
        var list = new List<string>();
        try
        {
            foreach (var name in key.GetSubKeyNames())
            {
                if (name == "NonPackaged")
                {
                    using var np = key.OpenSubKey(name);
                    if (np == null) continue;
                    foreach (var exe in np.GetSubKeyNames())
                    {
                        using var k = np.OpenSubKey(exe);
                        if (k != null && Active(k)) Add(list, ExeName(exe.Replace('#', '\\')));
                    }
                }
                else
                {
                    using var k = key.OpenSubKey(name);
                    if (k != null && Active(k)) Add(list, PackageName(name));
                }
            }
        }
        catch { }
        return list.ToArray();
    }

    static bool Active(RegistryKey k) =>
        k.GetValue("LastUsedTimeStop") is long stop && stop == 0 && k.GetValue("LastUsedTimeStart") is long start && start != 0;

    static void Add(List<string> list, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && !list.Contains(name)) list.Add(name);
    }

    // Programa comum: o nome do produto ("Discord"), senão o nome do arquivo
    static string? ExeName(string path)
    {
        if (string.Equals(Path.GetFileNameWithoutExtension(path), "Volum", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            if (File.Exists(path))
            {
                var v = FileVersionInfo.GetVersionInfo(path);
                var n = (v.FileDescription ?? "").Trim();
                if (n.Length is > 1 and < 40) return n;
            }
        }
        catch { }
        return Path.GetFileNameWithoutExtension(path);
    }

    // App da loja: "Microsoft.WindowsCamera_8wekyb3d8bbwe" → "WindowsCamera"
    static string PackageName(string family)
    {
        var n = family.Split('_')[0];
        int dot = n.LastIndexOf('.');
        return dot >= 0 && dot < n.Length - 1 ? n[(dot + 1)..] : n;
    }

    public void Dispose()
    {
        _stop.Set();
        _thread.Join(1000);
        _stop.Dispose();
    }

    [DllImport("advapi32.dll")]
    static extern int RegNotifyChangeKeyValue(IntPtr hKey, bool watchSubtree, uint filter, IntPtr hEvent, bool async);
}
