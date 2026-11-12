using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace Volum;

/// <summary>Um compromisso da agenda (horário local).</summary>
sealed record CalEvent(string Title, DateTime Start, DateTime End, bool AllDay);

/// <summary>
/// Agenda na ilha: lê o endereço iCal (.ics) que a pessoa cola nas configurações. O Google Agenda e o Outlook
/// dão esse endereço ("endereço secreto no formato iCal" / "publicar calendário"). Funciona nas duas versões.
/// Baixa a agenda a cada 15 min (só com a ilha ligada e um endereço salvo) e avisa 10 min antes e na hora
/// de cada compromisso. O endereço fica só no settings.json deste PC; nada mais sai do computador.
/// Entende os compromissos que se repetem mais comuns (todo dia, toda semana em certos dias, todo mês, todo ano,
/// com intervalo, até uma data ou um número de vezes) e as exceções (EXDATE).
/// </summary>
static class Agenda
{
    public const int SoonMin = 10;

    /// <summary>Compromissos de hoje e de amanhã (mudou a lista).</summary>
    public static event Action<IReadOnlyList<CalEvent>>? Changed;
    /// <summary>Hora de avisar: (compromisso, já começou?).</summary>
    public static event Action<CalEvent, bool>? Alert;

    public static IReadOnlyList<CalEvent> Upcoming { get; private set; } = Array.Empty<CalEvent>();
    public static string LastError { get; private set; } = "";

