using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Devices.Enumeration;

namespace Volum;

/// <summary>Um aparelho sem fio conectado, com a bateria que ele informa ao Windows.</summary>
sealed record WirelessDevice(string Id, string Name, int Battery, string Kind);

/// <summary>
/// Bateria dos aparelhos sem fio (fone, mouse, teclado, caixinha, controle). Usa o que o próprio Windows mostra em
/// Configurações → Bluetooth: a propriedade de bateria do aparelho ({104EA319-...} 2) e se ele está conectado
/// (o "contêiner" do aparelho, System.Devices.Connected). Tudo por observadores do Windows (DeviceWatcher):
/// nada fica consultando; quando a bateria muda ou um aparelho conecta, o Windows avisa.
/// Só roda com a ilha ligada. Receptores USB próprios (Logitech Lightspeed, Razer...) ainda não entram:
/// cada marca tem o seu protocolo.
/// </summary>
static class Devices
{
    const string Batt = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    const string ContainerKey = "System.Devices.ContainerId";
    const string ClassKey = "System.Devices.ClassGuid";
    const string ConnectedKey = "System.Devices.Connected";
    public const int LowAt = 20;   // avisa com 20% ou menos (uma vez; de novo só depois de passar de 30%)

    // Classes de aparelho do Windows (para o ícone)
    static readonly Guid Keyboard = new("4d36e96b-e325-11ce-bfc1-08002be10318");
    static readonly Guid Mouse = new("4d36e96f-e325-11ce-bfc1-08002be10318");
    static readonly Guid Media = new("4d36e96c-e325-11ce-bfc1-08002be10318");
    static readonly Guid AudioEndpoint = new("c166523c-fe0c-4a94-a586-f1a80cfbbf3e");
    static readonly Guid XboxComposite = new("d61ca365-5af4-4486-998b-9db4734c6ca3");

    sealed class Node { public string Container = ""; public Guid Class; public int? Battery; public string Name = ""; }
    sealed class Box { public string Name = ""; public bool Connected; }

    static readonly object s_lock = new();
    static readonly Dictionary<string, DeviceInformation> s_nodeInfo = new();
    static readonly Dictionary<string, Node> s_nodes = new();
    static readonly Dictionary<string, Box> s_boxes = new();
    static readonly HashSet<string> s_lowWarned = new();
    static DeviceWatcher? s_pnp, s_cont;
    static bool s_ready;   // os dois observadores já listaram o que existia (antes disso, "conectou" não é novidade)
    static int s_doneCount;
    static System.Threading.Timer? s_debounce;
    static HashSet<string> s_connectedBefore = new();

    /// <summary>Mudou a lista ou alguma bateria (vem de outra thread).</summary>
    public static event Action? Changed;
    /// <summary>Um aparelho conectou (já com a bateria, se ele informa).</summary>
    public static event Action<WirelessDevice>? Connected;
    /// <summary>Bateria fraca (20% ou menos), uma vez por descida.</summary>
    public static event Action<WirelessDevice>? Low;

    public static IReadOnlyList<WirelessDevice> List { get; private set; } = Array.Empty<WirelessDevice>();
    /// <summary>Quando foi o último aviso de "conectou" (para a ilha não repetir com o "fone conectado" da saída de áudio).</summary>
    public static DateTime LastConnectAt { get; private set; }

    public static void Apply(bool on)
    {
        if (on) Start(); else Stop();
    }

    static void Start()
    {
        if (s_pnp != null) return;
        try
        {
            s_ready = false; s_doneCount = 0;
            // Aparelhos (nós PnP): bateria, contêiner e classe. A lista inicial chega toda de uma vez (~0,4 s).
            s_pnp = DeviceInformation.CreateWatcher("", new[] { Batt, ContainerKey, ClassKey }, DeviceInformationKind.Device);
            s_pnp.Added += (_, d) => { lock (s_lock) { s_nodeInfo[d.Id] = d; Read(d); } Kick(); };
            s_pnp.Updated += (_, u) => { lock (s_lock) { if (s_nodeInfo.TryGetValue(u.Id, out var d)) { d.Update(u); Read(d); } } Kick(); };
            s_pnp.Removed += (_, u) => { lock (s_lock) { s_nodeInfo.Remove(u.Id); s_nodes.Remove(u.Id); } Kick(); };
            s_pnp.EnumerationCompleted += (_, _) => Done();
            // Contêineres (o aparelho inteiro): conectado ou não. Não procura aparelhos novos (não liga a busca do Bluetooth).
            s_cont = DeviceInformation.CreateWatcher("", new[] { ConnectedKey }, DeviceInformationKind.DeviceContainer);
            s_cont.Added += (_, d) => { lock (s_lock) s_boxes[Norm(d.Id)] = new Box { Name = d.Name, Connected = d.Properties.TryGetValue(ConnectedKey, out var c) && c is true }; Kick(); };
            s_cont.Updated += (_, u) =>
            {
                lock (s_lock)
                    if (s_boxes.TryGetValue(Norm(u.Id), out var b) && u.Properties.TryGetValue(ConnectedKey, out var c)) b.Connected = c is true;
                Kick();
            };
            s_cont.Removed += (_, u) => { lock (s_lock) s_boxes.Remove(Norm(u.Id)); Kick(); };
            s_cont.EnumerationCompleted += (_, _) => Done();
            s_pnp.Start();
            s_cont.Start();
        }
        catch (Exception e) { DevLog.Write("Devices: " + e.Message); Stop(); }
    }

