using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Timer = System.Windows.Forms.Timer;

namespace Volum;

/// <summary>Uma hora da previsão (hora local da cidade).</summary>
sealed record WeatherHour(string Hour, double Temp, int Code, bool Day);

/// <summary>O clima agora + as próximas horas.</summary>
sealed record WeatherNow(string Place, double Temp, int Code, bool Day, double Max, double Min, List<WeatherHour> Hours);

/// <summary>
/// Clima na ilha, pelo Open-Meteo (grátis, sem conta e sem chave). A pessoa digita a cidade nas configurações;
/// o Volum procura a cidade uma vez (para saber a latitude e a longitude) e baixa a previsão a cada 30 min,
/// só com a ilha ligada e uma cidade salva. Não usa a localização do Windows. A cidade fica só no settings.json.
/// </summary>
static class Weather
{
    public static event Action<WeatherNow?>? Changed;
    public static WeatherNow? Current { get; private set; }
    public static string LastError { get; private set; } = "";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static readonly Timer FetchTimer = new() { Interval = 30 * 60 * 1000 };
    static bool _on;

    static Weather() => FetchTimer.Tick += async (_, _) => await FetchAsync();

    // Países que usam Fahrenheit no dia a dia (EUA e territórios, Bahamas, Belize, Ilhas Cayman, Palau, Libéria, Micronésia, Ilhas Marshall)
    static readonly string[] FahrenheitRegions = { "US", "PR", "GU", "VI", "AS", "MP", "UM", "BS", "BZ", "KY", "PW", "LR", "FM", "MH" };

    /// <summary>Unidade usada agora: "c" ou "f" (no automático, pela região do Windows).</summary>
    public static string Unit
    {
        get
        {
            var u = AppSettings.Current.WeatherUnit;
            if (u is "c" or "f") return u;
            try { return FahrenheitRegions.Contains(System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName) ? "f" : "c"; }
            catch { return "c"; }
        }
    }

    static bool HasPlace => !string.IsNullOrWhiteSpace(AppSettings.Current.WeatherCity);

    /// <summary>Liga/desliga conforme a ilha e a cidade salva.</summary>
    public static void Apply(bool islandOn)
    {
        bool on = islandOn && HasPlace;
        if (on == _on) return;
        _on = on;
        FetchTimer.Stop();
        if (!on) { Current = null; Changed?.Invoke(null); return; }
        FetchTimer.Start();
        _ = FetchAsync();
    }

    /// <summary>
    /// Salvou outra cidade: procura (nome oficial, latitude e longitude) e baixa a previsão.
    /// Devolve o nome encontrado ("" = cidade apagada, null = erro: LastError = "notFound" ou "fetch").
    /// </summary>
    public static async Task<string?> SetCityAsync(string city, bool islandOn)
    {
        var s = AppSettings.Current;
        city = city.Trim();
        if (city.Length == 0)
        {
            s.WeatherCity = ""; s.WeatherPlace = ""; s.Save();
            Apply(islandOn);
            return "";
        }
        try
        {
            string lang = Strings.Normalize(s.Language);
            // "Ubá MG", "Ubá, MG" ou "Ubá, Minas Gerais": procura só o nome e usa o resto (estado/país) para escolher
            var (query, hint) = SplitPlace(city);
            string url = "https://geocoding-api.open-meteo.com/v1/search?count=100&format=json&language=" + lang +
                         "&name=" + Uri.EscapeDataString(query);
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            if (!doc.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0)
            {
                LastError = "notFound";
                return null;
            }
            var r = BestMatch(res, query, hint, out bool hintOk);
            // Digitou o estado/país e nenhum resultado é de lá: melhor dizer que não achou do que escolher a cidade errada
            if (hint.Length > 0 && !hintOk)
            {
                LastError = "notFound";
                return null;
            }
            string name = r.GetProperty("name").GetString() ?? city;
            string region = r.TryGetProperty("admin1", out var a) ? a.GetString() ?? "" : "";
            s.WeatherCity = city;
            s.WeatherPlace = name;
            s.WeatherLat = r.GetProperty("latitude").GetDouble();
            s.WeatherLon = r.GetProperty("longitude").GetDouble();
            s.Save();
            _on = false;            // força buscar de novo com a cidade nova
            Apply(islandOn);
            if (islandOn) await FetchAsync();
            return region.Length > 0 && region != name ? $"{name}, {region}" : name;
        }
        catch
        {
            LastError = "fetch";
            return null;
        }
    }

    // ---------- escolher a cidade certa ----------
    // O Open-Meteo devolve as cidades que COMEÇAM com o nome, das maiores para as menores: "Ubá" trazia Uberaba
    // primeiro. Agora ganha a que tem o nome exato (sem ligar para acento e maiúscula), do estado/país digitado.

