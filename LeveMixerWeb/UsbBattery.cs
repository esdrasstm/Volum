using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Volum;

/// <summary>
/// Bateria dos aparelhos sem fio com receptor USB (2.4 GHz), que o Windows não mostra.
/// Duas formas, por enquanto:
///  1) Padrão HID: alguns receptores mandam a bateria num campo padrão (Battery Strength, Remaining Capacity)
///     que o Windows ignora. Serve para qualquer marca que siga o padrão; só lê, não manda nada.
///  2) Logitech HID++ 2.0 (receptores Unifying, Bolt e Lightspeed, e aparelhos Logitech ligados direto):
///     pergunta a bateria pelos recursos 0x1004 (Unified Battery), 0x1000 (Battery Status) ou 0x1001 (voltagem).
///     Só fala com aparelhos da Logitech (VID 046D), pelo canal próprio do HID++ (página 0xFF00).
/// Nada disso tem aviso do Windows quando a bateria muda, então confere de tempos em tempos (2 min), só com a
/// ilha e a opção ligadas, numa thread de fundo. Cada conferência leva poucos milissegundos por aparelho.
/// <see cref="Diagnose"/> monta um texto com os receptores conectados (sem números de série) para o usuário
/// mandar quando o aparelho dele não aparece: é assim que outras marcas entram depois.
/// </summary>
static class UsbBattery
{
    const ushort Logitech = 0x046D;

    public static IReadOnlyList<WirelessDevice> List { get; private set; } = Array.Empty<WirelessDevice>();
    public static event Action? Changed;

    /// <summary>Quantas conferências terminaram desde o Start (0 = a lista ainda não veio). O Devices usa para não
    /// avisar "conectou" dos receptores que já estavam ali ao ligar a ilha.</summary>
    public static int Scans { get; private set; }

    static readonly object s_gate = new();       // Start/Stop/Poke/fim da conferência (threads diferentes)
    static readonly object s_scanLock = new();   // uma varredura por vez (timer e "Copiar diagnóstico")
    static Timer? s_timer;
    static int s_busy;
    static int s_gen;   // muda a cada Start/Stop: conferência que terminar depois do Stop é descartada
    static DateTime s_lastRun;

    public static void Start()
    {
        lock (s_gate)
        {
            if (s_timer != null) return;
            s_gen++; Scans = 0;
            s_timer = new Timer(_ => Refresh(), null, 2000, 120_000);
        }
    }

    public static void Stop()
    {
        lock (s_gate)
        {
            s_gen++;
            s_timer?.Dispose(); s_timer = null;
            List = Array.Empty<WirelessDevice>();
            Scans = 0;
        }
    }

    /// <summary>Algum aparelho entrou ou saiu: confere de novo em 3 s (no máx. uma vez a cada 15 s).</summary>
    public static void Poke()
    {
        lock (s_gate)
        {
            if (s_timer == null) return;
            double since = (DateTime.Now - s_lastRun).TotalMilliseconds;
            try { s_timer.Change((long)Math.Max(3000, 15_000 - since), 120_000); } catch (ObjectDisposedException) { }
        }
    }

    static void Refresh()
    {
        if (Interlocked.Exchange(ref s_busy, 1) == 1) return;
        try
        {
            int gen = s_gen;
            s_lastRun = DateTime.Now;
            List<WirelessDevice> found;
            lock (s_scanLock) found = Scan(null);
            lock (s_gate)
            {
                if (gen != s_gen) return;   // a ilha (ou a opção) desligou no meio
                bool changed = found.Count != List.Count || found.Zip(List).Any(p => p.First != p.Second);
                List = found;
                Scans++;
                if (!changed && Scans > 1) return;
            }
            Changed?.Invoke();
        }
        catch (Exception e) { DevLog.Write("UsbBattery: " + e.Message); }
        finally { Volatile.Write(ref s_busy, 0); }
    }

