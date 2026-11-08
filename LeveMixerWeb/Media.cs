using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Volum;

/// <summary>O que está tocando agora (para a ilha). Capa em data: URL, para a interface mostrar direto.
/// Tempo em segundos; <paramref name="UpdatedAt"/> (ms desde 1970) é quando o player informou a posição:
/// a interface anda o relógio sozinha a partir dela, sem o C# ficar mandando a posição a cada segundo.</summary>
sealed record MediaInfo(string Title, string Artist, string Album, string App, bool Playing, bool CanPrev, bool CanNext,
                        string? Cover, double Position, double Duration, long UpdatedAt, bool CanSeek = false);

/// <summary>
/// Música tocando no Windows (Spotify, navegador, player do Windows…) e os botões ⏮ ⏯ ⏭, pelos
/// controles de mídia do próprio Windows (os mesmos do painel de volume do Windows 11).
/// Leve: não fica checando; o Windows avisa quando a música, o player, o play/pause ou a posição mudam.
/// </summary>
sealed class MediaService : IDisposable
{
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _session;
    string? _coverKey, _cover;   // a capa só é lida de novo quando a música muda
    MediaInfo? _last;
    bool _disposed;

    /// <summary>Mudou o que está tocando (null = nada). Chamado fora da thread da interface.</summary>
    public event Action<MediaInfo?>? Changed;

    public static async Task<MediaService> CreateAsync()
    {
        var m = new MediaService();
        try
        {
            m._manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            m._manager.CurrentSessionChanged += (_, _) => m.Attach();
            m.Attach();
        }
        catch { /* Windows sem os controles de mídia: a ilha mostra "nada tocando" */ }
        return m;
    }

