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
/// Formas de ler:
///  1) Padrão HID: alguns receptores mandam a bateria num campo padrão (Battery Strength, Remaining Capacity)
///     que o Windows ignora. Serve para qualquer marca que siga o padrão; só lê, não manda nada.
///  2) Logitech HID++ 2.0 (receptores Unifying, Bolt e Lightspeed, e aparelhos Logitech ligados direto):
///     pergunta a bateria pelos recursos 0x1004 (Unified Battery), 0x1000 (Battery Status) ou 0x1001 (voltagem).
///     Só fala com aparelhos da Logitech (VID 046D), pelo canal próprio do HID++ (página 0xFF00). Também HID++ 1.0
///     (registradores) nos aparelhos antigos atrás do receptor. Referência do protocolo: documentação HID++ 2.0 da
///     Logitech e o Solaar (https://github.com/pwr-Solaar/Solaar, lib/logitech_receiver/hidpp20.py e hidpp10.py).
///  3) Protocolos de fabricante para VID:PID exatos (Sony, Attack Shark, Ajazz, ATK, Logitech PRO X 2): ver Known().
/// Licença: o Volum é MIT. Das fontes GPL (Solaar, HeadsetControl, libatk-rs, Linux) só foram usados fatos do
/// protocolo (bytes, posições, IDs, pontos da curva de voltagem); o código aqui foi escrito do zero, sem cópia nem
/// tradução linha a linha.
/// Nada disso tem aviso do Windows quando a bateria muda, então confere de tempos em tempos (2 min), só com a
/// ilha e a opção ligadas, numa thread de fundo. Cada conferência leva poucos milissegundos por aparelho.
/// <see cref="Diagnose"/> monta um texto com os receptores conectados (sem números de série) para o usuário
/// mandar quando o aparelho dele não aparece: é assim que outras marcas entram depois.
/// </summary>
static class UsbBattery
{
    const ushort Logitech = 0x046D;