    /// <summary>Texto para o usuário colar quando o aparelho dele não aparece (Configurações → Ilha).</summary>
    public static string Diagnose()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Volum {Updater.Version} · {(Updater.IsStore ? "Store" : "GitHub")} · Windows {Environment.OSVersion.Version}");
        sb.AppendLine();
        sb.AppendLine("[Windows / Bluetooth]");
        var bt = Devices.List.Where(d => !d.Id.StartsWith("usb:")).ToList();
        if (bt.Count == 0) sb.AppendLine("(nenhum aparelho com bateria)");
        foreach (var d in bt) sb.AppendLine($"- {d.Name} · {d.Battery}% · {d.Kind}");
        sb.AppendLine();
        sb.AppendLine("[USB / receptores]");
        var log = new List<string>();
        try { lock (s_scanLock) Scan(log); } catch (Exception e) { log.Add("erro: " + e.Message); }
        if (log.Count == 0) sb.AppendLine("(nenhum aparelho USB)");
        foreach (var l in log) sb.AppendLine(l);
        return sb.ToString();
    }

    // ---------- varredura ----------

    sealed class Col
    {
        public string Path = "";
        public string Key = "";            // o aparelho (VID:PID): junta as coleções dele
        public ushort Vid, Pid;
        public ushort UsagePage, Usage;
        public int InLen, OutLen, FeatLen;
        public string Maker = "", Product = "";
    }

    static List<WirelessDevice> Scan(List<string>? log)
    {
        var cols = Collections();
        var result = new List<WirelessDevice>();
        foreach (var dev in cols.GroupBy(c => c.Key).OrderBy(g => g.First().Vid).ThenBy(g => g.First().Pid))
        {
            var first = dev.First();
            var list = dev.ToList();
            string head = $"- {first.Vid:X4}:{first.Pid:X4} \"{first.Maker}\" \"{first.Product}\" · " +
                          string.Join(", ", list.Select(c => $"{c.UsagePage:X2}:{c.Usage:X2}"));
            var notes = new List<string>();
            string kind = KindFromUsages(list);

            // 1) padrão HID (um aparelho estranho não pode derrubar a varredura dos outros)
            WirelessDevice? std = null;
            foreach (var c in list)
            {
                int? b = null;
                try { b = StandardBattery(c, notes); } catch (Exception e) { notes.Add("padrão HID: erro " + e.GetType().Name); }
                if (b != null)
                {
                    std = new WirelessDevice($"usb:{first.Vid:X4}{first.Pid:X4}:std", Clean(first.Product, first.Maker), b.Value, kind);
                    break;
                }
            }
            if (log != null && notes.Count == 0) notes.Add("sem bateria no padrão HID");

            // 2) Logitech HID++ (se responder, fica no lugar do padrão HID: o mesmo aparelho não aparece duas vezes)
            var logi = new List<WirelessDevice>();
            if (first.Vid == Logitech)
            {
                var longCol = list.FirstOrDefault(c => c.UsagePage == 0xFF00 && c.Usage == 0x0002 && c.InLen >= 20);
                if (longCol == null) notes.Add("HID++: sem canal longo");
                else try { logi = Logi(longCol, first, notes); } catch (Exception e) { notes.Add("HID++: erro " + e.GetType().Name); }
            }
            if (logi.Count > 0) result.AddRange(logi);
            else if (std != null) result.Add(std);
            log?.Add(head);
            if (log != null) foreach (var n in notes) log.Add("    " + n);
        }
        return result.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static string Clean(string product, string maker)
    {
        var p = product.Trim();
        if (p.Length == 0) p = maker.Trim();
        return p.Length == 0 ? "USB" : p;
    }

    static string KindFromUsages(List<Col> cols)
    {
        if (cols.Any(c => c.UsagePage == 1 && c.Usage is 4 or 5)) return "gamepad";
        if (cols.Any(c => c.UsagePage == 1 && c.Usage == 2)) return "mouse";
        if (cols.Any(c => c.UsagePage == 1 && c.Usage == 6)) return "keyboard";
        if (cols.Any(c => c.UsagePage == 0x0B || c.UsagePage == 0x0C && c.Usage == 1 && cols.Count <= 2)) return "headphones";
        return "device";
    }

    // ---------- 1) padrão HID: Battery Strength (06:20), Remaining Capacity (85:66), Absolute State of Charge (85:65) ----------

    static int? StandardBattery(Col c, List<string> notes)
    {
        using var h = OpenRead(c.Path);
        if (h == null) return null;
        if (!HidD_GetPreparsedData(h, out var pp)) return null;
        try
        {
            foreach (var type in new[] { HidP_Feature, HidP_Input })
            {
                int len = type == HidP_Feature ? c.FeatLen : c.InLen;
                if (len <= 0) continue;
                foreach (var v in ValueCaps(pp, type))
                {
                    bool batt = v.Page == 0x06 && v.Usage == 0x20 || v.Page == 0x85 && v.Usage is 0x66 or 0x65;
                    if (!batt) continue;
                    var buf = new byte[len];
                    buf[0] = v.ReportId;
                    bool ok = type == HidP_Feature ? HidD_GetFeature(h, buf, len) : HidD_GetInputReport(h, buf, len);
                    if (!ok) { notes.Add($"campo de bateria {v.Page:X2}:{v.Usage:X2} (relatório {v.ReportId}) sem resposta"); continue; }
                    if (HidP_GetUsageValue(type, v.Page, v.Link, v.Usage, out uint raw, pp, buf, (uint)len) != HIDP_STATUS_SUCCESS) continue;
                    int pct = v.Max > v.Min ? (int)Math.Round(((long)raw - v.Min) * 100.0 / (v.Max - v.Min)) : (int)raw;
                    pct = Math.Clamp(pct, 0, 100);
                    notes.Add($"bateria pelo padrão HID ({v.Page:X2}:{v.Usage:X2}): {pct}%");
                    return pct;
                }
            }
        }
        finally { HidD_FreePreparsedData(pp); }
        return null;
    }

    readonly record struct VCap(ushort Page, byte ReportId, ushort Link, ushort Usage, int Min, int Max);

    static List<VCap> ValueCaps(IntPtr pp, int type)
    {
        var list = new List<VCap>();
        if (HidP_GetCaps(pp, out var caps) != HIDP_STATUS_SUCCESS) return list;
        ushort n = type == HidP_Feature ? caps.NumberFeatureValueCaps : caps.NumberInputValueCaps;
        if (n == 0) return list;
        var buf = Marshal.AllocHGlobal(72 * n);
        try
        {
            if (HidP_GetValueCaps(type, buf, ref n, pp) != HIDP_STATUS_SUCCESS) return list;
            for (int i = 0; i < n; i++)
            {
                IntPtr p = buf + 72 * i;
                bool isRange = Marshal.ReadByte(p, 12) != 0;
                ushort u0 = (ushort)Marshal.ReadInt16(p, 56), u1 = (ushort)Marshal.ReadInt16(p, 58);
                var page = (ushort)Marshal.ReadInt16(p, 0);
                var rid = Marshal.ReadByte(p, 2);
                var link = (ushort)Marshal.ReadInt16(p, 6);
                int min = Marshal.ReadInt32(p, 40), max = Marshal.ReadInt32(p, 44);
                if (!isRange) list.Add(new VCap(page, rid, link, u0, min, max));
                else for (int u = u0; u <= u1 && u - u0 < 64; u++) list.Add(new VCap(page, rid, link, (ushort)u, min, max));
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return list;
    }

    // ---------- 2) Logitech HID++ 2.0 ----------

    static readonly HashSet<ushort> Receivers = new()
    {
        0xC52B, 0xC532, 0xC534,                         // Unifying / nano
        0xC548,                                         // Bolt
        0xC539, 0xC53A, 0xC53D, 0xC53F, 0xC541, 0xC545, // Lightspeed
        0xC547, 0xC54D, 0xC543, 0xC544, 0xC546,
    };

    static List<WirelessDevice> Logi(Col col, Col first, List<string> notes)
    {
        var found = new List<WirelessDevice>();
        using var hpp = HidPP.Open(col.Path);
        if (hpp == null) { notes.Add("HID++: não abriu o canal"); return found; }
        bool receiver = Receivers.Contains(first.Pid) || first.Product.Contains("Receiver", StringComparison.OrdinalIgnoreCase);
        var targets = receiver ? new byte[] { 1, 2, 3, 4, 5, 6 } : new byte[] { 0xFF };
        foreach (var idx in targets)
        {
            var ping = hpp.Call(idx, 0x00, 1, new byte[] { 0, 0, 0x5A }, 400);
            if (ping == null) { if (!receiver) notes.Add("HID++: não respondeu"); continue; }
            if (ping[0] < 2) { notes.Add($"HID++ aparelho {idx}: versão 1.0 (sem suporte ainda)"); continue; }
            string name = DeviceName(hpp, idx, out string kind) ?? Clean(first.Product, first.Maker);
            int? pct = null; string how = "";
            byte f;
            if ((f = Feature(hpp, idx, 0x1004)) != 0 && hpp.Call(idx, f, 1) is { } u)
            {
                pct = u[0] > 0 ? u[0] : (u[1] & 8) != 0 ? 100 : (u[1] & 4) != 0 ? 60 : (u[1] & 2) != 0 ? 20 : (u[1] & 1) != 0 ? 5 : null;
                how = "1004";
            }
            else if ((f = Feature(hpp, idx, 0x1000)) != 0 && hpp.Call(idx, f, 0) is { } s && s[0] > 0)
            { pct = s[0]; how = "1000"; }
            else if ((f = Feature(hpp, idx, 0x1001)) != 0 && hpp.Call(idx, f, 0) is { } v)
            { pct = FromVoltage(v[0] << 8 | v[1]); how = $"1001 ({v[0] << 8 | v[1]} mV)"; }
            notes.Add($"HID++ aparelho {(idx == 0xFF ? "direto" : idx.ToString())}: \"{name}\" ({kind}) · " +
                      (pct != null ? $"bateria {pct}% ({how})" : "sem recurso de bateria"));
            if (pct != null)
                found.Add(new WirelessDevice($"usb:{first.Vid:X4}{first.Pid:X4}:{idx}", name, Math.Clamp(pct.Value, 0, 100), kind));
        }
        return found;
    }

    static byte Feature(HidPP h, byte idx, ushort feature)
    {
        var r = h.Call(idx, 0x00, 0, new[] { (byte)(feature >> 8), (byte)feature });
        return r?[0] ?? 0;
    }

    static string? DeviceName(HidPP h, byte idx, out string kind)
    {
        kind = "device";
        byte f = Feature(h, idx, 0x0005);
        if (f == 0) return null;
        if (h.Call(idx, f, 2) is { } t)
            kind = t[0] switch { 0 or 2 => "keyboard", 3 or 4 or 5 => "mouse", 8 => "headphones", 11 or 12 => "gamepad", _ => "device" };
        if (h.Call(idx, f, 0) is not { } c || c[0] == 0) return null;
        int len = Math.Min((int)c[0], 64);
        var bytes = new List<byte>();
        while (bytes.Count < len)
        {
            var part = h.Call(idx, f, 1, new[] { (byte)bytes.Count });
            if (part == null) break;
            bytes.AddRange(part.Take(Math.Min(16, len - bytes.Count)));
        }
        var name = Encoding.UTF8.GetString(bytes.ToArray()).TrimEnd('\0', ' ');
        return name.Length > 0 ? name : null;
    }

    // Bateria pela voltagem (pilha de lítio), igual à tabela usada pelo Solaar
    static readonly (int mv, int pct)[] Curve =
    {
        (4186, 100), (4067, 90), (3989, 80), (3922, 70), (3859, 60), (3811, 50),
        (3778, 40), (3751, 30), (3717, 20), (3671, 10), (3646, 5), (3579, 2), (3500, 0),
    };
    static int FromVoltage(int mv)
    {
        if (mv >= Curve[0].mv) return 100;
        for (int i = 1; i < Curve.Length; i++)
            if (mv >= Curve[i].mv)
            {
                var (hi, lo) = (Curve[i - 1], Curve[i]);
                return lo.pct + (mv - lo.mv) * (hi.pct - lo.pct) / (hi.mv - lo.mv);
            }
        return 0;
    }

    /// <summary>Canal HID++ (relatório longo 0x11, 20 bytes) de um aparelho Logitech.</summary>
    sealed class HidPP : IDisposable
    {
        const byte SwId = 0x0B;   // identifica as respostas do Volum (o G Hub e o Windows usam outros)
        readonly FileStream _fs;
        readonly byte[] _rx = new byte[64];
        HidPP(FileStream fs) { _fs = fs; }

        public static HidPP? Open(string path)
        {
            var h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); return null; }
            try { return new HidPP(new FileStream(h, FileAccess.ReadWrite, 1, true)); }
            catch { h.Dispose(); return null; }
        }

        /// <summary>Manda um pedido e espera a resposta certa. Nulo = erro, sem resposta ou aparelho desligado.</summary>
        public byte[]? Call(byte dev, byte feature, int fn, byte[]? args = null, int timeoutMs = 600)
        {
            var tx = new byte[20];
            tx[0] = 0x11; tx[1] = dev; tx[2] = feature; tx[3] = (byte)(fn << 4 | SwId);
            if (args != null) Array.Copy(args, 0, tx, 4, Math.Min(args.Length, 16));
            try
            {
                var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                // a escrita também tem prazo: um receptor travado não pode prender a thread para sempre
                using (var wcts = new CancellationTokenSource(timeoutMs))
                {
                    try { _fs.WriteAsync(tx, 0, tx.Length, wcts.Token).GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { return null; }
                }
                while (true)
                {
                    var left = until - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero) return null;
                    using var cts = new CancellationTokenSource(left);
                    int n;
                    try { n = _fs.ReadAsync(_rx, 0, _rx.Length, cts.Token).GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { return null; }
                    if (n < 7 || _rx[1] != dev) continue;
                    if (_rx[0] == 0x10 && _rx[2] == 0x8F && _rx[3] == feature && _rx[4] == tx[3]) return null;   // erro HID++ 1.0
                    if (_rx[0] != 0x11 || n < 20) continue;
                    if (_rx[2] == 0xFF && _rx[3] == feature && _rx[4] == tx[3]) return null;                     // erro HID++ 2.0
                    if (_rx[2] == feature && _rx[3] == tx[3]) return _rx.Skip(4).Take(16).ToArray();
                }
            }
            catch (Exception e) { DevLog.Write("HID++: " + e.Message); return null; }
        }

        public void Dispose() => _fs.Dispose();
    }

    // ---------- lista das coleções HID USB ----------

    static List<Col> Collections()
    {
        var list = new List<Col>();
        HidD_GetHidGuid(out var guid);
        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return list;
        try
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
            {
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int need, IntPtr.Zero);
                if (need <= 0) continue;
                var buf = Marshal.AllocHGlobal(need);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buf, need, out _, IntPtr.Zero)) continue;
                    var path = Marshal.PtrToStringUni(buf + 4) ?? "";
                    // só USB (Bluetooth o Windows já mostra; o caminho do USB tem "#vid_")
                    if (!path.Contains("#vid_", StringComparison.OrdinalIgnoreCase)) continue;
                    Col? c = null;
                    try { c = Describe(path); } catch (Exception e) { DevLog.Write("UsbBattery: " + e.Message); }
                    if (c != null) list.Add(c);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return list;
    }

    static Col? Describe(string path)
    {
        using var h = OpenRead(path);
        if (h == null) return null;
        var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        if (!HidD_GetAttributes(h, ref attr)) return null;
        var c = new Col
        {
            Path = path,
            Key = $"{attr.VendorID:X4}:{attr.ProductID:X4}",   // as partes do aparelho (mouse, teclas, canal HID++) ficam em interfaces diferentes
            Vid = attr.VendorID, Pid = attr.ProductID,
            Maker = Str(h, HidD_GetManufacturerString), Product = Str(h, HidD_GetProductString),
        };
        if (HidD_GetPreparsedData(h, out var pp))
        {
            try
            {
                if (HidP_GetCaps(pp, out var caps) == HIDP_STATUS_SUCCESS)
                {
                    c.UsagePage = caps.UsagePage; c.Usage = caps.Usage;
                    c.InLen = caps.InputReportByteLength; c.OutLen = caps.OutputReportByteLength; c.FeatLen = caps.FeatureReportByteLength;
                }
            }
            finally { HidD_FreePreparsedData(pp); }
        }
        return c;
    }

    delegate bool StrFn(SafeFileHandle h, byte[] buf, int len);
    static string Str(SafeFileHandle h, StrFn fn)
    {
        var buf = new byte[256];
        if (!fn(h, buf, buf.Length)) return "";
        return Encoding.Unicode.GetString(buf).Split('\0')[0].Trim();
    }

    /// <summary>Abre só para consultar (sem leitura/escrita): funciona até com mouse e teclado, que o Windows reserva.</summary>
    static SafeFileHandle? OpenRead(string path)
    {
        var h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid) { h.Dispose(); return null; }
        return h;
    }

    // ---------- Win32 ----------

    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
    const int DIGCF_PRESENT = 2, DIGCF_DEVICEINTERFACE = 0x10;
    const int HidP_Input = 0, HidP_Feature = 2;
    const int HIDP_STATUS_SUCCESS = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
            NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pp);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr pp);
    [DllImport("hid.dll")] static extern bool HidD_GetManufacturerString(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll")] static extern bool HidD_GetProductString(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll")] static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll")] static extern bool HidD_GetInputReport(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pp, out HIDP_CAPS caps);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type, IntPtr caps, ref ushort len, IntPtr pp);
    [DllImport("hid.dll")] static extern int HidP_GetUsageValue(int type, ushort page, ushort link, ushort usage, out uint value, IntPtr pp, byte[] report, uint len);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr hwnd, int flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid g, uint i, ref SP_DEVICE_INTERFACE_DATA d);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int need, IntPtr info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
