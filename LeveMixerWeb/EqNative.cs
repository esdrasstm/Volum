using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Volum;

/// <summary>
/// Ponte com o VolumEqApo.dll (native/). O preamp automático e a prévia usam o mesmo código C++
/// que roda dentro do motor de áudio do Windows.
/// </summary>
static class EqNative
{
    public const int EngineVersion = 3;
    public const int ParamsBytes = 1304;
    public const int RecordBytes = 1816;
    public const int MaxBands = 32;

    public static string DllPath => Path.Combine(AppContext.BaseDirectory, "VolumEqApo.dll");

    public static bool Available { get; }

    static EqNative()
    {
        try
        {
            if (!File.Exists(DllPath) || LoadLibraryW(DllPath) == IntPtr.Zero) return;
            Available = Version() == EngineVersion && ParamsSize() == ParamsBytes && RecordSize() == RecordBytes;
        }
        catch { Available = false; }
    }

    public static double Magnitude(byte[] blob, double sampleRate, double freqHz)
    {
        if (!Available || blob.Length < ParamsBytes) return 0;
        return MagnitudeDb(blob, sampleRate, freqHz);
    }

    public static void PreviewSet(byte[] blob)
    {
        if (!Available || blob.Length < ParamsBytes) return;
        PreviewSetNative(blob);
    }

    public static int PreviewStart(string? deviceId) => Available ? PreviewStartNative(deviceId) : -1;
    public static void PreviewStop() { if (Available) PreviewStopNative(); }
    public static float PreviewPeak() => Available ? PreviewPeakNative() : 0;

    public static string PreviewError()
    {
        if (!Available) return "";
        var p = PreviewErrorNative();
        return p == IntPtr.Zero ? "" : Marshal.PtrToStringUni(p) ?? "";
    }

    /// <summary>Cria o efeito como o Windows cria e processa alguns blocos. null = tudo certo.</summary>
    public static string? Smoke()
    {
        if (!Available) return "dll";
        var buf = new char[256];
        return SmokeNative(buf, buf.Length) == 0 ? null : new string(buf).TrimEnd('\0');
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibraryW(string path);

    const string Dll = "VolumEqApo.dll";

    [DllImport(Dll, EntryPoint = "volum_eq_version", CallingConvention = CallingConvention.Cdecl)]
    static extern int Version();

    [DllImport(Dll, EntryPoint = "volum_eq_params_size", CallingConvention = CallingConvention.Cdecl)]
    static extern int ParamsSize();

    [DllImport(Dll, EntryPoint = "volum_eq_record_size", CallingConvention = CallingConvention.Cdecl)]
    static extern int RecordSize();

    [DllImport(Dll, EntryPoint = "volum_eq_magnitude_db", CallingConvention = CallingConvention.Cdecl)]
    static extern double MagnitudeDb([In] byte[] blob, double sampleRate, double freqHz);

    [DllImport(Dll, EntryPoint = "volum_eq_preview_set", CallingConvention = CallingConvention.Cdecl)]
    static extern void PreviewSetNative([In] byte[] blob);

    [DllImport(Dll, EntryPoint = "volum_eq_preview_start", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    static extern int PreviewStartNative(string? deviceId);

    [DllImport(Dll, EntryPoint = "volum_eq_preview_stop", CallingConvention = CallingConvention.Cdecl)]
    static extern void PreviewStopNative();

    [DllImport(Dll, EntryPoint = "volum_eq_preview_peak", CallingConvention = CallingConvention.Cdecl)]
    static extern float PreviewPeakNative();

    [DllImport(Dll, EntryPoint = "volum_eq_preview_error", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr PreviewErrorNative();

    [DllImport(Dll, EntryPoint = "volum_eq_apo_smoke", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    static extern int SmokeNative([Out] char[] error, int chars);
}