    public static IReadOnlyList<WirelessDevice> List { get; private set; } = Array.Empty<WirelessDevice>();
    /// <summary>Receptores que parecem sem fio mas cuja bateria o Volum ainda não sabe ler ("bateria ainda não suportada").</summary>
    public static IReadOnlyList<UnsupportedDevice> Unsupported { get; private set; } = Array.Empty<UnsupportedDevice>();
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
            Unsupported = Array.Empty<UnsupportedDevice>();
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
            var unsup = new List<UnsupportedDevice>();
            lock (s_scanLock) found = Scan(null, unsup);
            lock (s_gate)
            {
                if (gen != s_gen) return;   // a ilha (ou a opção) desligou no meio
                bool changed = found.Count != List.Count || found.Zip(List).Any(p => p.First != p.Second)
                               || !unsup.SequenceEqual(Unsupported);
                List = found;
                Unsupported = unsup;
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

    static List<WirelessDevice> Scan(List<string>? log, List<UnsupportedDevice>? unsupported = null)
    {
        var cols = Collections();
        var result = new List<WirelessDevice>();
        foreach (var dev in cols.GroupBy(c => c.Key).OrderBy(g => g.First().Vid).ThenBy(g => g.First().Pid))
        {
            var first = dev.First();
            var list = dev.ToList();
            bool? wireless = LooksWireless(first, list);
            // Coisas que claramente não são sem fio (controle de LED da placa-mãe, microfone USB): fora do diagnóstico
            if (wireless != true && IsClearlyWired(first)) continue;
            string head = $"- {first.Vid:X4}:{first.Pid:X4} \"{first.Maker}\" \"{first.Product}\"{(wireless == true ? "" : " (com fio?)")} · " +
                          string.Join(", ", list.Select(c => $"{c.UsagePage:X2}:{c.Usage:X2}"));
            var notes = new List<string>();
            if (log != null)   // tamanhos dos relatórios (entrada/saída/recurso) de cada coleção: só descrição, nada enviado
                notes.Add("relatórios: " + string.Join(", ", list.Select(c => $"{c.UsagePage:X2}:{c.Usage:X2}={c.InLen}/{c.OutLen}/{c.FeatLen}")));
            string kind = KindFromUsages(list);
            int found = result.Count;

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
                else try { logi = Logi(longCol, first, list, notes, log != null); } catch (Exception e) { notes.Add("HID++: erro " + e.GetType().Name); }
            }
            if (logi.Count > 0) result.AddRange(logi);
            else if (std != null) result.Add(std);

            // 3) Protocolos de fabricante, só para os VID:PID exatos com fonte aberta (Known, mais abaixo)
            if (result.Count == found)
            {
                WirelessDevice? special = null;
                try { special = Known(first, list, kind, notes); } catch (Exception e) { notes.Add("protocolo do fabricante: erro " + e.GetType().Name); }
                if (special != null) result.Add(special);
            }
            // Parece sem fio e nenhum jeito deu a bateria: aparece como "bateria ainda não suportada"
            if (result.Count == found && wireless == true)
                unsupported?.Add(new UnsupportedDevice($"usb:{first.Vid:X4}{first.Pid:X4}:unsupported", Clean(first.Product, first.Maker), kind));
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
        ushort n = type == HidP_Feature ? caps.NumberFeatureValueCaps : type == HidP_Output ? caps.NumberOutputValueCaps : caps.NumberInputValueCaps;
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

    static List<WirelessDevice> Logi(Col col, Col first, List<Col> all, List<string> notes, bool diag)
    {
        var found = new List<WirelessDevice>();
        using var hpp = HidPP.Open(col.Path);
        if (hpp == null) { notes.Add("HID++: não abriu o canal"); return found; }
        bool receiver = Receivers.Contains(first.Pid) || first.Product.Contains("Receiver", StringComparison.OrdinalIgnoreCase);
        var targets = receiver ? new byte[] { 1, 2, 3, 4, 5, 6 } : new byte[] { 0xFF };
        // Canal curto (relatório 0x10, 7 bytes): só para o HID++ 1.0 dos aparelhos antigos atrás do receptor
        var shortCol = receiver ? all.FirstOrDefault(c => c.UsagePage == 0xFF00 && c.Usage == 0x0001 && c.InLen >= 7) : null;
        HidChan? shortChan = null;
        try
        {
            foreach (var idx in targets)
            {
                var ping = hpp.Call(idx, 0x00, 1, new byte[] { 0, 0, 0x5A }, 400);
                if (ping == null || ping[0] < 2)
                {
                    // Sem resposta no HID++ 2.0: pode ser um aparelho HID++ 1.0 (bateria por registrador)
                    if (shortCol != null)
                    {
                        shortChan ??= HidChan.Open(shortCol.Path, write: true);
                        if (shortChan != null && Hidpp10Battery(shortChan, idx, out int p10, out string how10))
                        {
                            notes.Add($"HID++ 1.0 aparelho {idx}: bateria {p10}% ({how10})");
                            found.Add(new WirelessDevice($"usb:{first.Vid:X4}{first.Pid:X4}:{idx}", Clean(first.Product, first.Maker) is var nm && nm.Contains("Receiver", StringComparison.OrdinalIgnoreCase) ? "Logitech" : nm, p10, KindFromUsages(all)));
                            continue;
                        }
                    }
                    if (!receiver) notes.Add(ping == null ? "HID++: não respondeu" : "HID++: versão 1.0 sem bateria");
                    continue;
                }
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
                // Diagnóstico: a lista dos recursos que o aparelho tem (IFeatureSet 0x0001, só leitura), para achar
                // em qual deles a bateria está nos aparelhos que ainda não mostram
                if (diag && pct == null) notes.Add("    recursos: " + FeatureList(hpp, idx));
                if (pct != null)
                    found.Add(new WirelessDevice($"usb:{first.Vid:X4}{first.Pid:X4}:{idx}", name, Math.Clamp(pct.Value, 0, 100), kind));
            }
        }
        finally { shortChan?.Dispose(); }
        return found;
    }

    static string FeatureList(HidPP h, byte idx)
    {
        byte fs = Feature(h, idx, 0x0001);
        if (fs == 0 || h.Call(idx, fs, 0) is not { } c) return "(não leu)";
        var ids = new List<string>();
        for (int i = 1; i <= Math.Min((int)c[0], 40); i++)
            if (h.Call(idx, fs, 1, new[] { (byte)i }) is { } r) ids.Add($"{r[0]:X2}{r[1]:X2}");
        return string.Join(" ", ids);
    }

    // ---------- Logitech HID++ 1.0: bateria por registrador ----------
    // Referência: Solaar (https://github.com/pwr-Solaar/Solaar, GPL-2.0-or-later, lib/logitech_receiver/hidpp10.py) e
    // ltunify (https://github.com/Lekensteyn/ltunify, registers.txt). Só lê (GET_REGISTER = 0x81), nada é gravado:
    //   pedido  10 <aparelho> 81 <registrador> 00 00 00
    //   0x0D (carga):   resposta[0] = % direto
    //   0x07 (status):  resposta[0] = nível 7/5/3/1 → 90/50/20/5 % (as faixas do Solaar); resposta[1] = carregando
    //   erro:           10 <aparelho> 8F 81 <registrador> <código> 00
    static bool Hidpp10Battery(HidChan ch, byte idx, out int pct, out string how)
    {
        pct = 0; how = "";
        if (ReadRegister(ch, idx, 0x0D) is { } c && ParseHidpp10Charge(c) is int p) { pct = p; how = "registrador 0x0D"; return true; }
        if (ReadRegister(ch, idx, 0x07) is { } s && ParseHidpp10Status(s) is int q) { pct = q; how = "registrador 0x07"; return true; }
        return false;
    }

    internal static int? ParseHidpp10Charge(byte[] r) => r[0] is > 0 and <= 100 ? r[0] : null;
    internal static int? ParseHidpp10Status(byte[] r) => r[0] switch { 7 => 90, 5 => 50, 3 => 20, 1 => 5, _ => (int?)null };

    static byte[]? ReadRegister(HidChan ch, byte idx, byte reg)
    {
        var tx = new byte[7] { 0x10, idx, 0x81, reg, 0, 0, 0 };
        if (!ch.Write(tx, 400)) return null;
        var until = DateTime.UtcNow.AddMilliseconds(400);
        while (ch.Read(until) is { } rx)
        {
            if (rx.Length < 7 || rx[0] != 0x10 || rx[1] != idx) continue;
            if (rx[2] == 0x8F && rx[3] == 0x81 && rx[4] == reg) return null;              // erro: não tem esse registrador
            if (rx[2] == 0x81 && rx[3] == reg) return new[] { rx[4], rx[5], rx[6] };
        }
        return null;
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

    // Bateria pela voltagem (pilha de lítio): pontos medidos da curva de descarga (mV → %), os mesmos que o Solaar
    // publica em https://github.com/pwr-Solaar/Solaar (lib/logitech_receiver/hidpp20.py); a conta entre eles é linear
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

    /// <summary>Uma coleção HID aberta para ler (e, só quando o protocolo pede, escrever), sempre com prazo.</summary>
    sealed class HidChan : IDisposable
    {
        readonly FileStream _fs;
        readonly byte[] _rx = new byte[128];
        HidChan(FileStream fs) { _fs = fs; }

        public static HidChan? Open(string path, bool write)
        {
            uint access = write ? GENERIC_READ | GENERIC_WRITE : GENERIC_READ;
            var h = CreateFile(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); return null; }
            try { return new HidChan(new FileStream(h, write ? FileAccess.ReadWrite : FileAccess.Read, 1, true)); }
            catch { h.Dispose(); return null; }
        }

        public bool Write(byte[] tx, int timeoutMs)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                _fs.WriteAsync(tx, 0, tx.Length, cts.Token).GetAwaiter().GetResult();
                return true;
            }
            catch (Exception e) { DevLog.Write("USB: escrita " + e.GetType().Name); return false; }
        }

        /// <summary>O próximo relatório de entrada (com o número do relatório no byte 0), ou nulo quando passa do prazo.</summary>
        public byte[]? Read(DateTime untilUtc)
        {
            var left = untilUtc - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) return null;
            try
            {
                using var cts = new CancellationTokenSource(left);
                int n = _fs.ReadAsync(_rx, 0, _rx.Length, cts.Token).GetAwaiter().GetResult();
                return n > 0 ? _rx.Take(n).ToArray() : null;
            }
            catch { return null; }
        }