    // No máximo 5 MB: um endereço errado apontando para um arquivo enorme não enche a memória
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 5 * 1024 * 1024 };
    static readonly Timer FetchTimer = new() { Interval = 15 * 60 * 1000 };
    static readonly Timer AlertTimer = new();
    static readonly HashSet<string> Alerted = new();
    static bool _on;

    static Agenda()
    {
        FetchTimer.Tick += async (_, _) => await FetchAsync();
        AlertTimer.Tick += (_, _) => { AlertTimer.Stop(); FireAlerts(); };
    }

    /// <summary>Liga/desliga conforme a ilha e o endereço salvo.</summary>
    public static void Apply(bool islandOn)
    {
        bool on = islandOn && !string.IsNullOrWhiteSpace(AppSettings.Current.CalendarUrl);
        if (on == _on && !on) return;
        _on = on;
        FetchTimer.Stop();
        AlertTimer.Stop();
        if (!on) { Upcoming = Array.Empty<CalEvent>(); Changed?.Invoke(Upcoming); return; }
        FetchTimer.Start();
        _ = FetchAsync();
    }

    /// <summary>Baixa e lê a agenda agora. Devolve quantos compromissos achou hoje e amanhã (-1 = erro).</summary>
    public static async Task<int> FetchAsync()
    {
        var url = (AppSettings.Current.CalendarUrl ?? "").Trim();
        if (url.Length == 0) return -1;
        if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url[9..];
        // Só endereço criptografado: o endereço da agenda é secreto e não pode trafegar aberto na rede
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) { LastError = "https"; return -1; }
        try
        {
            var text = await Http.GetStringAsync(url);
            if (!text.Contains("BEGIN:VCALENDAR")) { LastError = "notIcs"; return -1; }
            var from = DateTime.Today;
            Upcoming = Parse(text, from, from.AddDays(2)).OrderBy(e => e.Start).ToList();
            LastError = "";
            Changed?.Invoke(Upcoming);
            ScheduleAlerts();
            return Upcoming.Count;
        }
        catch { LastError = "fetch"; return -1; }
    }

    // ---------- avisos: 10 min antes e na hora ----------

    static void ScheduleAlerts()
    {
        AlertTimer.Stop();
        var now = DateTime.Now;
        DateTime? next = null;
        foreach (var e in Upcoming.Where(e => !e.AllDay))
        {
            foreach (var at in new[] { e.Start.AddMinutes(-SoonMin), e.Start })
                if (at > now && (next == null || at < next)) next = at;
        }
        if (next == null) return;
        AlertTimer.Interval = (int)Math.Clamp((next.Value - now).TotalMilliseconds + 300, 1000, int.MaxValue);
        AlertTimer.Start();
    }

    static void FireAlerts()
    {
        var now = DateTime.Now;
        foreach (var e in Upcoming.Where(e => !e.AllDay))
        {
            bool started = now >= e.Start && now < e.Start.AddMinutes(2);
            bool soon = !started && now >= e.Start.AddMinutes(-SoonMin) && now < e.Start;
            if (!started && !soon) continue;
            string key = $"{e.Title}|{e.Start:o}|{(started ? "now" : "soon")}";
            if (!Alerted.Add(key)) continue;
            Alert?.Invoke(e, started);
        }
        if (Alerted.Count > 200) Alerted.Clear();
        ScheduleAlerts();
    }

    // ---------- leitura do .ics ----------

    static IEnumerable<CalEvent> Parse(string ics, DateTime from, DateTime to)
    {
        var lines = Unfold(ics);
        Dictionary<string, (string Params, string Value)>? ev = null;
        var exdates = new List<(string, string)>();
        var result = new List<CalEvent>();
        var overrides = new HashSet<string>();   // UID + início de ocorrências alteradas (RECURRENCE-ID)
        var pending = new List<(Dictionary<string, (string, string)> Ev, List<(string, string)> Ex)>();

        foreach (var line in lines)
        {
            if (line == "BEGIN:VEVENT") { ev = new(); exdates = new(); continue; }
            if (line == "END:VEVENT") { if (ev != null) pending.Add((ev, exdates)); ev = null; continue; }
            if (ev == null) continue;
            int colon = IndexOfValue(line);
            if (colon < 0) continue;
            string head = line[..colon], value = line[(colon + 1)..];
            int semi = head.IndexOf(';');
            string name = (semi < 0 ? head : head[..semi]).ToUpperInvariant();
            string pars = semi < 0 ? "" : head[(semi + 1)..];
            if (name == "EXDATE") exdates.Add((pars, value));
            else ev[name] = (pars, value);
        }

        foreach (var (e, ex) in pending)
            if (e.TryGetValue("RECURRENCE-ID", out var rid) && e.TryGetValue("UID", out var u))
                overrides.Add(u.Item2 + "|" + ParseDate(rid.Item1, rid.Item2, out _)?.ToString("o"));

        foreach (var (e, ex) in pending)
        {
            if (!e.TryGetValue("DTSTART", out var ds)) continue;
            if (e.TryGetValue("STATUS", out var st) && st.Item2.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;
            var start = ParseDate(ds.Item1, ds.Item2, out bool allDay);
            if (start == null) continue;
            DateTime end;
            if (e.TryGetValue("DTEND", out var de) && ParseDate(de.Item1, de.Item2, out _) is DateTime d1) end = d1;
            else if (e.TryGetValue("DURATION", out var du) && TryDuration(du.Item2, out var span)) end = start.Value + span;
            else end = allDay ? start.Value.AddDays(1) : start.Value;
            var length = end - start.Value;
            string title = Unescape(e.TryGetValue("SUMMARY", out var s) ? s.Item2 : "");
            string uid = e.TryGetValue("UID", out var uu) ? uu.Item2 : "";
            bool isOverride = e.ContainsKey("RECURRENCE-ID");

            var skip = new HashSet<DateTime>();
            foreach (var (p, v) in ex)
                foreach (var part in v.Split(','))
                    if (ParseDate(p, part, out _) is DateTime x) skip.Add(x);

            IEnumerable<DateTime> starts = !isOverride && e.TryGetValue("RRULE", out var rr)
                ? Expand(start.Value, rr.Item2, to)
                : new[] { start.Value };
            foreach (var st0 in starts)
            {
                if (skip.Contains(st0)) continue;
                if (!isOverride && overrides.Contains(uid + "|" + st0.ToString("o"))) continue;   // essa ocorrência foi alterada: vale a versão alterada
                var en = st0 + length;
                if (en <= from || st0 >= to) continue;
                result.Add(new CalEvent(title.Length > 0 ? title : "—", st0, en, allDay));
            }
        }
        return result;
    }

    // Repetições (RRULE): FREQ, INTERVAL, UNTIL, COUNT, BYDAY (semanal)
    static IEnumerable<DateTime> Expand(DateTime start, string rule, DateTime to)
    {
        var r = rule.Split(';').Select(p => p.Split('=')).Where(p => p.Length == 2).ToDictionary(p => p[0].ToUpperInvariant(), p => p[1]);
        string freq = r.GetValueOrDefault("FREQ", "").ToUpperInvariant();
        int interval = int.TryParse(r.GetValueOrDefault("INTERVAL"), out var iv) && iv > 0 ? iv : 1;
        int count = int.TryParse(r.GetValueOrDefault("COUNT"), out var c) ? c : int.MaxValue;
        DateTime until = r.TryGetValue("UNTIL", out var u) && ParseDate("", u, out _) is DateTime ud ? ud : DateTime.MaxValue;
        var days = r.TryGetValue("BYDAY", out var bd)
            ? bd.Split(',').Select(d => d.Length >= 2 ? DayOf(d[^2..]) : (DayOfWeek?)null).Where(d => d != null).Select(d => d!.Value).ToHashSet()
            : null;

        int n = 0;
        for (int step = 0; step < 5000; step++)
        {
            DateTime baseDate = freq switch
            {
                "DAILY" => start.AddDays(step * interval),
                "WEEKLY" => start.AddDays(step * 7 * interval),
                "MONTHLY" => start.AddMonths(step * interval),
                "YEARLY" => start.AddYears(step * interval),
                _ => step == 0 ? start : DateTime.MaxValue
            };
            if (baseDate == DateTime.MaxValue) yield break;
            if (freq == "WEEKLY" && days != null && days.Count > 0)
            {
                // Semana de domingo a sábado: o domingo dessa semana pode vir antes da data-base (que cai no dia do início)
                var weekStart = baseDate.AddDays(-(int)baseDate.DayOfWeek);
                if (weekStart > until || weekStart >= to) yield break;
                for (int i = 0; i < 7; i++)
                {
                    var d = weekStart.AddDays(i);
                    if (!days.Contains(d.DayOfWeek) || d < start) continue;
                    if (d > until || d >= to) yield break;
                    if (++n > count) yield break;
                    yield return d;
                }
            }
            else
            {
                if (baseDate > until || baseDate >= to) yield break;
                if (++n > count) yield break;
                yield return baseDate;
            }
        }
    }

    static DayOfWeek? DayOf(string d) => d.ToUpperInvariant() switch
    {
        "SU" => DayOfWeek.Sunday, "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday,
        "TH" => DayOfWeek.Thursday, "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, _ => null
    };

    // Data/hora do .ics em horário local: "20261107T150000Z" (UTC), com TZID=..., ou dia inteiro (VALUE=DATE)
    static DateTime? ParseDate(string pars, string v, out bool allDay)
    {
        v = v.Trim();
        allDay = v.Length == 8 || pars.Contains("VALUE=DATE", StringComparison.OrdinalIgnoreCase) && !v.Contains('T');
        if (allDay)
            return DateTime.TryParseExact(v[..Math.Min(8, v.Length)], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        bool utc = v.EndsWith('Z');
        if (!DateTime.TryParseExact(v.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return null;
        if (utc) return DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime();
        var tzid = pars.Split(';').FirstOrDefault(p => p.StartsWith("TZID=", StringComparison.OrdinalIgnoreCase))?[5..].Trim('"');
        if (tzid != null)
        {
            try { return TimeZoneInfo.ConvertTime(t, TimeZoneInfo.FindSystemTimeZoneById(tzid), TimeZoneInfo.Local); }
            catch { }
        }
        return t;   // sem fuso: hora local
    }

    static bool TryDuration(string v, out TimeSpan span)
    {
        try { span = System.Xml.XmlConvert.ToTimeSpan(v.Trim()); return true; }
        catch { span = TimeSpan.Zero; return false; }
    }

    // Linhas longas do .ics continuam na linha de baixo começando com espaço
    static List<string> Unfold(string ics)
    {
        var list = new List<string>();
        foreach (var raw in ics.Replace("\r\n", "\n").Split('\n'))
        {
            if ((raw.StartsWith(' ') || raw.StartsWith('\t')) && list.Count > 0) list[^1] += raw[1..];
            else list.Add(raw.TrimEnd('\r'));
        }
        return list;
    }

    // Dois-pontos que separam o nome do valor (pula os que estão dentro de aspas, ex.: TZID="...")
    static int IndexOfValue(string line)
    {
        bool quote = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quote = !quote;
            else if (line[i] == ':' && !quote) return i;
        }
        return -1;
    }

    static string Unescape(string s) => s.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();
}
