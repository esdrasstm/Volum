using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using NAudio.CoreAudioApi;

namespace Volum;

/// <summary>
/// Janela do equalizador (eq.html). Guarda as curvas por dispositivo, grava o eq.bin que o efeito do
/// Windows lê (até ~30 vezes por segundo enquanto você arrasta), cuida da prévia, do analisador e da
/// ativação no sistema (num processo elevado, sem travar a janela).
/// </summary>
sealed class EqForm : Form
{
    static EqForm? s_open;
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    readonly WebView2 _web = new();
    readonly AppSettings _settings = AppSettings.Current;
    readonly Timer _binTimer = new() { Interval = 30 };
    readonly Timer _jsonTimer = new() { Interval = 500 };
    readonly Timer _liveTimer = new() { Interval = 1000 };
    readonly Timer _peakTimer = new() { Interval = 66 };
    readonly EqSpectrum _spectrum;
    readonly Dictionary<string, bool> _installed = new(StringComparer.OrdinalIgnoreCase);
    EqDocument _doc = EqConfig.Load();
    List<DeviceRow> _devices = new();
    bool _binDirty, _jsonDirty, _preview, _spectrumOn, _busy, _ready;

    public static void ShowWindow()
    {
        if (s_open != null && !s_open.IsDisposed)
        {
            if (s_open.WindowState == FormWindowState.Minimized) s_open.WindowState = FormWindowState.Normal;
            s_open.Activate();
            return;
        }
        s_open = new EqForm();
        s_open.Show();
        s_open.Activate();
    }

    EqForm()
    {
        float k = DeviceDpi / 96f;
        Text = "Volum — " + Strings.Get(_settings.Language, "eq");
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size((int)(1120 * k), (int)(760 * k));
        MinimumSize = new Size((int)(860 * k), (int)(620 * k));
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        var bg = IsDark() ? Color.FromArgb(9, 9, 9) : Color.White;   // fundo da identidade do Volum, sem piscar branco
        BackColor = bg;
        _web.DefaultBackgroundColor = bg;
        _web.Dock = DockStyle.Fill;
        Controls.Add(_web);

        _spectrum = new EqSpectrum(bins =>
        {
            if (IsDisposed || !_spectrumOn) return;
            try { BeginInvoke(() => Post(new { type = "spectrum", bins = bins.Select(v => (int)Math.Round(v)).ToArray() })); } catch { }
        });

        _binTimer.Tick += (_, _) => FlushBin();
        _jsonTimer.Tick += (_, _) => FlushJson();
        _liveTimer.Tick += (_, _) => PostLive();
        _peakTimer.Tick += (_, _) =>
        {
            if (!_preview) return;
            float peak = EqNative.PreviewPeak();
            Post(new { type = "peak", db = peak > 1e-6f ? 20 * Math.Log10(peak) : -120 });
        };
        _binTimer.Start();
        _jsonTimer.Start();
        _liveTimer.Start();
        _peakTimer.Start();

        Activated += (_, _) => { if (_ready && !_busy) PostState(); };
        Resize += (_, _) => UpdateSpectrum();
        FormClosed += (_, _) =>
        {
            _preview = false;
            EqNative.PreviewStop();
            _spectrum.Dispose();
            FlushBin();
            FlushJson();
            foreach (var t in new[] { _binTimer, _jsonTimer, _liveTimer, _peakTimer }) t.Dispose();
            if (s_open == this) s_open = null;
        };
        _ = InitAsync();
    }

