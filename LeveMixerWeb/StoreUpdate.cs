using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Services.Store;
using Timer = System.Windows.Forms.Timer;

namespace Volum;

/// <summary>
/// Aviso de versão nova para quem instalou pela Microsoft Store (lá quem atualiza é a Store, não o Velopack).
/// Confere 1 min depois de abrir e a cada 6 h. Achou: aviso na ilha (ou no cartão perto da bandeja, com a ilha
/// desligada) com o botão "Atualizar", que pede para a Store baixar e instalar (a Store mostra a confirmação e
/// fecha o Volum para trocar a versão). Se não der, abre a página do Volum na Store. Uma vez por execução;
/// com jogo/vídeo em tela cheia, espera e tenta de novo em 1 min. Na versão do GitHub não faz nada.
/// Desde a 0.6.0: antes do aviso, tenta ATUALIZAR SOZINHO (como a versão do GitHub): com o mixer fechado e sem tela cheia,
/// pede para a Store baixar e instalar em silêncio; o Windows fecha o Volum para trocar a versão e, como o app se registrou
/// com RegisterApplicationRestart, abre ele de novo sozinho. Se a Store não deixar (ou falhar), sai o aviso com "Atualizar".
/// Testar o aviso (sem a Store): Volum.exe --test-store-update
/// </summary>
static class StoreUpdate
{
    const int FirstMs = 60_000, EveryMs = 6 * 3600 * 1000, RetryMs = 60_000, ShowMs = 30_000;
    static Timer? s_timer;
    static bool s_shown;
    static StoreContext? s_ctx;
    static Form? s_owner;   // janela invisível: a Store precisa de uma janela "dona" para mostrar a confirmação
    static Func<bool> s_canInstall = () => true;
    static bool s_triedSilent;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int RegisterApplicationRestart(string? commandLine, int flags);

    /// <param name="canInstall">Pode instalar agora? (o mixer não está aberto)</param>
    public static void Start(Func<bool> canInstall)
    {
        if (!Updater.IsStore && !Program.TestStoreUpdate) return;
        s_canInstall = canInstall;
        // Quando a Store fecha o Volum para instalar a versão nova, o Windows abre ele de novo (vale depois de 60 s rodando)
        if (Updater.IsStore) { try { RegisterApplicationRestart(null, 0); } catch { } }
        s_timer = new Timer { Interval = Program.TestStoreUpdate ? 3000 : FirstMs };
        s_timer.Tick += async (_, _) => { s_timer.Stop(); await CheckAsync(); if (!s_shown) s_timer.Start(); };
        s_timer.Start();
    }

    static async Task CheckAsync()
    {
        s_timer!.Interval = EveryMs;
        IReadOnlyList<StorePackageUpdate>? updates = null;
        if (!Program.TestStoreUpdate)
        {
            try
            {
                updates = await Ctx().GetAppAndOptionalStorePackageUpdatesAsync();
                if (updates == null || updates.Count == 0) return;
            }
            catch (Exception ex) { DevLog.Write("Store: " + ex.Message); return; }
        }
        if (Fullscreen.ForegroundIsFullscreen() || !s_canInstall()) { s_timer.Interval = RetryMs; return; }   // não atrapalha jogo/vídeo nem o mixer aberto

        // Primeiro tenta atualizar sozinho, sem perguntar nada (uma vez por execução)
        if (updates != null && !s_triedSilent)
        {
            s_triedSilent = true;
            try
            {
                var ctx = Ctx();
                if (ctx.CanSilentlyDownloadStorePackageUpdates)
                {
                    DevLog.Write("Store: atualizando em silêncio");
                    var r = await ctx.TrySilentDownloadAndInstallStorePackageUpdatesAsync(updates);
                    if (r.OverallState is StorePackageUpdateState.Completed or StorePackageUpdateState.Deploying) return;   // o Windows fecha e reabre o Volum
                }
            }
            catch (Exception ex) { DevLog.Write("Store (silencioso): " + ex.Message); }
        }
        Show(updates);
    }

    static void Show(IReadOnlyList<StorePackageUpdate>? updates)
    {
        s_shown = true;
        var lang = AppSettings.Current.Language;
        string title = Strings.Get(lang, "storeUpdTitle"), text = Strings.Get(lang, "storeUpdText"), action = Strings.Get(lang, "storeUpdAction");
        Action install = () => _ = InstallAsync(updates);
        if (IslandForm.Notify("sparkle", title, text, action, install, ShowMs)) return;
        UpdateNotice.ShowCard(title, text, action, install);
    }

    static async Task InstallAsync(IReadOnlyList<StorePackageUpdate>? updates)
    {
        try
        {
            if (updates == null || updates.Count == 0) throw new InvalidOperationException("sem atualização (teste)");
            var r = await Ctx().RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
            if (r.OverallState is StorePackageUpdateState.Completed or StorePackageUpdateState.Downloading
                or StorePackageUpdateState.Deploying or StorePackageUpdateState.Pending) return;
            MixerForm.OpenSite("store");   // cancelou ou deu erro: abre a página na Store
        }
        catch (Exception ex)
        {
            DevLog.Write("Store: " + ex.Message);
            MixerForm.OpenSite("store");
        }
    }

    static StoreContext Ctx()
    {
        if (s_ctx != null) return s_ctx;
        s_ctx = StoreContext.GetDefault();
        s_owner ??= new Form { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, Size = new System.Drawing.Size(1, 1) };
        WinRT.Interop.InitializeWithWindow.Initialize(s_ctx, s_owner.Handle);   // app de desktop: a Store pede a janela dona
        return s_ctx;
    }
}
