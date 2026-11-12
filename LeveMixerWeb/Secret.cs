using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Volum;

/// <summary>Guarda textos secretos (ex.: o endereço da agenda) protegidos pelo Windows (DPAPI, escopo do usuário):
/// o arquivo de configurações fica com o texto embaralhado, e só este usuário, neste PC, consegue abrir.</summary>
static class Secret
{
    public static string Protect(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        try { return Convert.ToBase64String(Run(Encoding.UTF8.GetBytes(text), protect: true)); }
        catch { return ""; }
    }

    public static string Unprotect(string? data)
    {
        if (string.IsNullOrEmpty(data)) return "";
        try { return Encoding.UTF8.GetString(Run(Convert.FromBase64String(data), protect: false)); }
        catch { return ""; }   // veio de outro PC/usuário ou estragou: fica sem agenda
    }

    static byte[] Run(byte[] input, bool protect)
    {
        var inBlob = new Blob { Size = input.Length, Data = Marshal.AllocHGlobal(input.Length) };
        var outBlob = new Blob();
        try
        {
            Marshal.Copy(input, 0, inBlob.Data, input.Length);
            bool ok = protect
                ? CryptProtectData(ref inBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x1 /* sem janela */, ref outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x1, ref outBlob);
            if (!ok) throw new InvalidOperationException("DPAPI");
            var result = new byte[outBlob.Size];
            Marshal.Copy(outBlob.Data, result, 0, outBlob.Size);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(inBlob.Data);
            if (outBlob.Data != IntPtr.Zero) LocalFree(outBlob.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Blob { public int Size; public IntPtr Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref Blob dataIn, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob dataOut);
    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptUnprotectData(ref Blob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob dataOut);
    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr mem);
}
