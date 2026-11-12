using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Volum;

sealed class EqBand
{
    public int Type { get; set; } = EqConfig.Bell;
    public bool Enabled { get; set; } = true;
    public double FreqHz { get; set; } = 1000;
    public double GainDb { get; set; }
    public double Q { get; set; } = 1.41;
    public int Slope { get; set; } = 1;

    public EqBand Clone() => (EqBand)MemberwiseClone();
}

class EqCurve
{
    public bool Bypass { get; set; }
    public double PreampDb { get; set; }
    public bool AutoPreamp { get; set; } = true;
    public string Preset { get; set; } = "";
    public List<EqBand> Bands { get; set; } = new();

    public void CopyFrom(EqCurve other)
    {
        Bypass = other.Bypass;
        PreampDb = other.PreampDb;
        AutoPreamp = other.AutoPreamp;
        Preset = other.Preset;
        Bands = other.Bands.Select(b => b.Clone()).ToList();
    }
}

sealed class EqDeviceCurve : EqCurve
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

sealed class EqPreset : EqCurve
{
    public string Name { get; set; } = "";
}

sealed class EqDocument
{
    public int Version { get; set; } = EqConfig.DocVersion;
    public string ActiveId { get; set; } = "";
    public List<EqDeviceCurve> Devices { get; set; } = new();
    public List<EqPreset> Presets { get; set; } = new();
}

/// <summary>
/// Curvas do equalizador. Ficam em eq.json (pasta do Volum, por usuário) e em eq.bin
/// (ProgramData\Volum), que é o que o efeito do Windows lê. O formato do eq.bin bate com native/eq_params.h.
/// </summary>
static class EqConfig
{
    public const int Off = 0, Bell = 1, LowShelf = 2, HighShelf = 3, HighPass = 4, LowPass = 5, Notch = 6;
    public const int DocVersion = 2;
    const int HeaderBytes = 24, IdChars = 256, MaxDevices = 16;

    static readonly double[] GraphicFreqs = { 32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string JsonPath => Path.Combine(AppSettings.DataFolder, "eq.json");
    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Volum");
    public static string BinPath => Path.Combine(DataDir, "eq.bin");
    public static string LiveDir => Path.Combine(DataDir, "eq-live");

    public static bool IsGainType(int type) => type is Bell or LowShelf or HighShelf;
    public static bool IsCut(int type) => type is HighPass or LowPass;

    public static List<EqBand> FlatBands() =>
        GraphicFreqs.Select(f => new EqBand { Type = Bell, FreqHz = f, GainDb = 0, Q = 1.41 }).ToList();

    // ---------------------------------------------------------------- eq.json

    public static EqDocument Load()
    {
        try
        {
            if (File.Exists(JsonPath))
            {
                var doc = JsonSerializer.Deserialize<EqDocument>(File.ReadAllText(JsonPath), Json);
                if (doc != null && doc.Version == DocVersion)
                {
                    foreach (var c in doc.Devices.Cast<EqCurve>().Concat(doc.Presets)) Sanitize(c);
                    return doc;
                }
            }
        }
        catch { }
        return new EqDocument();
    }

    public static void SaveJson(EqDocument doc)
    {
        Directory.CreateDirectory(AppSettings.DataFolder);
        var tmp = JsonPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(doc, Json));
        File.Move(tmp, JsonPath, true);
    }

    public static EqDeviceCurve ForDevice(EqDocument doc, string id)
    {
        var found = doc.Devices.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        if (found != null) return found;
        var created = new EqDeviceCurve { Id = id, Bands = FlatBands(), Preset = "flat" };
        doc.Devices.Add(created);
        return created;
    }

    public static void Sanitize(EqCurve c)
    {
        c.Bands ??= new List<EqBand>();
        if (c.Bands.Count > EqNative.MaxBands) c.Bands = c.Bands.Take(EqNative.MaxBands).ToList();
        c.PreampDb = Clamp(c.PreampDb, -30, 12, 0);
        c.Preset ??= "";
        foreach (var b in c.Bands)
        {
            if (b.Type < Off || b.Type > Notch) b.Type = Bell;
            b.FreqHz = Clamp(b.FreqHz, 10, 22000, 1000);
            b.GainDb = Clamp(b.GainDb, -24, 24, 0);
            b.Q = Clamp(b.Q, 0.1, 24, 0.7071);
            b.Slope = (int)Clamp(b.Slope, 1, 4, 1);
        }
    }