    bool IsDark() => _settings.Theme == ThemeMode.System ? MixerForm.WindowsIsDark() : _settings.Theme == ThemeMode.Dark;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int dark = IsDark() ? 1 : 0;
            DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            int caption = dark == 1 ? 0x00090909 : 0x00FFFFFF, border = dark == 1 ? 0x001C1C1C : 0x00E6E6E6;
            DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));
        }
        catch { }
    }

    async Task InitAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, MixerForm.WebViewDataFolder);
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = Program.DevMode;
            core.Settings.AreDevToolsEnabled = Program.DevMode;
            core.Settings.AreBrowserAcceleratorKeysEnabled = Program.DevMode;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith("https://app.volum/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            core.WebMessageReceived += OnWebMessage;
            var source = Program.DevMode ? MixerForm.FindSourceWwwroot() : null;
            core.SetVirtualHostNameToFolderMapping("app.volum",
                source ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Allow);
            core.Navigate("https://app.volum/eq.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show(Strings.Get(_settings.Language, "webviewError") + "\n\n" + ex.Message,
                "Volum", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    // ================================================================ mensagens da página

    void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "ready":
                    _ready = true;
                    _binDirty = true;   // garante o eq.bin em dia (ele some quando o Volum é desinstalado)
                    PostState();
                    break;
                case "select":
                    SetPreview(false);
                    _doc.ActiveId = root.GetProperty("id").GetString() ?? "";
                    _jsonDirty = true;
                    PostState();
                    UpdateSpectrum();
                    break;
                case "curve":
                    ApplyCurve(Active(), root.GetProperty("curve"));
                    Changed();
                    break;
                case "preview":
                    SetPreview(root.GetProperty("on").GetBoolean());
                    break;
                case "spectrum":
                    _spectrumOn = root.GetProperty("on").GetBoolean();
                    UpdateSpectrum();
                    break;
                case "import":
                    Import(root.GetProperty("text").GetString() ?? "");
                    break;
                case "export":
                    Export();
                    break;
                case "presetSave":
                    SavePreset(root.GetProperty("name").GetString() ?? "");
                    break;
                case "presetDelete":
                    var name = root.GetProperty("name").GetString() ?? "";
                    _doc.Presets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    _jsonDirty = true;
                    PostState();
                    break;
                case "install":
                    _ = RunSystemAsync("--eq-install");
                    break;
                case "remove":
                    _ = RunSystemAsync("--eq-remove");
                    break;
                case "openSound":
                    Process.Start(new ProcessStartInfo("control.exe", "mmsys.cpl") { UseShellExecute = true });
                    break;
                case "openAutoEq":
                    Process.Start(new ProcessStartInfo("https://autoeq.app/") { UseShellExecute = true });
                    break;
            }
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    static void ApplyCurve(EqCurve target, JsonElement c)
    {
        target.Bypass = c.GetProperty("bypass").GetBoolean();
        target.PreampDb = c.GetProperty("preampDb").GetDouble();
        target.AutoPreamp = c.GetProperty("autoPreamp").GetBoolean();
        target.Preset = c.TryGetProperty("preset", out var p) ? p.GetString() ?? "" : "";
        target.Bands = c.GetProperty("bands").EnumerateArray().Take(EqNative.MaxBands).Select(b => new EqBand
        {
            Type = b.GetProperty("type").GetInt32(),
            Enabled = b.GetProperty("enabled").GetBoolean(),
            FreqHz = b.GetProperty("freq").GetDouble(),
            GainDb = b.GetProperty("gain").GetDouble(),
            Q = b.GetProperty("q").GetDouble(),
            Slope = b.TryGetProperty("slope", out var s) ? s.GetInt32() : 1
        }).ToList();
        EqConfig.Sanitize(target);
    }

    void Changed()
    {
        _binDirty = true;
        _jsonDirty = true;
        PushPreview();
    }

    void Import(string text)
    {
        var curve = EqConfig.Import(text, out bool trimmed, out bool graphic);
        if (curve == null)
        {
            Post(new { type = "notice", kind = "error", code = graphic ? "import-graphic" : "import-empty" });
            return;
        }
        Post(new { type = "imported", curve = CurveJson(curve), trimmed });
    }

    void Export()
    {
        var curve = Active();
        using var dlg = new SaveFileDialog
        {
            Filter = "Equalizer APO / AutoEq (*.txt)|*.txt",
            FileName = "Volum EQ - " + Safe(DeviceName(curve.Id)) + ".txt",
            OverwritePrompt = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, EqConfig.Export(curve), new UTF8Encoding(false));
            Post(new { type = "notice", kind = "ok", code = "exported" });
        }
        catch (Exception ex) { Post(new { type = "notice", kind = "error", code = "export-failed", text = ex.Message }); }
    }

    static string Safe(string name) => string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim();

    void SavePreset(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Length > 40) return;
        var curve = Active();
        _doc.Presets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var preset = new EqPreset { Name = name };
        preset.CopyFrom(curve);
        preset.Bypass = false;
        preset.Preset = "";
        _doc.Presets.Add(preset);
        curve.Preset = "user:" + name;
        _jsonDirty = true;
        PostState();
    }

    // ================================================================ áudio

    EqDeviceCurve Active()
    {
        if (string.IsNullOrEmpty(_doc.ActiveId)) _doc.ActiveId = DefaultDeviceId() ?? "default";
        var curve = EqConfig.ForDevice(_doc, _doc.ActiveId);
        var row = _devices.FirstOrDefault(d => d.Id.Equals(curve.Id, StringComparison.OrdinalIgnoreCase));
        if (row != null) curve.Name = row.Name;
        return curve;
    }

    bool Installed(string id)
    {
        if (!_installed.TryGetValue(id, out var on)) _installed[id] = on = EqInstaller.IsInstalled(id);
        return on;
    }

    void FlushBin()
    {
        if (!_binDirty) return;
        _binDirty = false;
        if (!EqConfig.WriteBin(_doc, Installed)) _binDirty = true;   // arquivo ocupado: tenta no próximo tique
    }

    void FlushJson()
    {
        if (!_jsonDirty) return;
        _jsonDirty = false;
        try { EqConfig.SaveJson(_doc); } catch { _jsonDirty = true; }
    }

    void PushPreview()
    {
        if (!_preview) return;
        var curve = Active();
        // Com o efeito ativo neste dispositivo, o ruído sai cru e quem equaliza é o Windows (é o teste de verdade).
        bool system = Installed(curve.Id);
        var blob = EqConfig.ParamsBlob(curve, EqConfig.EffectivePreamp(curve), false);
        if (system) blob[0] = 1;
        EqNative.PreviewSet(blob);
    }

    void SetPreview(bool on)
    {
        on = on && EqNative.Available;
        if (on == _preview && !on) return;
        _preview = on;
        if (!on)
        {
            EqNative.PreviewStop();
            Post(new { type = "preview", on = false });
            return;
        }
        PushPreview();
        var id = Active().Id;
        int hr = EqNative.PreviewStart(id == "default" ? null : id);
        if (hr != 0)
        {
            _preview = false;
            Post(new { type = "preview", on = false, error = EqNative.PreviewError() });
        }
        else Post(new { type = "preview", on = true });
    }

    void UpdateSpectrum()
    {
        bool want = _spectrumOn && WindowState != FormWindowState.Minimized && !_busy;
        if (want && !_spectrum.Running) _spectrum.Start(Active().Id);
        else if (!want && _spectrum.Running) _spectrum.Stop();
        else if (want && _spectrum.Running && _doc.ActiveId != _spectrumDevice) _spectrum.Start(Active().Id);
        _spectrumDevice = want ? _doc.ActiveId : null;
    }
    string? _spectrumDevice;

    // ================================================================ ativação no Windows

    async Task RunSystemAsync(string action)
    {
        if (_busy) return;
        if (Updater.IsStore) { Post(new { type = "notice", kind = "error", code = "store" }); return; }
        var id = Active().Id;
        if (id == "default") return;
        FlushBin();
        FlushJson();
        SetPreview(false);
        _busy = true;
        _spectrum.Stop();
        Post(new { type = "busy", on = true, action });

        var resultPath = Path.Combine(Path.GetTempPath(), "volum-eq-" + Guid.NewGuid().ToString("N") + ".json");
        string code = "error", detail = "";
        List<string> notes = new();
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            psi.ArgumentList.Add(action);
            psi.ArgumentList.Add(id);
            psi.ArgumentList.Add("--eq-result");
            psi.ArgumentList.Add(resultPath);
            using var p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();
            if (File.Exists(resultPath))
            {
                var r = JsonSerializer.Deserialize<EqInstaller.Result>(await File.ReadAllTextAsync(resultPath), Json);
                if (r != null) { code = r.Code; detail = r.Detail; notes = r.Notes; }
                File.Delete(resultPath);
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { code = "cancelled"; }   // UAC recusado
        catch (Exception ex) { detail = ex.Message; }

        _busy = false;
        if (IsDisposed) return;
        _installed.Clear();
        _binDirty = true;
        FlushBin();   // as marcas de "ativado" no eq.bin acompanham o registro
        PostState();
        UpdateSpectrum();
        Post(new { type = "busy", on = false });
        Post(new { type = "notice", kind = code is "installed" or "removed" ? "ok" : code == "cancelled" ? "info" : "error", code, text = detail, notes });
    }

    // ================================================================ estado para a página

    void PostState()
    {
        _devices = Devices();
        _installed.Clear();
        if (_devices.Count > 0 && _devices.All(d => !d.Id.Equals(_doc.ActiveId, StringComparison.OrdinalIgnoreCase)))
            _doc.ActiveId = (_devices.FirstOrDefault(d => d.IsDefault) ?? _devices[0]).Id;
        var curve = Active();
        bool installed = Installed(curve.Id);
        Post(new
        {
            type = "state",
            language = Strings.Normalize(_settings.Language),
            dark = IsDark(),
            native = EqNative.Available,
            store = Updater.IsStore,
            devices = _devices.Select(d => new { id = d.Id, name = d.Name, isDefault = d.IsDefault, installed = Installed(d.Id) }),
            activeId = curve.Id,
            curve = CurveJson(curve),
            preampDb = EqConfig.EffectivePreamp(curve),
            presets = _doc.Presets.Select(p => new { name = p.Name, curve = CurveJson(p) }),
            system = new
            {
                installed,
                anyInstalled = _installed.Values.Any(v => v) || EqInstaller.AnyInstalled(),
                needsUpdate = installed && EqInstaller.NeedsUpdate(),
                enhancementsOff = EqInstaller.EnhancementsOff(curve.Id)
            }
        });
        PostLive();
    }

    static object CurveJson(EqCurve c) => new
    {
        bypass = c.Bypass,
        preampDb = c.PreampDb,
        autoPreamp = c.AutoPreamp,
        preset = c.Preset,
        bands = c.Bands.Select(b => new { type = b.Type, enabled = b.Enabled, freq = b.FreqHz, gain = b.GainDb, q = b.Q, slope = b.Slope })
    };

    readonly Dictionary<string, long> _clipsSeen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Lê o sinal de vida que o efeito deixa a cada segundo e diz se o Windows está mesmo passando o som por ele.
    /// O Windows roda mais de uma cópia do efeito por dispositivo, cada uma com o seu arquivo
    /// ("&lt;guid&gt;.&lt;pid&gt;-&lt;n&gt;.txt"): vale qualquer uma atualizada, e os estouros são contados por cópia.
    /// </summary>
    void PostLive()
    {
        if (!_ready || IsDisposed) return;
        var curve = Active();
        if (!Installed(curve.Id)) { Post(new { type = "live", state = "off" }); _clipsSeen.Clear(); return; }
        try
        {
            var fresh = new DirectoryInfo(EqConfig.LiveDir).EnumerateFiles(EndpointKey(curve.Id) + ".*.txt")
                .Where(f => DateTime.UtcNow - f.LastWriteTimeUtc <= TimeSpan.FromSeconds(4))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            if (fresh.Count == 0)
            {
                Post(new { type = "live", state = "waiting" });
                _clipsSeen.Clear();
                return;
            }
            Dictionary<string, string>? newest = null;
            long newClips = 0;
            bool anyFloat = false, chained = false;
            foreach (var file in fresh)
            {
                var values = ReadLive(file.FullName);
                if (values == null) continue;
                newest ??= values;
                long clips = Num(values, "clips");
                if (_clipsSeen.TryGetValue(file.Name, out var before) && clips >= before) newClips += clips - before;
                _clipsSeen[file.Name] = clips;
                anyFloat |= Num(values, "float") == 1;
                chained |= Num(values, "child") > 0;
            }
            if (newest == null) { Post(new { type = "live", state = "waiting" }); return; }
            Post(new
            {
                type = "live",
                state = anyFloat ? "running" : "format",
                rate = double.TryParse(newest.GetValueOrDefault("rate"), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0,
                channels = Num(newest, "channels"),
                clips = newClips,
                chained
            });
        }
        catch { Post(new { type = "live", state = "waiting" }); }
    }

    static Dictionary<string, string>? ReadLive(string path)
    {
        try
        {
            // Abre deixando o efeito trocar o arquivo ao mesmo tempo (ele grava a cada segundo).
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var rd = new StreamReader(fs);
            return rd.ReadToEnd().Split('\n')
                .Select(l => l.Split('=', 2)).Where(p => p.Length == 2)
                .ToDictionary(p => p[0].Trim(), p => p[1].Trim());
        }
        catch (IOException) { return null; }
    }

    static long Num(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var v) && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    static string EndpointKey(string id)
    {
        var dot = id.LastIndexOf('.');
        return dot >= 0 ? id[(dot + 1)..] : id;
    }

    string DeviceName(string id) => _devices.FirstOrDefault(d => d.Id == id)?.Name ?? "Volum";

    static string? DefaultDeviceId()
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            using var d = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            return d.ID;
        }
        catch { return null; }
    }

    static List<DeviceRow> Devices()
    {
        var list = new List<DeviceRow>();
        try
        {
            using var en = new MMDeviceEnumerator();
            string def = DefaultDeviceId() ?? "";
            foreach (var device in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                try { list.Add(new DeviceRow(device.ID, device.FriendlyName, device.ID == def)); }
                catch { }
                finally { device.Dispose(); }
            }
        }
        catch { }
        return list.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    void Post(object message)
    {
        try { _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json)); }
        catch { }
    }

    sealed record DeviceRow(string Id, string Name, bool IsDefault);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