        public void Dispose() => _fs.Dispose();
    }

    // ---------- protocolos de fabricante (só para os VID:PID exatos; fonte de cada um no comentário) ----------
    // Regras: só o pedido de LEITURA documentado pela fonte, só na coleção certa (página de uso e tamanhos conferidos),
    // prazo em toda escrita e leitura; não deu = o aparelho fica sem bateria.

    static WirelessDevice? Known(Col first, List<Col> list, string kind, List<string> notes)
    {
        string id = $"usb:{first.Vid:X4}{first.Pid:X4}:k";
        string name = Clean(first.Product, first.Maker);
        int? pct = null;
        switch (first.Vid, first.Pid)
        {
            // Sony DualShock 4 (05C4, v2 09CC, adaptador sem fio 0BA0) e DualSense (0CE6, Edge 0DF2), pelo cabo/adaptador USB.
            // Referência: Linux https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c (GPL-2.0):
            // relatório de entrada 0x01 que o controle manda
            // sozinho o tempo todo; DS4: byte 30 (nível 0–10 nos 4 bits de baixo, bit 4 = cabo); DualSense: byte 53
            // (nível nos 4 bits de baixo, estado de carga nos de cima: 2 = carregado). % = nível×10+5, no máx. 100.
            case (0x054C, 0x05C4 or 0x09CC or 0x0BA0):
                pct = ReadUnsolicited(list.FirstOrDefault(c => c.UsagePage == 1 && c.Usage == 5 && c.InLen >= 64), 600, r => ParseDs4(r), notes, "DualShock 4");
                kind = "gamepad"; if (name.Contains("Wireless Controller")) name = "DualShock 4";
                break;
            case (0x054C, 0x0CE6 or 0x0DF2):
                pct = ReadUnsolicited(list.FirstOrDefault(c => c.UsagePage == 1 && c.Usage == 5 && c.InLen >= 64), 600, r => ParseDualSense(r), notes, "DualSense");
                kind = "gamepad"; if (name.Contains("Wireless Controller")) name = "DualSense";
                break;

            // Attack Shark (R1, X11; receptor Beken 1D57:FA60). O receptor manda sozinho, a cada poucos segundos, um
            // relatório 0x03 de 5 bytes na coleção de "eventos" (página 0x0A): 03 <id> 40 <estado> <nível %>.
            // Fontes (MIT): https://github.com/HarukaYamamoto0/attack-shark-x11-driver (docs/messages/battery-status.md) e
            // https://gitlab.com/kxinha/Attack-Shark-Battery-KDE (README). Só lê; nunca escreve nada nesse receptor
            // (o mesmo projeto avisa que o relatório 0x10 reinicia o mouse no modo de gravação).
            case (0x1D57, 0xFA60):
                pct = ReadUnsolicited(list.FirstOrDefault(c => c.UsagePage == 0x0A && c.Usage == 0x00), 3500, r => ParseAttackShark(r), notes, "Attack Shark");
                if (name == "2.4G Wireless Device") name = "Attack Shark";
                kind = "mouse";
                break;

            // Ajazz AJ179 (receptor XCTECH 249A:5C2F, cabo 248A:5C2E). Interface 2 (MI_02), sem número de relatório,
            // 32 bytes de entrada e saída. Pedido "info" 0x10 (só leitura): 10 00 00 00 … soma; resposta: [0]=0x10,
            // [1]=0 (ok; 2 = mouse dormindo), [12]=carregando, [13]=bateria %, [14]=conectado. Também manda sozinho
            // C0 <online> <bateria %>. Fonte: https://github.com/Rockeyxx/aj179-linux (MIT, docs/PROTOCOL.md, src/protocol.c).
            case (0x249A, 0x5C2F) or (0x248A, 0x5C2E):
            {
                var c = list.FirstOrDefault(x => x.Path.Contains("&mi_02", StringComparison.OrdinalIgnoreCase) && x.InLen == 33 && x.OutLen == 33);
                if (c == null) { notes.Add("AJ179: canal de configuração (MI_02, 32 bytes) não encontrado"); break; }
                var req = new byte[33];   // [0] = número do relatório (0 = sem número); depois os 32 bytes
                req[1] = 0x10;            // comando "info"; seq, tamanho e soma ficam 0
                pct = Ask(c, req, 1500, r => ParseAj179(r), notes, "AJ179");
                if (name is "Wireless-Receiver" or "") name = "Ajazz AJ179";
                kind = "mouse";
                break;
            }

            // ATK / VXE (Compx, VID 373B: A9 PRO MAX 1117, "8K NANO dongle" 111B). Pedido GetBatteryLevel (0x04) no
            // relatório de saída 0x08, 17 bytes: 08 04 00 … 00 <soma = 0x55 − (0x08 + 0x04) = 0x49>; resposta no
            // relatório 0x08: [1]=0x04, [6]=bateria %. Fonte: https://github.com/cyberphantom52/libatk-rs (GPL-3.0,
            // src/types.rs e src/command.rs). Só na coleção de fabricante que declara o relatório de saída 0x08 de 17 bytes.
            case (0x373B, 0x1117 or 0x111B):
            {
                var c = list.FirstOrDefault(x => x.UsagePage >= 0xFF00 && x.OutLen == 17 && x.InLen >= 17 && OutputReportIds(x.Path).Contains((byte)0x08));
                if (c == null) { notes.Add("ATK: canal 0x08 (17 bytes) não encontrado"); break; }
                var req = new byte[17];
                req[0] = 0x08; req[1] = 0x04; req[16] = 0x49;
                pct = Ask(c, req, 1200, r => ParseAtk(r), notes, "ATK");
                kind = "mouse";
                break;
            }

            // Logitech G PRO X 2 LIGHTSPEED (headset, 046D:0AF7). Coleção FFA0:0001, relatório 0x51 de 64 bytes.
            // Pedido de bateria (o "legado" que funciona sem aperto de mão): 51 08 00 03 1A 00 03 00 04 0A 00…;
            // resposta: [1]=0x0B e [8]=0x04 → [10] = bateria %, [12]=2 carregando; [1]=0x05 e [6]=0 → desligado.
            // Fontes: HeadsetControl (https://github.com/Sapd/HeadsetControl, GPL-3.0, lib/devices/logitech_gpro_x2_lightspeed.hpp)
            // e o driver hid-logitech-headset proposto ao Linux (https://lwn.net/Articles/1093805, GPL-2.0-or-later).
            case (0x046D, 0x0AF7):
            {
                var c = list.FirstOrDefault(x => x.UsagePage == 0xFFA0 && x.Usage == 0x0001 && x.OutLen == 64);
                if (c == null) { notes.Add("PRO X 2: canal FFA0 (64 bytes) não encontrado"); break; }
                var req = new byte[64];
                new byte[] { 0x51, 0x08, 0x00, 0x03, 0x1A, 0x00, 0x03, 0x00, 0x04, 0x0A }.CopyTo(req, 0);
                pct = Ask(c, req, 1500, r => ParseProX2(r), notes, "PRO X 2");
                kind = "headphones";
                break;
            }
            default:
                return null;
        }
        return pct is int p ? new WirelessDevice(id, name, Math.Clamp(p, 0, 100), kind) : null;
    }

    // Só ouve: o aparelho manda o relatório sozinho. Lê até achar um que dê a bateria ou passar do prazo.
    static int? ReadUnsolicited(Col? c, int ms, Func<byte[], int?> parse, List<string> notes, string what)
    {
        if (c == null) { notes.Add($"{what}: coleção não encontrada"); return null; }
        using var ch = HidChan.Open(c.Path, write: false);
        if (ch == null) { notes.Add($"{what}: não abriu"); return null; }
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        int seen = 0;
        while (ch.Read(until) is { } r)
        {
            seen++;
            if (parse(r) is int p) { notes.Add($"{what}: bateria {p}%"); return p; }
            if (seen > 400) break;
        }
        notes.Add($"{what}: sem relatório de bateria ({seen} recebidos)");
        return null;
    }

    // Manda o pedido de leitura documentado e espera a resposta certa (até 8 relatórios ou o prazo)
    static int? Ask(Col c, byte[] req, int ms, Func<byte[], int?> parse, List<string> notes, string what)
    {
        using var ch = HidChan.Open(c.Path, write: true);
        if (ch == null) { notes.Add($"{what}: não abriu"); return null; }
        if (!ch.Write(req, ms)) { notes.Add($"{what}: pedido não foi aceito"); return null; }
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        for (int i = 0; i < 8 && ch.Read(until) is { } r; i++)
            if (parse(r) is int p) { notes.Add($"{what}: bateria {p}%"); return p; }
        notes.Add($"{what}: sem resposta de bateria (desligado ou dormindo?)");
        return null;
    }

    // As contas, separadas para dar para testar com bytes de exemplo das fontes
    internal static int? ParseDs4(byte[] r)
    {
        if (r.Length < 31 || r[0] != 0x01) return null;
        if (r[1] == 0 && r[2] == 0 && r[3] == 0 && r[4] == 0) return null;   // adaptador sem controle (manches zerados)
        int level = r[30] & 0x0F;
        return Math.Min(Math.Min(level, 10) * 10 + 5, 100);
    }

    internal static int? ParseDualSense(byte[] r)
    {
        if (r.Length < 54 || r[0] != 0x01) return null;
        if (r[1] == 0 && r[2] == 0 && r[3] == 0 && r[4] == 0) return null;
        int level = r[53] & 0x0F, state = r[53] >> 4;
        if (state == 2) return 100;                                         // carregado
        return Math.Min(Math.Min(level, 10) * 10 + 5, 100);
    }

    internal static int? ParseAttackShark(byte[] r) =>
        r.Length >= 5 && r[0] == 0x03 && (r[2] is 0x40 or 0x41) && r[4] <= 100 ? r[4] : null;   // 03 xx 01 00 00 = falha do rádio: ignora

    internal static int? ParseAj179(byte[] w)
    {
        if (w.Length < 16) return null;
        var r = w.AsSpan(1);   // tira o número do relatório (0)
        if (r[0] == 0x10 && r[1] == 0x00) return r[13] <= 100 && !(r[14] == 0 && r[13] == 0) ? r[13] : null;
        if (r[0] == 0xC0 && r[1] == 1 && r[2] <= 100) return r[2];
        return null;
    }

    internal static int? ParseAtk(byte[] r) => r.Length >= 7 && r[0] == 0x08 && r[1] == 0x04 && r[6] <= 100 ? r[6] : null;

    internal static int? ParseProX2(byte[] r) =>
        r.Length >= 13 && r[0] == 0x51 && r[1] == 0x0B && r[8] == 0x04 && r[10] <= 100 ? r[10] : null;

    // Números dos relatórios de saída que a coleção declara (só lê a descrição do Windows, nada é enviado)
    static HashSet<byte> OutputReportIds(string path)
    {
        var ids = new HashSet<byte>();
        using var h = OpenRead(path);
        if (h == null || !HidD_GetPreparsedData(h, out var pp)) return ids;
        try { foreach (var v in ValueCaps(pp, HidP_Output)) ids.Add(v.ReportId); }
        finally { HidD_FreePreparsedData(pp); }
        return ids;
    }

    // ---------- sem fio ou com fio? (só para o diagnóstico e para o "bateria ainda não suportada") ----------

    static readonly string[] WirelessWords = { "wireless", "2.4g", "receiver", "dongle", "nano", "lightspeed", "unifying", "bolt", "rf ", "sem fio" };

    static bool? LooksWireless(Col first, List<Col> list)
    {
        var s = (first.Maker + " " + first.Product).ToLowerInvariant() + " ";
        if (WirelessWords.Any(s.Contains)) return true;
        if (first.Vid == Logitech && Receivers.Contains(first.Pid)) return true;
        if (first.Vid == 0x054C && first.Pid is 0x0BA0) return true;
        return null;   // não dá para saber (pode ser com fio)
    }

    // Controle de LED da placa-mãe, microfone e placa de som USB: não têm bateria
    static bool IsClearlyWired(Col first)
    {
        var s = (first.Maker + " " + first.Product).ToLowerInvariant();
        return s.Contains("aura") || s.Contains("led controller") || s.Contains(" mic") || s.StartsWith("usb mic") || s.Contains("rgb controller");
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
    const int HidP_Input = 0, HidP_Output = 1, HidP_Feature = 2;
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