    static readonly Dictionary<string, string> BrazilStates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AC"] = "Acre", ["AL"] = "Alagoas", ["AP"] = "Amapá", ["AM"] = "Amazonas", ["BA"] = "Bahia", ["CE"] = "Ceará",
        ["DF"] = "Distrito Federal", ["ES"] = "Espírito Santo", ["GO"] = "Goiás", ["MA"] = "Maranhão", ["MT"] = "Mato Grosso",
        ["MS"] = "Mato Grosso do Sul", ["MG"] = "Minas Gerais", ["PA"] = "Pará", ["PB"] = "Paraíba", ["PR"] = "Paraná",
        ["PE"] = "Pernambuco", ["PI"] = "Piauí", ["RJ"] = "Rio de Janeiro", ["RN"] = "Rio Grande do Norte",
        ["RS"] = "Rio Grande do Sul", ["RO"] = "Rondônia", ["RR"] = "Roraima", ["SC"] = "Santa Catarina", ["SP"] = "São Paulo",
        ["SE"] = "Sergipe", ["TO"] = "Tocantins",
    };

    /// <summary>Separa "cidade" e "estado/país": pela vírgula ou pela sigla do estado no fim ("Ubá MG").</summary>
    static (string query, string hint) SplitPlace(string text)
    {
        int comma = text.IndexOf(',');
        if (comma > 0) return (text[..comma].Trim(), text[(comma + 1)..].Trim());
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1 && BrazilStates.ContainsKey(parts[^1])) return (string.Join(' ', parts[..^1]), parts[^1]);
        return (text, "");
    }

    // Sem acento, minúsculo e sem hífen/apóstrofo (o IBGE escreve "Ji-Paraná" e "Olhos-d'Água"; o Open-Meteo, "Ji Paraná" e "Olhos d'Água")
    static readonly System.Text.RegularExpressions.Regex Punct = new(@"[\-\u2010-\u2015'\u2018\u2019\u00B4\u0060]+");
    static readonly System.Text.RegularExpressions.Regex Spaces = new(@"\s+");

    static string Plain(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = Spaces.Replace(Punct.Replace(s, " "), " ");
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Trim();
    }

    /// <summary>Pontua cada resultado: nome exato vale mais; estado/país igual ao digitado também; empate = a primeira (maior).</summary>
    static JsonElement BestMatch(JsonElement results, string query, string hint, out bool hintOk)
    {
        hintOk = false;
        string myCountry = "";
        try { myCountry = Plain(RegionInfo.CurrentRegion.TwoLetterISORegionName); } catch { }
        string q = Plain(query), h = Plain(BrazilStates.TryGetValue(hint, out var st) ? st : hint);
        JsonElement best = results[0];
        int bestScore = int.MinValue, i = 0;
        foreach (var r in results.EnumerateArray())
        {
            string name = Plain(r.TryGetProperty("name", out var n) ? n.GetString() : "");
            string admin = Plain(r.TryGetProperty("admin1", out var a) ? a.GetString() : "");
            string country = Plain(r.TryGetProperty("country", out var c) ? c.GetString() : "");
            string code = Plain(r.TryGetProperty("country_code", out var cc) ? cc.GetString() : "");
            int score = -i;                                    // ordem do Open-Meteo como desempate
            if (name == q) score += 10_000;            // pesos bem maiores que a posição (até 100 resultados)
            if (h.Length > 0 && (admin == h || country == h || code == h || admin.StartsWith(h) || country.StartsWith(h))) score += 1_000;
            if (code == myCountry) score += 500;           // mesmo país do Windows da pessoa ("Santa Cruz" no Brasil = a brasileira)
            bool inHint = h.Length > 0 && (admin == h || country == h || code == h || admin.StartsWith(h) || country.StartsWith(h));
            if (score > bestScore) { bestScore = score; best = r; hintOk = inHint; }
            i++;
        }
        return best;
    }

    /// <summary>Baixa a previsão agora (só com a ilha ligada e uma cidade salva).</summary>
    public static async Task FetchAsync()
    {
        if (!_on) return;
        var s = AppSettings.Current;
        try
        {
            string lat = s.WeatherLat.ToString(CultureInfo.InvariantCulture), lon = s.WeatherLon.ToString(CultureInfo.InvariantCulture);
            string url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
                         "&current=temperature_2m,weather_code,is_day" +
                         "&hourly=temperature_2m,weather_code,is_day" +
                         "&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=2";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var root = doc.RootElement;
            var cur = root.GetProperty("current");
            var daily = root.GetProperty("daily");
            var hourly = root.GetProperty("hourly");

            // Próximas 5 horas cheias depois da hora atual (o horário vem no fuso da cidade, "2026-11-08T13:00")
            string curTime = cur.GetProperty("time").GetString() ?? "";
            string nowHour = curTime.Length >= 13 ? curTime[..13] : curTime;   // "2026-11-08T13"
            var times = hourly.GetProperty("time").EnumerateArray().Select(t => t.GetString() ?? "").ToList();
            var temps = hourly.GetProperty("temperature_2m").EnumerateArray().Select(t => t.GetDouble()).ToList();
            var codes = hourly.GetProperty("weather_code").EnumerateArray().Select(t => t.GetInt32()).ToList();
            var days = hourly.GetProperty("is_day").EnumerateArray().Select(t => t.GetInt32() == 1).ToList();
            int i = times.FindIndex(t => t.StartsWith(nowHour, StringComparison.Ordinal));
            var hours = new List<WeatherHour>();
            for (int k = i + 1; i >= 0 && k < times.Count && hours.Count < 5; k++)
                hours.Add(new WeatherHour(times[k].Length >= 16 ? times[k][11..13] : "", temps[k], codes[k], days[k]));

            Current = new WeatherNow(
                s.WeatherPlace,
                cur.GetProperty("temperature_2m").GetDouble(),
                cur.GetProperty("weather_code").GetInt32(),
                cur.GetProperty("is_day").GetInt32() == 1,
                daily.GetProperty("temperature_2m_max")[0].GetDouble(),
                daily.GetProperty("temperature_2m_min")[0].GetDouble(),
                hours);
            Changed?.Invoke(Current);
        }
        catch { /* sem internet agora: tenta de novo daqui a 30 min (a ilha continua com a última previsão) */ }
    }
}
