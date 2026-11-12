using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Velopack;

namespace Volum;

static class Program
{
    /// <summary>Volum.exe --dev : habilita F12 (DevTools), abre o mixer ao iniciar e não fecha ao perder o foco.</summary>
    internal static bool DevMode { get; private set; }

    /// <summary>Volum.exe --test-update-notice : mostra o aviso "Volum atualizado" ao iniciar (para testar o visual).</summary>
    internal static bool TestUpdateNotice { get; private set; }

    /// <summary>Volum.exe --test-store-update : mostra o aviso de versão nova da Store em 3 s (o botão abre a página na Store).</summary>
    internal static bool TestStoreUpdate { get; private set; }

    /// <summary>Volum.exe --test-greet : o mascote da ilha dá oi ao iniciar, como se o PC tivesse acabado de ligar.</summary>
    internal static bool TestGreeting { get; private set; }

    /// <summary>Sinal que uma segunda cópia do Volum manda para a que já está aberta: "abra o mixer".</summary>
    internal const string OpenSignalName = "Volum_OpenMixer";

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>
    /// Comandos do equalizador. --eq-selftest roda o autoteste (resultado em %TEMP%olum-eq-selftest.txt);
    /// --eq-install / --eq-remove "id" e --eq-remove-all rodam elevados, chamados pela janela do equalizador.
    /// </summary>
    static bool EqCommand(string[] args)
    {
        if (args.Contains("--eq-selftest", StringComparer.OrdinalIgnoreCase))
        {
            var err = EqConfig.SelfTest();
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "volum-eq-selftest.txt"), err ?? "ok");
            Environment.Exit(err == null ? 0 : 1);
        }
        string[] actions = { "--eq-install", "--eq-remove", "--eq-remove-all" };
        int i = Array.FindIndex(args, a => actions.Contains(a, StringComparer.OrdinalIgnoreCase));
        if (i < 0) return false;
        string action = args[i].ToLowerInvariant();
        string id = action != "--eq-remove-all" && i + 1 < args.Length ? args[i + 1] : "";
        int r = Array.FindIndex(args, a => a.Equals("--eq-result", StringComparison.OrdinalIgnoreCase));
        string? result = r >= 0 && r + 1 < args.Length ? args[r + 1] : null;
        Environment.ExitCode = EqInstaller.RunCommand(action, id, result);
        return true;
    }

    /// <summary>Desinstalando o Volum: o equalizador sai do Windows junto (pede permissão; se negar, o efeito só deixa o som passar).</summary>
    static void RemoveEqOnUninstall()
    {
        try { File.Delete(EqConfig.BinPath); } catch { }   // sem curva, o efeito passa o som intacto
        try
        {
            if (!EqInstaller.AnyInstalled() || Environment.ProcessPath == null) return;
            var psi = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = true, Verb = "runas" };
            psi.ArgumentList.Add("--eq-remove-all");
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(25000);
        }
        catch { }
    }

    [STAThread]
    static void Main(string[] args)
    {
        // Velopack (instalador/atualizador): precisa ser a primeira coisa do app. Durante a
        // instalação, atualização e desinstalação ele roda o Volum rapidinho e sai daqui mesmo.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => { AutoStart.Set(false); RemoveEqOnUninstall(); })   // desinstalou: sai do "iniciar com o Windows" e o equalizador sai do sistema
            .Run();

        if (EqCommand(args)) return;

        DevMode = args.Contains("--dev", StringComparer.OrdinalIgnoreCase);
        TestUpdateNotice = args.Contains("--test-update-notice", StringComparer.OrdinalIgnoreCase);
        TestStoreUpdate = args.Contains("--test-store-update", StringComparer.OrdinalIgnoreCase);
        TestGreeting = args.Contains("--test-greet", StringComparer.OrdinalIgnoreCase);

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Uma única instância por vez. Abrir o Volum de novo (ícone fixado num dock, atalho da área de
        // trabalho, menu Iniciar) avisa o que já está rodando, que abre ou fecha o mixer.
        using var mutex = new Mutex(true, "Volum_SingleInstance", out bool created);
        if (!created)
        {
            AllowSetForegroundWindow(-1);   // quem foi clicado pode passar a vez: o mixer vem para a frente
            try
            {
                using var signal = EventWaitHandle.OpenExisting(OpenSignalName);
                signal.Set();
            }
            catch { /* versão antiga aberta, sem o sinal: não faz nada, como antes */ }
            return;
        }

        // Só a versão instalada mexe sozinha no "iniciar com o Windows": a cópia de desenvolvimento
        // (pasta bin) não pode trocar o caminho para ela mesma.
        if (Updater.IsRelease)
        {
            // Primeira execução: já deixa para abrir com o Windows (dá para desligar nas configurações)
            var settings = AppSettings.Current;
            if (!settings.FirstRunDone)
            {
                AutoStart.Set(true);
                settings.FirstRunDone = true;
                settings.LastSeenVersion = Updater.Version;   // instalou agora: tudo é novo, sem popup de novidades
                settings.Save();
            }
            AutoStart.RefreshPath();
        }

        using var tray = new TrayController();
        if (args.Contains("--open-eq", StringComparer.OrdinalIgnoreCase))   // Volum.exe --dev --open-eq: já abre o equalizador
        {
            var later = new System.Windows.Forms.Timer { Interval = 300 };
            later.Tick += (_, _) => { later.Dispose(); EqForm.ShowWindow(); };
            later.Start();
        }
        MemoryTrim.Run();
        Application.Run();
    }
}