    static void Stop()
    {
        foreach (var w in new[] { s_pnp, s_cont })
            try { if (w != null && w.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) w.Stop(); } catch { }
        s_pnp = s_cont = null;
        s_debounce?.Dispose(); s_debounce = null;
        lock (s_lock) { s_nodeInfo.Clear(); s_nodes.Clear(); s_boxes.Clear(); s_lowWarned.Clear(); s_connectedBefore.Clear(); }
        s_sawFirst = false;
        List = Array.Empty<WirelessDevice>();
    }

    static string Norm(string id) => id.Trim('{', '}').ToLowerInvariant();

    static void Read(DeviceInformation d)
    {
        var p = d.Properties;
        int? batt = p.TryGetValue(Batt, out var b) && b is byte by ? by : null;
        var node = new Node
        {
            Container = p.TryGetValue(ContainerKey, out var c) && c is Guid g ? g.ToString() : "",
            Class = p.TryGetValue(ClassKey, out var k) && k is Guid kg ? kg : Guid.Empty,
            Battery = batt is >= 0 and <= 100 ? batt : null,
            Name = d.Name,
        };
        s_nodes[d.Id] = node;
    }

    static void Done()
    {
        if (System.Threading.Interlocked.Increment(ref s_doneCount) == 2) { s_ready = true; Kick(); }
    }

    // Muitos avisos chegam juntos (um aparelho conectando mexe em vários nós): junta tudo em 400 ms
    static void Kick()
    {
        if (!s_ready) return;
        s_debounce ??= new System.Threading.Timer(_ => Rebuild());
        s_debounce.Change(400, System.Threading.Timeout.Infinite);
    }

    static void Rebuild()
    {
        List<WirelessDevice> list;
        lock (s_lock)
        {
            list = s_nodes.Values
                .Where(n => n.Battery != null && n.Container != "")
                .GroupBy(n => n.Container)
                .Where(g => s_boxes.TryGetValue(g.Key, out var box) && box.Connected)
                .Select(g =>
                {
                    var box = s_boxes[g.Key];
                    var classes = s_nodes.Values.Where(n => n.Container == g.Key).Select(n => n.Class).ToHashSet();
                    return new WirelessDevice(g.Key, box.Name, g.Max(n => n.Battery!.Value), KindOf(box.Name, classes));
                })
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        var before = s_connectedBefore;
        s_connectedBefore = list.Select(d => d.Id).ToHashSet();
        List = list;
        Changed?.Invoke();
        bool first = before.Count == 0 && !s_sawFirst;
        s_sawFirst = true;
        foreach (var d in list)
        {
            if (!first && !before.Contains(d.Id)) { LastConnectAt = DateTime.Now; Connected?.Invoke(d); }
            if (d.Battery <= LowAt) { if (s_lowWarned.Add(d.Id)) Low?.Invoke(d); }
            else if (d.Battery > 30) s_lowWarned.Remove(d.Id);
        }
    }
    static bool s_sawFirst;   // a primeira lista (ao ligar o Volum) não conta como "conectou"

    // Tipo do aparelho pelo nome (caixinhas e controles) e pelas classes dos nós dele (teclado, mouse, áudio)
    static string KindOf(string name, HashSet<Guid> classes)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("controller") || n.Contains("controle") || n.Contains("gamepad") || n.Contains("dualsense") || n.Contains("dualshock") || classes.Contains(XboxComposite))
            return "gamepad";
        if (n.Contains("speaker") || n.Contains("caixa") || n.Contains("soundbar") || n.Contains("boombox") || n.Contains("flip") || n.Contains("charge") || n.Contains("altavoz"))
            return "speaker";
        if (classes.Contains(Keyboard) || n.Contains("keyboard") || n.Contains("teclado") || n.Contains("keys")) return "keyboard";
        if (classes.Contains(Mouse) || n.Contains("mouse") || n.Contains("ratón")) return "mouse";
        if (classes.Contains(Media) || classes.Contains(AudioEndpoint) || n.Contains("buds") || n.Contains("fone") || n.Contains("headphone") || n.Contains("headset")) return "headphones";
        return "device";
    }
}
