using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Volum;

/// <summary>
/// Liga o equalizador como efeito de endpoint (EFX) de um dispositivo de saída, do mesmo jeito que o
/// Windows liga os efeitos dos fabricantes. Roda num processo elevado (Volum.exe --eq-install "id").
///
/// O que ele mexe, e o que ele guarda para desfazer (Program Files\Volum\Eq\fx-backup.json):
///  - copia o VolumEqApo.dll para Program Files\Volum\Eq e registra o efeito (HKLM);
///  - no FxProperties do dispositivo: o EFX passa a ser o Volum (o EFX anterior, se havia, roda dentro dele)
///    ou, em drivers com lista de efeitos (CompositeFX), o Volum entra no fim da lista;
///  - liga as "melhorias de áudio" do dispositivo, se estavam desligadas (sem isso o Windows não carrega efeito nenhum);
///  - DisableProtectedAudioDG = 1, como o Equalizer APO: o Windows aceita efeitos sem assinatura de fabricante.
/// Na remoção tudo volta ao valor anterior, valor por valor.
/// </summary>
static class EqInstaller
{
    public const string Clsid = "{7E4C1A2B-9F30-4D55-A1C8-6B2E0F5D3A17}";
    const string FxKeys = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d}";
    const string ModeKeys = "{d3993a3f-99c2-4402-b5ec-a92a0367664b}";
    const string OurProps = "{c2a91f40-5e77-4b18-9d3c-81f6a0b24e55}";
    const string SysFxOff = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5";
    const string KsAnyNode = "{DFF21BE1-F70F-11D0-B917-00A0C9223196}";
    const string NullGuid = "{00000000-0000-0000-0000-000000000000}";
    const string RenderRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";
    const string AudioKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio";

    static string Efx => FxKeys + ",7";
    static string CompositeEfx => FxKeys + ",15";
    static string Association => FxKeys + ",0";
    static string EfxModes => ModeKeys + ",7";
    static string PropEndpoint => OurProps + ",1";
    static string PropChild => OurProps + ",2";

    // Modos de áudio em que o efeito vale (o modo RAW fica de fora de propósito: é o "sem processamento").
    static readonly string[] Modes =
    {
        "{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}",   // padrão
        "{4780004E-7133-41D8-8C74-660DADD2C0EE}",   // mídia
        "{B26FEB0D-EC94-477C-9494-D1AB8E753F6E}",   // filme
        "{98951333-B9CD-48B1-A0A3-FF40682D73F7}",   // comunicação
        "{FC1CFC9B-B9D6-4CFA-B5E0-4BB2166878B2}",   // fala
        "{9CF2A70B-F377-403B-BD6B-360863E0355C}",   // notificação
    };

    public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Volum", "Eq");
    public static string InstalledDll => Path.Combine(InstallDir, "VolumEqApo.dll");
    static string BackupPath => Path.Combine(InstallDir, "fx-backup.json");
    static string LogPath => Path.Combine(InstallDir, "install.log");

    // ================================================================ consultas (sem elevação)

    /// <summary>O Volum está como efeito deste dispositivo?</summary>
    public static bool IsInstalled(string deviceId)
    {
        try
        {
            using var fx = Registry.LocalMachine.OpenSubKey(FxPath(deviceId));
            return fx != null && HasOurs(fx);
        }
        catch { return false; }
    }

    public static bool AnyInstalled()
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(RenderRoot);
            if (root == null) return false;
            foreach (var name in root.GetSubKeyNames())
            {
                using var fx = root.OpenSubKey(name + "\\FxProperties");
                if (fx != null && HasOurs(fx)) return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>As "melhorias de áudio" do dispositivo estão desligadas? (aí o Windows não carrega efeito nenhum)</summary>
    public static bool EnhancementsOff(string deviceId)
    {
        try
        {
            using var props = Registry.LocalMachine.OpenSubKey(EndpointPath(deviceId) + "\\Properties");
            return ReadValue(props, SysFxOff) is int v && v != 0;
        }
        catch { return false; }
    }

    /// <summary>O DLL ativado no sistema é diferente do que veio com esta versão do Volum?</summary>
    public static bool NeedsUpdate()
    {
        try
        {
            if (!File.Exists(InstalledDll) || !File.Exists(EqNative.DllPath)) return false;
            return CachedHash(InstalledDll) != CachedHash(EqNative.DllPath);
        }
        catch { return false; }
    }

    public static bool ProtectedAudioOff()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(AudioKey);
            return key?.GetValue("DisableProtectedAudioDG") is int v && v == 1;
        }
        catch { return false; }
    }

    // ================================================================ ações (processo elevado)

    public sealed class Result
    {
        public bool Ok { get; set; }
        public string Code { get; set; } = "";
        public string Detail { get; set; } = "";
        public List<string> Notes { get; set; } = new();
    }

    /// <summary>Entrada do processo elevado. args: --eq-install|--eq-remove|--eq-remove-all "id" --eq-result "arquivo".</summary>
    public static int RunCommand(string action, string deviceId, string? resultPath)
    {
        var result = new Result();
        try
        {
            RequireAdmin();
            Directory.CreateDirectory(InstallDir);
            Log("---- " + action + " " + deviceId + " (Volum " + Updater.Version + ")");
            switch (action)
            {
                case "--eq-install": Install(deviceId, result); break;
                case "--eq-remove": Remove(deviceId, result); break;
                case "--eq-remove-all": RemoveAll(result); break;
                default: throw new ArgumentException("ação desconhecida");
            }
            result.Ok = true;
            if (result.Code.Length == 0) result.Code = "ok";
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            result.Ok = false;
            if (result.Code.Length == 0) result.Code = "error";
            result.Detail = ex.Message;
        }
        Log("resultado: " + result.Code + " " + result.Detail);
        if (resultPath != null) WriteResult(resultPath, result);
        return result.Ok ? 0 : 1;
    }

    static void Install(string deviceId, Result result)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("dispositivo ausente");
        using (var endpoint = OpenEndpoint(deviceId))
        {
            if (endpoint == null)
            {
                result.Code = "no-device";
                throw new InvalidOperationException("O Windows não tem esse dispositivo de saída.");
            }
        }
        // Antes de mexer em qualquer coisa: o dispositivo toca som agora? Se não (outro app em modo exclusivo,
        // fone desconectando), para aqui. Assim, uma falha depois da ativação é culpa do efeito, e só aí se desfaz.
        if (PlaySilence(deviceId, TimeSpan.FromMilliseconds(400), null) is string busy)
        {
            result.Code = "device-busy";
            throw new InvalidOperationException("O dispositivo não está tocando som agora (" + busy + "). Nada foi alterado.");
        }

        var backup = LoadBackup();
        bool dllChanged;
        try
        {
            PrepareFolders();
            dllChanged = CopyDll();
            Register();
            PatchDevice(deviceId, backup.DeviceFor(deviceId), result);
            AllowUnsigned(backup);
            SaveBackup(backup);
        }
        catch
        {
            // Tudo ou nada: o que já tinha sido mexido volta ao original antes de reportar o erro.
            Log("falhou no meio; desfazendo");
            Undo(deviceId, backup, restart: false);
            throw;
        }

        bool ok;
        try
        {
            RestartAudio(result);
            if (dllChanged) Log("motor copiado: " + Hash(InstalledDll));
            ok = Verify(deviceId, result);
        }
        catch
        {
            // O áudio não voltou com o efeito ativo: tira o efeito e tenta subir o áudio de novo.
            Log("o áudio não voltou depois de ativar; revertendo");
            Undo(deviceId, backup, restart: true);
            throw;
        }
        if (!ok)
        {
            // O dispositivo tocava antes e parou de tocar com o efeito: desfaz tudo na hora.
            Log("o dispositivo ficou sem som com o efeito; revertendo");
            Undo(deviceId, backup, restart: true);
            result.Code = "rolled-back";
            throw new InvalidOperationException("O Windows não tocou som com o efeito ativo; tudo foi desfeito e o áudio voltou ao normal.");
        }
        result.Code = "installed";
    }

    /// <summary>Desfaz a ativação de um dispositivo (e o registro do efeito, se ele não ficou em nenhum outro).</summary>
    static void Undo(string deviceId, FxBackup backup, bool restart)
    {
        try { RestoreDevice(deviceId, backup); } catch (Exception ex) { Log("desfazer dispositivo: " + ex.Message); }
        if (!AnyInstalled())
        {
            try { Unregister(backup); } catch (Exception ex) { Log("desfazer registro: " + ex.Message); }
        }
        try { SaveBackup(backup); } catch (Exception ex) { Log("desfazer backup: " + ex.Message); }
        if (restart)
        {
            try { RestartAudio(new Result()); } catch (Exception ex) { Log("desfazer: o áudio não reiniciou: " + ex.Message); }
        }
        if (!AnyInstalled())
        {
            try { DeleteDll(); } catch { }
        }
    }

    static void Remove(string deviceId, Result result)
    {
        var backup = LoadBackup();
        RestoreDevice(deviceId, backup);
        SaveBackup(backup);
        if (!AnyInstalled()) Unregister(backup);
        SaveBackup(backup);
        RestartAudio(result);
        if (!AnyInstalled()) DeleteDll();
        result.Code = "removed";
    }

    static void RemoveAll(Result result)
    {
        var backup = LoadBackup();
        foreach (var id in backup.Devices.Keys.ToList()) RestoreDevice(id, backup);
        // Um dispositivo que sumiu do backup mas ainda aponta para o Volum também sai.
        using (var root = Registry.LocalMachine.OpenSubKey(RenderRoot))
        {
            foreach (var name in root?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var fx = OpenValues(RenderRoot + "\\" + name + "\\FxProperties");
                if (fx != null) StripOurs(fx);
            }
        }
        Unregister(backup);
        SaveBackup(backup);
        RestartAudio(result);
        DeleteDll();
        result.Code = "removed";
    }

    // ---------------------------------------------------------------- dispositivo

    static void PatchDevice(string deviceId, DeviceBackup device, Result result)
    {
        using var fx = OpenOrCreateFx(deviceId, device);

        var composite = ReadMulti(fx, CompositeEfx);
        if (composite != null && composite.Length > 0)
        {
            // Driver com lista de efeitos: o Windows encadeia sozinho; o Volum entra por último (mais perto da saída).
            if (!composite.Any(IsOurs))
            {
                Remember(fx, device, CompositeEfx);
                fx.SetValue(Canonical(fx, CompositeEfx), composite.Where(c => !IsOurs(c)).Append(Clsid).ToArray(), RegistryValueKind.MultiString);
            }
            result.Notes.Add("composite");
        }
        else
        {
            var previous = ReadString(fx, Efx);
            if (previous != null && !IsOurs(previous) && !previous.Equals(NullGuid, StringComparison.OrdinalIgnoreCase))
            {
                // Já havia um EFX (Dolby, Nahimic, efeito do fabricante...): ele continua, rodando antes do Volum.
                SetString(fx, device, PropChild, previous);
                result.Notes.Add("chained");
            }
            SetString(fx, device, Efx, Clsid);
        }

        SetString(fx, device, PropEndpoint, deviceId);
        var assoc = ReadString(fx, Association);
        if (assoc == null || assoc.Equals(NullGuid, StringComparison.OrdinalIgnoreCase)) SetString(fx, device, Association, KsAnyNode);
        var modes = ReadMulti(fx, EfxModes) ?? Array.Empty<string>();
        var merged = modes.Concat(Modes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (merged.Length != modes.Length)
        {
            Remember(fx, device, EfxModes);
            fx.SetValue(Canonical(fx, EfxModes), merged, RegistryValueKind.MultiString);
        }

        // Melhorias de áudio desligadas = o Windows pula todos os efeitos do dispositivo.
        using var props = OpenValues(EndpointPath(deviceId) + "\\Properties");
        if (props != null && ReadValue(props, SysFxOff) is int off && off != 0)
        {
            Remember(props, device.Properties, SysFxOff);
            props.SetValue(Canonical(props, SysFxOff), 0, RegistryValueKind.DWord);
            result.Notes.Add("enhancements");
        }
    }

    static void RestoreDevice(string deviceId, FxBackup backup)
    {
        using var fx = OpenValues(FxPath(deviceId));
        if (backup.Devices.TryGetValue(deviceId, out var device))
        {
            if (fx != null) Restore(fx, device.Values);
            using var props = OpenValues(EndpointPath(deviceId) + "\\Properties");
            if (props != null) Restore(props, device.Properties.Values);
            backup.Devices.Remove(deviceId);
        }
        if (fx != null) StripOurs(fx);
    }

    // Todas as posições em que o Windows aceita um efeito (FX e listas CompositeFX, inclusive offload e detector de voz).
    static readonly int[] SingleSlots = { 1, 2, 5, 6, 7, 8, 9, 10, 11, 12 };
    static readonly int[] CompositeSlots = { 13, 14, 15, 16, 17, 18, 19, 20 };

    /// <summary>
    /// Tira o Volum de qualquer posição de efeito da chave, mesmo sem backup (backup perdido, versão antiga
    /// que usou outra posição). Um efeito apontando para um DLL removido deixa o dispositivo mudo; por isso
    /// nenhuma posição pode sobrar com o Volum.
    /// </summary>
    static void StripOurs(RegistryKey fx)
    {
        var child = ReadString(fx, PropChild);
        foreach (int pid in SingleSlots)
        {
            var name = FxKeys + "," + pid;
            if (!IsOurs(ReadString(fx, name))) continue;
            if (pid == 7 && child != null && !IsOurs(child)) fx.SetValue(Canonical(fx, name), child, RegistryValueKind.String);
            else TryDelete(fx, name);
            Log("limpeza: Volum tirado de " + name);
        }
        foreach (int pid in CompositeSlots)
        {
            var name = FxKeys + "," + pid;
            var list = ReadMulti(fx, name);
            if (list == null || !list.Any(IsOurs)) continue;
            fx.SetValue(Canonical(fx, name), list.Where(c => !IsOurs(c)).ToArray(), RegistryValueKind.MultiString);
            Log("limpeza: Volum tirado da lista " + name);
        }
        TryDelete(fx, PropEndpoint);
        TryDelete(fx, PropChild);
    }

    static bool HasOurs(RegistryKey fx) =>
        SingleSlots.Any(pid => IsOurs(ReadString(fx, FxKeys + "," + pid)))
        || CompositeSlots.Any(pid => ReadMulti(fx, FxKeys + "," + pid)?.Any(IsOurs) == true);

    static RegistryKey OpenOrCreateFx(string deviceId, DeviceBackup device)
    {
        var path = FxPath(deviceId);
        var fx = OpenValues(path);
        if (fx != null) return fx;
        // Dispositivo sem efeitos (comum em USB e HDMI): a chave não existe e o Windows não deixa nem o
        // administrador criá-la. Com o privilégio de restauração dá (é o que instaladores de driver fazem).
        Log("criando FxProperties em " + path);
        EnablePrivilege("SeRestorePrivilege");
        EnablePrivilege("SeBackupPrivilege");
        int rc = RegCreateKeyExW(HKEY_LOCAL_MACHINE, path, 0, null, REG_OPTION_BACKUP_RESTORE, KEY_ALL_ACCESS, IntPtr.Zero, out var handle, out _);
        if (rc != 0) throw new Win32Exception(rc, "Não deu para criar a chave de efeitos do dispositivo.");
        device.CreatedFx = true;
        return RegistryKey.FromHandle(handle);
    }

    // ---------------------------------------------------------------- arquivos e registro do COM

    static void PrepareFolders()
    {
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var localService = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        // ProgramData\Volum: o Volum (sem elevação) grava o eq.bin; o motor de áudio (LOCAL SERVICE) lê.
        var data = new DirectoryInfo(EqConfig.DataDir);
        data.Create();
        var sec = data.GetAccessControl();
        sec.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.Modify, inherit, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(localService, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        data.SetAccessControl(sec);
        if (File.Exists(EqConfig.BinPath))
        {
            var bin = new FileInfo(EqConfig.BinPath);
            var fs = bin.GetAccessControl();
            fs.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.Modify, AccessControlType.Allow));
            fs.AddAccessRule(new FileSystemAccessRule(localService, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            bin.SetAccessControl(fs);
        }

        // ProgramData\Volum\eq-live: só o motor de áudio escreve (sinal de vida e registro); usuários só leem.
        // Sem herança, para ninguém plantar um link ali e desviar a escrita do serviço.
        var live = new DirectoryInfo(EqConfig.LiveDir);
        live.Create();
        var ls = new DirectorySecurity();
        ls.SetAccessRuleProtection(true, false);
        ls.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        ls.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        ls.AddAccessRule(new FileSystemAccessRule(localService, FileSystemRights.Modify, inherit, PropagationFlags.None, AccessControlType.Allow));
        ls.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        live.SetAccessControl(ls);
    }

    /// <summary>Copia o motor para Program Files. Se o Windows está com o antigo carregado, troca sem desligar o som.</summary>
    static bool CopyDll()
    {
        var source = EqNative.DllPath;
        if (!File.Exists(source)) throw new FileNotFoundException("O VolumEqApo.dll não está ao lado do Volum.exe.", source);
        if (EqNative.Smoke() is string smoke) throw new InvalidOperationException("O motor do equalizador falhou no teste: " + smoke);
        Directory.CreateDirectory(InstallDir);
        foreach (var old in Directory.GetFiles(InstallDir, "*.old")) { try { File.Delete(old); } catch { } }
        if (File.Exists(InstalledDll) && SameFile(InstalledDll, source)) return false;

        var staged = InstalledDll + ".new";
        File.Copy(source, staged, true);
        if (File.Exists(InstalledDll))
        {
            try { File.Delete(InstalledDll); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Em uso pelo audiodg: renomear um DLL carregado é permitido; o antigo some no próximo boot.
                var aside = InstalledDll + "." + DateTime.UtcNow.Ticks + ".old";
                File.Move(InstalledDll, aside);
                MoveFileExW(aside, null, MOVEFILE_DELAY_UNTIL_REBOOT);
            }
        }
        File.Move(staged, InstalledDll);
        return true;
    }

    static void Register()
    {
        int code = Run("regsvr32.exe", "/s \"" + InstalledDll + "\"");
        if (code != 0) throw new InvalidOperationException("O Windows não registrou o efeito (regsvr32 " + code + ").");
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\AudioEngine\AudioProcessingObjects\" + Clsid);
        if (key == null) throw new InvalidOperationException("O registro do efeito não apareceu no Windows.");
    }

    static void Unregister(FxBackup backup)
    {
        if (File.Exists(InstalledDll)) Run("regsvr32.exe", "/s /u \"" + InstalledDll + "\"");
        try { Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Classes\AudioEngine\AudioProcessingObjects\" + Clsid, false); } catch { }
        try { Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Classes\CLSID\" + Clsid, false); } catch { }
        RestoreProtected(backup);
    }

    static void DeleteDll()
    {
        foreach (var file in new[] { InstalledDll }.Concat(Directory.Exists(InstallDir) ? Directory.GetFiles(InstallDir, "*.old") : Array.Empty<string>()))
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { MoveFileExW(file, null, MOVEFILE_DELAY_UNTIL_REBOOT); }
        }
    }

    static void AllowUnsigned(FxBackup backup)
    {
        using var key = Registry.LocalMachine.CreateSubKey(AudioKey, true);
        var current = key.GetValue("DisableProtectedAudioDG");
        if (current is int on && on == 1) return;
        if (!backup.WeSetProtected)
        {
            backup.WeSetProtected = true;
            backup.ProtectedAudioPrevious = current is int n ? n : null;
        }
        key.SetValue("DisableProtectedAudioDG", 1, RegistryValueKind.DWord);
    }

    static void RestoreProtected(FxBackup backup)
    {
        if (!backup.WeSetProtected) return;
        using var key = Registry.LocalMachine.OpenSubKey(AudioKey, true);
        if (key != null && key.GetValue("DisableProtectedAudioDG") is int n && n == 1)
        {
            if (backup.ProtectedAudioPrevious is int prev) key.SetValue("DisableProtectedAudioDG", prev, RegistryValueKind.DWord);
            else key.DeleteValue("DisableProtectedAudioDG", false);
        }
        backup.WeSetProtected = false;
        backup.ProtectedAudioPrevious = null;
    }

    /// <summary>O Windows só relê os efeitos do dispositivo quando o serviço de áudio reinicia (o som some por 2–5 s).</summary>
    static void RestartAudio(Result result)
    {
        Run("net.exe", "stop AudioEndpointBuilder /y", 30000);
        Run("net.exe", "stop Audiosrv /y", 30000);
        Run("net.exe", "start AudioEndpointBuilder", 30000);
        Run("net.exe", "start Audiosrv", 30000);
        for (int i = 0; i < 20 && !ServiceRunning("Audiosrv"); i++) Thread.Sleep(250);
        if (!ServiceRunning("Audiosrv"))
        {
            result.Code = "audio-down";
            throw new InvalidOperationException("O serviço de áudio do Windows não voltou. Reinicie o computador.");
        }
    }

    /// <summary>
    /// Confere de verdade, depois de reiniciar o áudio: toca silêncio no dispositivo (o Windows só monta a
    /// cadeia de efeitos com um app tocando) e espera o sinal de vida do efeito. Se não vier, olha se o
    /// audiodg carregou o DLL e junta o registro do efeito ao install.log, para dar para saber o porquê.
    /// </summary>
    static bool Verify(string deviceId, Result result)
    {
        var since = DateTime.UtcNow.AddSeconds(-1);
        bool alive = false;
        bool LiveSeen()
        {
            try
            {
                return Directory.Exists(EqConfig.LiveDir) && new DirectoryInfo(EqConfig.LiveDir)
                    .EnumerateFiles(EndpointGuid(deviceId) + ".*.txt").Any(f => f.LastWriteTimeUtc >= since);
            }
            catch { return false; }
        }
        // Toca silêncio por pelo menos 1,5 s (até 6 s esperando o sinal de vida) e confere que o som não parou no meio.
        var error = PlaySilence(deviceId, TimeSpan.FromSeconds(6), () => alive = alive || LiveSeen(), TimeSpan.FromMilliseconds(1500));
        bool stream = error == null;
        if (error != null) Log("verificação: o dispositivo não tocou som: " + error);

        bool loaded = alive;
        if (!alive)
        {
            try
            {
                EnablePrivilege("SeDebugPrivilege");
                foreach (var p in Process.GetProcessesByName("audiodg"))
                {
                    try { loaded |= p.Modules.Cast<ProcessModule>().Any(m => m.FileName.EndsWith("VolumEqApo.dll", StringComparison.OrdinalIgnoreCase)); }
                    catch (Exception ex) { Log("verificação: audiodg " + p.Id + ": " + ex.Message); }
                }
            }
            catch (Exception ex) { Log("verificação: " + ex.Message); }
        }
        foreach (var file in new[]
        {
            Path.Combine(EqConfig.LiveDir, "apo.log"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"ServiceProfiles\LocalService\AppData\Local\Temp\VolumEqApo.log"),
        })
        {
            try { if (File.Exists(file)) foreach (var line in File.ReadLines(file).TakeLast(8)) Log("  efeito: " + line); } catch { }
        }
        string verdict = alive ? "verified" : loaded ? "loaded-silent" : "not-loaded";
        Log("verificação: " + verdict + (stream ? "" : " (o dispositivo não abriu som)"));
        result.Notes.Add(verdict);
        return stream;
    }

    /// <summary>
    /// Toca silêncio no dispositivo em modo compartilhado. Devolve null se tocou o tempo todo, ou o motivo da falha
    /// (não abriu, ou parou no meio). <paramref name="tick"/> roda a cada 150 ms; o teste para cedo quando ele
    /// devolve verdadeiro, mas nunca antes de <paramref name="minimum"/>.
    /// </summary>
    static string? PlaySilence(string deviceId, TimeSpan maximum, Func<bool>? tick, TimeSpan? minimum = null)
    {
        Exception? stopped = null;
        bool stopping = false;
        try
        {
            using var en = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            using var device = en.GetDevice(deviceId);
            using var output = new NAudio.Wave.WasapiOut(device, NAudio.CoreAudioApi.AudioClientShareMode.Shared, true, 60);
            var mix = device.AudioClient.MixFormat;
            output.PlaybackStopped += (_, e) =>
            {
                if (!stopping) stopped ??= e.Exception ?? new InvalidOperationException("o som parou sozinho");
            };
            output.Init(new NAudio.Wave.SilenceProvider(NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(mix.SampleRate, mix.Channels)));
            output.Play();
            var clock = Stopwatch.StartNew();
            bool done = false;
            while (clock.Elapsed < maximum && stopped == null)
            {
                Thread.Sleep(150);
                if (tick != null && tick()) done = true;
                if ((done || tick == null) && clock.Elapsed >= (minimum ?? TimeSpan.Zero)) break;
            }
            if (stopped == null && output.PlaybackState != NAudio.Wave.PlaybackState.Playing)
                stopped = new InvalidOperationException("o som parou sozinho");
            stopping = true;
            output.Stop();
            return stopped?.Message;
        }
        catch (Exception ex) { return ex.Message; }
    }

    static bool ServiceRunning(string name)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("sc.exe", "query " + name)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            });
            if (p == null) return false;
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return text.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // ---------------------------------------------------------------- backup

    sealed class FxBackup
    {
        public int Version { get; set; } = 2;
        public bool WeSetProtected { get; set; }
        public int? ProtectedAudioPrevious { get; set; }
        public Dictionary<string, DeviceBackup> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public DeviceBackup DeviceFor(string id)
        {
            if (!Devices.TryGetValue(id, out var d)) Devices[id] = d = new DeviceBackup();
            return d;
        }
    }

    sealed class DeviceBackup
    {
        public bool CreatedFx { get; set; }
        public List<ValueBackup> Values { get; set; } = new();
        public PropsBackup Properties { get; set; } = new();
    }

    sealed class PropsBackup
    {
        public List<ValueBackup> Values { get; set; } = new();
    }

    sealed class ValueBackup
    {
        public string Name { get; set; } = "";
        public bool Existed { get; set; }
        public string Kind { get; set; } = "";
        public string? Text { get; set; }
        public int? Dword { get; set; }
        public string[]? Multi { get; set; }
    }

    static readonly JsonSerializerOptions BackupJson = new() { WriteIndented = true };

    // O backup fica em Program Files (só administrador escreve): um usuário comum não consegue forjar
    // valores que o processo elevado depois gravaria no registro.
    static FxBackup LoadBackup()
    {
        try
        {
            if (File.Exists(BackupPath))
            {
                var b = JsonSerializer.Deserialize<FxBackup>(File.ReadAllText(BackupPath), BackupJson);
                if (b != null)
                {
                    b.Devices = new Dictionary<string, DeviceBackup>(b.Devices, StringComparer.OrdinalIgnoreCase);
                    return b;
                }
            }
        }
        catch (Exception ex) { Log("backup ilegível: " + ex.Message); }
        return new FxBackup();
    }

    static void SaveBackup(FxBackup backup)
    {
        Directory.CreateDirectory(InstallDir);
        if (backup.Devices.Count == 0 && !backup.WeSetProtected)
        {
            try { File.Delete(BackupPath); } catch { }
            return;
        }
        var tmp = BackupPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(backup, BackupJson));
        File.Move(tmp, BackupPath, true);
    }

    static void SetString(RegistryKey key, DeviceBackup device, string name, string value)
    {
        Remember(key, device, name);
        key.SetValue(Canonical(key, name), value, RegistryValueKind.String);
    }

    static void Remember(RegistryKey key, DeviceBackup device, string name) => Remember(key, device.Values, name);
    static void Remember(RegistryKey key, PropsBackup props, string name) => Remember(key, props.Values, name);

    static void Remember(RegistryKey key, List<ValueBackup> list, string name)
    {
        if (list.Any(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return;   // guarda só o original
        var actual = key.GetValueNames().FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (actual == null)
        {
            list.Add(new ValueBackup { Name = name, Existed = false });
            return;
        }
        var kind = key.GetValueKind(actual);
        var b = new ValueBackup { Name = name, Existed = true, Kind = kind.ToString() };
        switch (kind)
        {
            case RegistryValueKind.String: b.Text = key.GetValue(actual) as string; break;
            case RegistryValueKind.DWord: b.Dword = key.GetValue(actual) as int?; break;
            case RegistryValueKind.MultiString: b.Multi = key.GetValue(actual) as string[]; break;
            case RegistryValueKind.ExpandString:
                b.Text = key.GetValue(actual, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                break;
            default:
                // Sem como guardar o original, não mexe: a ativação para e tudo é desfeito.
                throw new InvalidOperationException("O valor " + name + " do dispositivo tem um tipo (" + kind + ") que o Volum não sabe restaurar.");
        }
        list.Add(b);
    }

    static void Restore(RegistryKey key, List<ValueBackup> values)
    {
        foreach (var v in values)
        {
            var name = Canonical(key, v.Name);
            try
            {
                if (!v.Existed) TryDelete(key, name);
                else if (v.Kind == nameof(RegistryValueKind.String) && v.Text != null) key.SetValue(name, v.Text, RegistryValueKind.String);
                else if (v.Kind == nameof(RegistryValueKind.DWord) && v.Dword.HasValue) key.SetValue(name, v.Dword.Value, RegistryValueKind.DWord);
                else if (v.Kind == nameof(RegistryValueKind.MultiString) && v.Multi != null) key.SetValue(name, v.Multi, RegistryValueKind.MultiString);
                else if (v.Kind == nameof(RegistryValueKind.ExpandString) && v.Text != null) key.SetValue(name, v.Text, RegistryValueKind.ExpandString);
            }
            catch (Exception ex) { Log("não restaurou " + v.Name + ": " + ex.Message); }
        }
    }

    // ---------------------------------------------------------------- utilidades

    static bool IsOurs(string? clsid) => clsid != null && clsid.Trim().Equals(Clsid, StringComparison.OrdinalIgnoreCase);

    static string EndpointGuid(string deviceId)
    {
        var dot = deviceId.LastIndexOf('.');
        var guid = (dot >= 0 ? deviceId[(dot + 1)..] : deviceId).Trim().Trim('{', '}');
        if (!Guid.TryParse(guid, out var g)) throw new ArgumentException("id de dispositivo inválido");
        return "{" + g.ToString() + "}";
    }

    static string EndpointPath(string deviceId) => RenderRoot + "\\" + EndpointGuid(deviceId);
    static string FxPath(string deviceId) => EndpointPath(deviceId) + "\\FxProperties";

    static RegistryKey? OpenEndpoint(string deviceId)
    {
        try { return Registry.LocalMachine.OpenSubKey(EndpointPath(deviceId)); }
        catch { return null; }
    }

    /// <summary>
    /// Abre uma chave de dispositivo para ler e gravar valores. O Windows dá ao administrador só
    /// "consultar" e "definir valor" nessas chaves (o resto é do serviço de áudio), então é exatamente isso que se pede.
    /// </summary>
    static RegistryKey? OpenValues(string path) =>
        Registry.LocalMachine.OpenSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ReadKey | RegistryRights.SetValue);

    static object? ReadValue(RegistryKey? key, string name)
    {
        if (key == null) return null;
        var actual = key.GetValueNames().FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        return actual == null ? null : key.GetValue(actual);
    }

    static string? ReadString(RegistryKey? key, string name) => ReadValue(key, name) as string;
    static string[]? ReadMulti(RegistryKey? key, string name) => ReadValue(key, name) as string[];

    static string Canonical(RegistryKey key, string name) =>
        key.GetValueNames().FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;

    static void TryDelete(RegistryKey key, string name)
    {
        try { key.DeleteValue(Canonical(key, name), false); } catch { }
    }

    static bool SameFile(string a, string b) => Hash(a) == Hash(b);

    // A janela pergunta a cada vez que ganha foco: só recalcula o hash quando o arquivo muda.
    static readonly Dictionary<string, (long Size, DateTime Stamp, string Hash)> s_hashes = new(StringComparer.OrdinalIgnoreCase);

    static string CachedHash(string path)
    {
        var info = new FileInfo(path);
        lock (s_hashes)
        {
            if (s_hashes.TryGetValue(path, out var c) && c.Size == info.Length && c.Stamp == info.LastWriteTimeUtc) return c.Hash;
            var h = Hash(path);
            s_hashes[path] = (info.Length, info.LastWriteTimeUtc, h);
            return h;
        }
    }

    static string Hash(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s));
    }

    static void RequireAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Esta etapa precisa de permissão de administrador.");
    }

    static int Run(string file, string args, int timeoutMs = 20000)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, file), args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            });
            if (p == null) return -1;
            p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { Log(file + " " + args + " -> tempo esgotado"); return -2; }
            Log(file + " " + args + " -> " + p.ExitCode);
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            Log(file + " " + args + " -> " + ex.Message);
            return -1;
        }
    }

    static void WriteResult(string path, Result result)
    {
        try
        {
            // CreateNew: não segue um arquivo (ou link) que já estivesse no lugar.
            using var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(f, result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
        catch (Exception ex) { Log("resultado não gravado: " + ex.Message); }
    }

    static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(InstallDir);
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > 256 * 1024) info.Delete();
            File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    static void EnablePrivilege(string name)
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token)) return;
        try
        {
            if (!LookupPrivilegeValueW(null, name, out var luid)) return;
            var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { CloseHandle(token); }
    }

    // ---------------------------------------------------------------- Win32

    static readonly UIntPtr HKEY_LOCAL_MACHINE = new(0x80000002u);
    const int REG_OPTION_BACKUP_RESTORE = 0x4;
    const int KEY_ALL_ACCESS = 0xF003F;
    const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;
    const uint TOKEN_ADJUST_PRIVILEGES = 0x20, TOKEN_QUERY = 0x8, SE_PRIVILEGE_ENABLED = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    struct LUID { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID Luid; public uint Attributes; }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    static extern int RegCreateKeyExW(UIntPtr hKey, string subKey, int reserved, string? cls, int options, int sam,
        IntPtr security, out SafeRegistryHandle result, out int disposition);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool LookupPrivilegeValueW(string? system, string name, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, int len, IntPtr prev, IntPtr ret);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool MoveFileExW(string existing, string? replacement, int flags);
}
