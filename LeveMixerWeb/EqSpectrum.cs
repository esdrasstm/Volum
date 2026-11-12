using System;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Volum;

/// <summary>
/// Analisador de espectro da janela do equalizador: escuta a saída escolhida em modo loopback
/// (o mesmo jeito que gravadores de tela fazem; não muda nada no som nem no jogo) e entrega
/// ~30 vezes por segundo o nível em faixas logarítmicas de 20 Hz a 20 kHz.
/// </summary>
sealed class EqSpectrum : IDisposable
{
    public const int Bins = 160;
    const int Size = 8192;            // 5,9 Hz de resolução a 48 kHz: graves bem definidos
    const double MinDb = -120;

    readonly Action<float[]> _frame;
    readonly float[] _ring = new float[Size];
    readonly double[] _re = new double[Size], _im = new double[Size], _window = new double[Size];
    readonly float[] _out = new float[Bins];
    readonly Stopwatch _clock = Stopwatch.StartNew();
    WasapiLoopbackCapture? _capture;
    int _pos;
    long _last;
    int _rate = 48000;

    public EqSpectrum(Action<float[]> frame)
    {
        _frame = frame;
        for (int i = 0; i < Size; i++) _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Size - 1));   // Hann
    }

    public bool Running => _capture != null;

    public void Start(string? deviceId)
    {
        Stop();
        try
        {
            using var en = new MMDeviceEnumerator();
            var device = string.IsNullOrEmpty(deviceId) || deviceId == "default"
                ? en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console)
                : en.GetDevice(deviceId);
            var capture = new WasapiLoopbackCapture(device);
            if (capture.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat && capture.WaveFormat.Encoding != WaveFormatEncoding.Extensible)
            {
                capture.Dispose();
                return;
            }
            _rate = capture.WaveFormat.SampleRate;
            capture.DataAvailable += OnData;
            capture.StartRecording();
            _capture = capture;
        }
        catch (Exception ex) { Debug.WriteLine(ex); Stop(); }
    }

    public void Stop()
    {
        var c = _capture;
        _capture = null;
        if (c == null) return;
        try { c.DataAvailable -= OnData; c.StopRecording(); } catch { }
        try { c.Dispose(); } catch { }
        Array.Clear(_ring);
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        var c = _capture;
        if (c == null || e.BytesRecorded <= 0) return;
        int ch = Math.Max(1, c.WaveFormat.Channels);
        int frames = e.BytesRecorded / (4 * ch);
        var span = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, frames * 4 * ch));
        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int k = 0; k < ch; k++) sum += span[f * ch + k];
            _ring[_pos] = sum / ch;
            _pos = (_pos + 1) & (Size - 1);
        }
        long now = _clock.ElapsedMilliseconds;
        if (now - _last < 33) return;
        _last = now;
        Analyze();
        _frame((float[])_out.Clone());
    }

    void Analyze()
    {
        for (int i = 0; i < Size; i++)
        {
            _re[i] = _ring[(_pos + i) & (Size - 1)] * _window[i];
            _im[i] = 0;
        }
        Fft(_re, _im);
        // Amplitude de um seno em dBFS: |X| * 2 / (N * 0,5), o 0,5 é o ganho da janela de Hann.
        double scale = 4.0 / Size, hz = (double)_rate / Size;
        int half = Size / 2;
        double Db(int k)
        {
            double m = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) * scale;
            return m > 1e-9 ? 20 * Math.Log10(m) : MinDb;
        }
        for (int b = 0; b < Bins; b++)
        {
            double f0 = 20 * Math.Pow(1000, (double)b / Bins), f1 = 20 * Math.Pow(1000, (double)(b + 1) / Bins);
            int k0 = (int)Math.Ceiling(f0 / hz), k1 = (int)Math.Floor(f1 / hz);
            double db;
            if (k1 >= k0)
            {
                db = MinDb;
                for (int k = Math.Max(1, k0); k <= Math.Min(half - 1, k1); k++) db = Math.Max(db, Db(k));
            }
            else
            {
                // Faixa mais estreita que a resolução (graves): interpola entre as duas raias vizinhas.
                double pos = Math.Sqrt(f0 * f1) / hz;
                int k = Math.Clamp((int)pos, 1, half - 2);
                double t = pos - k;
                db = Db(k) * (1 - t) + Db(k + 1) * t;
            }
            _out[b] = (float)Math.Max(MinDb, db);
        }
    }

    static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    double nr = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = nr;
                }
            }
        }
    }

    public void Dispose() => Stop();
}
