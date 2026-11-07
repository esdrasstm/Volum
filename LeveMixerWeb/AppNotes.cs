using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Volum;

/// <summary>Uma notificação de outro app, pronta para a ilha.</summary>
sealed record AppNote(string App, string Title, string Body, string? Icon);

/// <summary>
/// Notificações dos outros apps (WhatsApp, Discord, Teams...) na ilha. Só na versão da Microsoft Store:
/// o Windows só deixa ler as notificações (UserNotificationListener) com identidade de pacote e com a
/// permissão da pessoa ("Permitir que o Volum acesse suas notificações?"). O Volum só lê; não guarda nem envia nada.
/// O Windows avisa quando chega uma (NotificationChanged); se esse aviso não funcionar num app de desktop,
/// confere a lista a cada 2 s (só com a ilha e a opção ligadas).
/// Para aparecerem SÓ na ilha, a pessoa liga o "Não incomodar" do Windows: as notificações vão em silêncio
/// para a central e o Volum continua recebendo.
/// </summary>
sealed class AppNotifications : IDisposable
{
    public event Action<AppNote>? Arrived;

    readonly UserNotificationListener _listener = UserNotificationListener.Current;
    readonly HashSet<uint> _seen = new();
    System.Windows.Forms.Timer? _poll;
    bool _busy, _subscribed, _disposed;

    /// <summary>Só a versão da Store tem a identidade de pacote que o Windows exige.</summary>
    public static bool Supported => Updater.IsStore;

    /// <summary>Pede a permissão (o Windows mostra a pergunta na primeira vez). Chamar na thread da janela.</summary>
    public static async Task<bool> RequestAsync()
    {
        if (!Supported) return false;
        try { return await UserNotificationListener.Current.RequestAccessAsync() == UserNotificationListenerAccessStatus.Allowed; }
        catch { return false; }
    }

    public async Task StartAsync()
    {
        try
        {
            foreach (var n in await _listener.GetNotificationsAsync(NotificationKinds.Toast)) _seen.Add(n.Id);   // as que já estavam lá não aparecem
        }
        catch { }
        try { _listener.NotificationChanged += OnChanged; _subscribed = true; }
        catch
        {
            _poll = new System.Windows.Forms.Timer { Interval = 2000 };
            _poll.Tick += async (_, _) => await CheckAsync();
            _poll.Start();
        }
    }

    void OnChanged(UserNotificationListener sender, UserNotificationChangedEventArgs e)
    {
        if (e.ChangeKind == UserNotificationChangedKind.Added) _ = CheckAsync();
    }

    async Task CheckAsync()
    {
        if (_busy || _disposed) return;
        _busy = true;
        try
        {
            var list = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var now = new HashSet<uint>();
            foreach (var n in list)
            {
                now.Add(n.Id);
                if (!_seen.Add(n.Id)) continue;
                var note = await ReadAsync(n);
                if (note != null) Arrived?.Invoke(note);
            }
            _seen.IntersectWith(now);   // esquece as que já saíram da central
        }
        catch { }
        finally { _busy = false; }
    }

    static async Task<AppNote?> ReadAsync(UserNotification n)
    {
        try
        {
            var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            var texts = binding?.GetTextElements().Select(t => (t.Text ?? "").Trim()).Where(t => t.Length > 0).ToList() ?? new List<string>();
            string app = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
            if (texts.Count == 0 && app.Length == 0) return null;
            string title = texts.Count > 0 ? texts[0] : app;
            string body = string.Join(" · ", texts.Skip(1));
            return new AppNote(app, title, body, await LogoAsync(n));
        }
        catch { return null; }
    }

    // Ícone do app (32 px) como imagem embutida
    static async Task<string?> LogoAsync(UserNotification n)
    {
        try
        {
            var r = n.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(32, 32));
            using var s = await r.OpenReadAsync();
            using var ms = new MemoryStream();
            await s.AsStreamForRead().CopyToAsync(ms);
            return ms.Length is > 0 and < 200_000 ? "data:image/png;base64," + Convert.ToBase64String(ms.ToArray()) : null;
        }
        catch { return null; }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_subscribed) { try { _listener.NotificationChanged -= OnChanged; } catch { } }
        _poll?.Dispose();
    }
}