    // Passa a acompanhar o player atual (o Windows escolhe o que está em foco/tocando)
    void Attach()
    {
        if (_disposed) return;
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnProps;
            _session.PlaybackInfoChanged -= OnPlayback;
            _session.TimelinePropertiesChanged -= OnTimeline;
        }
        _session = _manager?.GetCurrentSession();
        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnProps;
            _session.PlaybackInfoChanged += OnPlayback;
            _session.TimelinePropertiesChanged += OnTimeline;
        }
        _ = RefreshCoreAsync(readCover: true);
    }

    // Música mudou (nome, artista, capa): lê a capa de novo. O Spotify avisa o nome novo antes de trocar a
    // imagem, então a capa também é conferida de novo um pouco depois (senão ficava a da música anterior).
    void OnProps(GlobalSystemMediaTransportControlsSession s, object e)
    {
        _ = RefreshCoreAsync(readCover: true);
        _ = Task.Delay(1500).ContinueWith(_ => RefreshCoreAsync(readCover: true));
    }

    // Só play/pause ou botões: a capa continua a mesma
    void OnPlayback(GlobalSystemMediaTransportControlsSession s, object e) => _ = RefreshCoreAsync(readCover: false);

    // A posição mudou (pulou um trecho, trocou de música): só atualiza o tempo, sem ler nome e capa de novo
    void OnTimeline(GlobalSystemMediaTransportControlsSession s, object e)
    {
        if (_last == null || _disposed) return;
        try
        {
            var (pos, dur, at) = Timeline(s);
            _last = _last with { Position = pos, Duration = dur, UpdatedAt = at };
            Changed?.Invoke(_last);
        }
        catch { }
    }

    /// <summary>Manda o estado atual de novo (ex.: a interface acabou de carregar).</summary>
    public Task RefreshAsync() => RefreshCoreAsync(readCover: true);

    async Task RefreshCoreAsync(bool readCover)
    {
        var s = _session;
        if (_disposed) return;
        if (s == null) { _last = null; Changed?.Invoke(null); return; }
        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            var playback = s.GetPlaybackInfo();
            if (string.IsNullOrWhiteSpace(props.Title)) { _last = null; Changed?.Invoke(null); return; }

            // Capa: lida de novo sempre que a música avisa mudança (a imagem pode chegar depois do nome).
            // Música nova sem capa ainda: some com a antiga em vez de mostrar a errada.
            var key = props.Title + "\n" + props.Artist + "\n" + props.AlbumTitle;
            if (readCover || key != _coverKey)
            {
                var cover = await ReadCoverAsync(props.Thumbnail);
                if (cover != null) { _cover = cover; _coverKey = key; }
                else if (key != _coverKey) _cover = null;
            }

            var (pos, dur, at) = Timeline(s);
            _last = new MediaInfo(
                props.Title,
                props.Artist ?? "",
                props.AlbumTitle ?? "",
                AppName(s.SourceAppUserModelId),
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                playback.Controls.IsPreviousEnabled,
                playback.Controls.IsNextEnabled,
                _cover, pos, dur, at,
                playback.Controls.IsPlaybackPositionEnabled);   // o player deixa mudar o ponto da música?
            Changed?.Invoke(_last);
        }
        catch { /* o player fechou no meio da leitura: o próximo aviso do Windows corrige */ }
    }

    static (double pos, double dur, long at) Timeline(GlobalSystemMediaTransportControlsSession s)
    {
        var t = s.GetTimelineProperties();
        double dur = Math.Max(0, (t.EndTime - t.StartTime).TotalSeconds);
        double pos = Math.Clamp((t.Position - t.StartTime).TotalSeconds, 0, dur > 0 ? dur : double.MaxValue);
        return (pos, dur, t.LastUpdatedTime.ToUnixTimeMilliseconds());
    }

    static async Task<string?> ReadCoverAsync(IRandomAccessStreamReference? thumb)
    {
        if (thumb == null) return null;
        try
        {
            using var stream = await thumb.OpenReadAsync();
            if (stream.Size > 2_000_000) return null;   // capa gigante: fica sem
            // Copia tudo até o fim: ler "o tamanho de uma vez" às vezes trazia só um pedaço (capa quebrada)
            using var input = stream.AsStreamForRead();
            using var copy = new MemoryStream();
            await input.CopyToAsync(copy);
            if (copy.Length < 100) return null;          // vazio ou quase: tenta de novo no próximo aviso
            var bytes = copy.ToArray();
            return $"data:{ImageType(bytes, stream.ContentType)};base64,{Convert.ToBase64String(bytes)}";
        }
        catch { return null; }
    }

    // Tipo da imagem pelos primeiros bytes (o player às vezes informa errado ou não informa)
    static string ImageType(byte[] b, string? informed)
    {
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8) return "image/jpeg";
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length > 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 && b[8] == 0x57) return "image/webp";
        if (b.Length > 3 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) return "image/gif";
        return string.IsNullOrEmpty(informed) ? "image/png" : informed;
    }

    // "Spotify.exe" → "Spotify"; "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic" → "ZuneMusic"; "Chrome" → "Chrome"
    static string AppName(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        var name = id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(id) : id;
        name = name[(name.LastIndexOf('!') + 1)..];
        name = name[(name.LastIndexOf('.') + 1)..];
        return name.Length > 0 ? char.ToUpper(name[0]) + name[1..] : name;
    }

    public void PlayPause() => _ = _session?.TryTogglePlayPauseAsync();
    public void Next() => _ = _session?.TrySkipNextAsync();
    public void Previous() => _ = _session?.TrySkipPreviousAsync();
    public void Stop() => _ = _session?.TryStopAsync();

    /// <summary>Vai para este ponto da música (segundos desde o começo). Só funciona se o player permitir (CanSeek).</summary>
    public void Seek(double seconds)
    {
        var s = _session;
        if (s == null) return;
        try
        {
            var t = s.GetTimelineProperties();
            _ = s.TryChangePlaybackPositionAsync((t.StartTime + TimeSpan.FromSeconds(Math.Max(0, seconds))).Ticks);
        }
        catch { /* o player fechou */ }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnProps;
            _session.PlaybackInfoChanged -= OnPlayback;
            _session.TimelinePropertiesChanged -= OnTimeline;
        }
        _session = null;
        _manager = null;
    }
}