    static double Clamp(double v, double lo, double hi, double fallback) =>
        double.IsFinite(v) ? Math.Clamp(v, lo, hi) : fallback;

    // ---------------------------------------------------------------- eq.bin

    /// <summary>Preamp que vai para o motor: o manual, ou o automático (tira o ganho máximo da curva para não clipar).</summary>
    public static double EffectivePreamp(EqCurve curve)
    {
        if (!curve.AutoPreamp) return curve.PreampDb;
        var probe = new EqCurve { Bands = curve.Bands, PreampDb = 0, AutoPreamp = false };
        return -Math.Max(0, MaxBoost(probe));
    }

    public static double MaxBoost(EqCurve curve)
    {
        var blob = ParamsBlob(curve, 0, false);
        double max = double.NegativeInfinity;
        void Probe(double f) => max = Math.Max(max, EqNative.Magnitude(blob, 48000, f));
        for (int i = 0; i <= 400; i++) Probe(20 * Math.Pow(1000, i / 400.0));
        foreach (var b in curve.Bands.Where(b => b.Enabled && IsGainType(b.Type))) Probe(Math.Clamp(b.FreqHz, 20, 20000));
        return double.IsFinite(max) ? Math.Round(max, 2) : 0;
    }

    public static byte[] ParamsBlob(EqCurve curve, double preampDb, bool installed)
    {
        var blob = new byte[EqNative.ParamsBytes];
        var span = blob.AsSpan();
        var bands = curve.Bands.Take(EqNative.MaxBands).ToList();
        BinaryPrimitives.WriteInt32LittleEndian(span, curve.Bypass ? 1 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], bands.Count);
        BinaryPrimitives.WriteInt32LittleEndian(span[8..], installed ? 1 : 0);
        BinaryPrimitives.WriteDoubleLittleEndian(span[16..], preampDb);
        for (int i = 0; i < bands.Count; i++)
        {
            var d = span[(24 + i * 40)..];
            var b = bands[i];
            BinaryPrimitives.WriteInt32LittleEndian(d, b.Type);
            BinaryPrimitives.WriteInt32LittleEndian(d[4..], b.Enabled ? 1 : 0);
            BinaryPrimitives.WriteInt32LittleEndian(d[8..], b.Slope);
            BinaryPrimitives.WriteDoubleLittleEndian(d[16..], b.FreqHz);
            BinaryPrimitives.WriteDoubleLittleEndian(d[24..], b.GainDb);
            BinaryPrimitives.WriteDoubleLittleEndian(d[32..], b.Q);
        }
        return blob;
    }

    public static byte[] BuildBin(EqDocument doc, Func<string, bool> installed)
    {
        // Os dispositivos ativados vêm primeiro: com mais de 16 dispositivos, nenhum ativado fica de fora.
        var devices = doc.Devices.OrderByDescending(d => installed(d.Id)).Take(MaxDevices).ToList();
        var bin = new byte[HeaderBytes + devices.Count * EqNative.RecordBytes];
        Encoding.ASCII.GetBytes("VOLUMEQ3").CopyTo(bin, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bin.AsSpan(8), EqNative.EngineVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(bin.AsSpan(12), (uint)devices.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(bin.AsSpan(16), EqNative.RecordBytes);
        for (int i = 0; i < devices.Count; i++)
        {
            var d = devices[i];
            int at = HeaderBytes + i * EqNative.RecordBytes;
            var id = d.Id.Length > IdChars - 1 ? d.Id[..(IdChars - 1)] : d.Id;
            Encoding.Unicode.GetBytes(id).CopyTo(bin, at);
            ParamsBlob(d, EffectivePreamp(d), installed(d.Id)).CopyTo(bin, at + IdChars * 2);
        }
        return bin;
    }

    /// <summary>Grava o eq.bin de forma atômica (o efeito nunca lê um arquivo pela metade).</summary>
    public static bool WriteBin(EqDocument doc, Func<string, bool> installed)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var tmp = BinPath + "." + Environment.ProcessId + ".tmp";
            File.WriteAllBytes(tmp, BuildBin(doc, installed));
            File.Move(tmp, BinPath, true);
            return true;
        }
        catch { return false; }
    }

    // ---------------------------------------------------------------- AutoEq / Equalizer APO

    /// <summary>
    /// Lê o texto paramétrico do AutoEq ou do Equalizer APO ("Preamp: -6 dB", "Filter 1: ON PK Fc 105 Hz Gain 3 dB Q 0.7").
    /// Devolve null se não achou filtros; trimmed = havia mais bandas do que cabem.
    /// </summary>
    public static EqCurve? Import(string text, out bool trimmed, out bool graphic)
    {
        trimmed = false;
        graphic = Regex.IsMatch(text, @"^\s*GraphicEQ\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var bands = new List<EqBand>();
        double preamp = 0;
        bool sawPreamp = false;
        const string Num = @"([+-]?\d+(?:[.,]\d+)?)";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var pre = Regex.Match(line, @"^Preamp\s*:\s*" + Num + @"\s*dB", RegexOptions.IgnoreCase);
            if (pre.Success)
            {
                preamp += ParseNum(pre.Groups[1].Value);
                sawPreamp = true;
                continue;
            }
            var f = Regex.Match(line,
                @"^Filter\s*\d*\s*:\s*(ON|OFF)\s+(PK|PEQ|MODAL|LSC|HSC|LS|HS|LSQ|HSQ|HP|HPQ|LP|LPQ|NO)\b(?:\s+(\d+)\s*dB)?\s+Fc\s+" + Num + @"\s*Hz" +
                @"(?:\s+Gain\s+" + Num + @"\s*dB)?(?:\s+(?:Q\s+" + Num + @"|BW\s+Oct\s+" + Num + @"))?",
                RegexOptions.IgnoreCase);
            if (!f.Success) continue;
            var kind = f.Groups[2].Value.ToUpperInvariant();
            int type = kind switch
            {
                "PK" or "PEQ" or "MODAL" => Bell,
                "LS" or "LSC" or "LSQ" => LowShelf,
                "HS" or "HSC" or "HSQ" => HighShelf,
                "HP" or "HPQ" => HighPass,
                "LP" or "LPQ" => LowPass,
                _ => Notch
            };
            double q;
            if (f.Groups[6].Success) q = ParseNum(f.Groups[6].Value);
            else if (f.Groups[7].Success)
            {
                double bw = Math.Pow(2, ParseNum(f.Groups[7].Value));
                q = Math.Sqrt(bw) / (bw - 1);
            }
            else q = type == Bell ? 1.41 : type == Notch ? 8 : 0.7071;
            bands.Add(new EqBand
            {
                Type = type,
                Enabled = f.Groups[1].Value.Equals("ON", StringComparison.OrdinalIgnoreCase),
                FreqHz = ParseNum(f.Groups[4].Value),
                GainDb = f.Groups[5].Success ? ParseNum(f.Groups[5].Value) : 0,
                Q = q,
                Slope = 1
            });
        }
        if (bands.Count == 0) return null;
        if (bands.Count > EqNative.MaxBands)
        {
            trimmed = true;
            // Fica com as bandas que mais mudam o som, na ordem original.
            bands = bands.Select((b, i) => (b, i))
                .OrderByDescending(x => !IsGainType(x.b.Type))
                .ThenByDescending(x => Math.Abs(x.b.GainDb))
                .Take(EqNative.MaxBands)
                .OrderBy(x => x.i)
                .Select(x => x.b)
                .ToList();
        }
        var curve = new EqCurve { Bands = bands, PreampDb = sawPreamp ? preamp : 0, AutoPreamp = !sawPreamp, Preset = "" };
        Sanitize(curve);
        return curve;
    }

    static double ParseNum(string s) => double.Parse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Texto no formato do Equalizer APO / AutoEq (dá para abrir em outros equalizadores).</summary>
    public static string Export(EqCurve curve)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("# Volum ").Append(Updater.Version).Append('\n');
        sb.Append("Preamp: ").Append(EffectivePreamp(curve).ToString("0.0#", inv)).Append(" dB\n");
        int n = 1;
        foreach (var b in curve.Bands)
        {
            if (b.Type == Off) continue;
            string on = b.Enabled ? "ON" : "OFF";
            string fc = b.FreqHz.ToString("0.##", inv), q = b.Q.ToString("0.###", inv), g = b.GainDb.ToString("0.0#", inv);
            switch (b.Type)
            {
                case Bell: sb.Append($"Filter {n}: {on} PK Fc {fc} Hz Gain {g} dB Q {q}\n"); break;
                case LowShelf: sb.Append($"Filter {n}: {on} LSC Fc {fc} Hz Gain {g} dB Q {q}\n"); break;
                case HighShelf: sb.Append($"Filter {n}: {on} HSC Fc {fc} Hz Gain {g} dB Q {q}\n"); break;
                case Notch: sb.Append($"Filter {n}: {on} NO Fc {fc} Hz Q {q}\n"); break;
                default:
                    // Corte de 24/36/48 dB: vira estágios Butterworth (o Equalizer APO não tem inclinação).
                    string kind = b.Type == HighPass ? "HPQ" : "LPQ";
                    int stages = Math.Clamp(b.Slope, 1, 4);
                    for (int s = 0; s < stages; s++)
                    {
                        double qs = 1 / (2 * Math.Sin(Math.PI * (2 * (s + 1) - 1) / (4.0 * stages)));
                        if (s == 0) qs *= b.Q / 0.70710678118654752;
                        sb.Append($"Filter {n}: {on} {kind} Fc {fc} Hz Q {qs.ToString("0.####", inv)}\n");
                        if (s + 1 < stages) n++;
                    }
                    break;
            }
            n++;
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- autoteste (--eq-selftest)

    public static string? SelfTest()
    {
        if (!EqNative.Available) return "VolumEqApo.dll não carregou";
        var smoke = EqNative.Smoke();
        if (smoke != null) return "efeito: " + smoke;

        const string sample = """
            Preamp: -6.2 dB
            Filter 1: ON LSC Fc 105 Hz Gain 6.5 dB Q 0.70
            Filter 2: ON PK Fc 3200,5 Hz Gain -2.4 dB Q 1.41
            Filter 3: OFF HSC Fc 10000 Hz Gain 3 dB Q 0.7
            Filter 4: ON PK Fc 1000 Hz Gain 2 dB BW Oct 1
            """;
        var c = Import(sample, out _, out _);
        if (c == null || c.Bands.Count != 4) return "importação";
        if (Math.Abs(c.PreampDb + 6.2) > 1e-9 || c.AutoPreamp) return "preamp importado";
        if (Math.Abs(c.Bands[1].FreqHz - 3200.5) > 1e-9 || c.Bands[2].Enabled) return "banda importada";
        if (Math.Abs(c.Bands[3].Q - 1.4142) > 0.01) return "BW em oitavas";

        var bell = new EqCurve { AutoPreamp = false, Bands = { new EqBand { Type = Bell, FreqHz = 1000, GainDb = 6, Q = 1.41 } } };
        double mag = EqNative.Magnitude(ParamsBlob(bell, -2, false), 48000, 1000);
        if (Math.Abs(mag - 4) > 0.01) return "magnitude " + mag.ToString(CultureInfo.InvariantCulture);
        bell.AutoPreamp = true;
        if (Math.Abs(EffectivePreamp(bell) + 6) > 0.05) return "preamp automático";

        var round = Import(Export(c), out _, out _);
        if (round == null || round.Bands.Count != 4 || Math.Abs(round.Bands[0].GainDb - 6.5) > 1e-6) return "exportação";

        var doc = new EqDocument();
        doc.Devices.Add(new EqDeviceCurve { Id = "a" });
        var bin = BuildBin(doc, _ => true);
        if (bin.Length != HeaderBytes + EqNative.RecordBytes) return "tamanho do eq.bin";
        return null;
    }
}
