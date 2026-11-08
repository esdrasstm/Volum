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
            string url = "https://geocoding-api.open-meteo.com/v1/search?count=1&format=json&language=" + lang +
                         "&name=" + Uri.EscapeDataString(city);
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            if (!doc.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0)
            {
                LastError = "notFound";
                return null;
            }
            var r = res[0];
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
