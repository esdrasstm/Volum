using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Velopack;

namespace Volum;

static class Program
{
    /// <summary>Volum.exe --dev : habilita F12 (DevTools), abre o mixer ao iniciar e não fecha ao perder o foco.</summary>
    internal static bool DevMode { get; private set; }

    /// <summary>Sinal que uma segunda cópia do Volum manda para a que já está aberta: "abra o mixer".</summary>
    internal const string OpenSignalName = "Volum_OpenMixer";

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    [STAThread]
    static void Main(string[] args)
    {
        // Velopack (instalador/atualizador): precisa ser a primeira coisa do app. Durante a
        // instalação, atualização e desinstalação ele roda o Volum rapidinho e sai daqui mesmo.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => AutoStart.Set(false))   // desinstalou: tira do "iniciar com o Windows"
            .Run();

        DevMode = args.Contains("--dev", StringComparer.OrdinalIgnoreCase);

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
        if (Updater.IsInstalled)
        {
            // Primeira execução: já deixa para abrir com o Windows (dá para desligar nas configurações)
            var settings = AppSettings.Load();
            if (!settings.FirstRunDone)
            {
                AutoStart.Set(true);
                settings.FirstRunDone = true;
                settings.Save();
            }
            AutoStart.RefreshPath();
        }

        using var tray = new TrayController();
        MemoryTrim.Run();
        Application.Run();
    }
}
