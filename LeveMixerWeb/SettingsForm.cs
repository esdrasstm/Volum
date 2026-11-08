using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Volum;

/// <summary>
/// Janela de configurações (engrenagem do mixer ou menu da bandeja): tema, idioma, iniciar com o Windows,
/// modo leve, atalho, lembrete de água e "Sobre". A interface é o settings.html da wwwroot.
/// Uma janela normal do Windows (com barra de título); só existe enquanto está aberta: ao fechar,
/// o WebView2 dela é descartado e a memória volta.
/// </summary>
sealed class SettingsForm : Form
{
    static SettingsForm? s_open;

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    readonly WebView2 _web = new();
    readonly AppSettings _settings = AppSettings.Current;   // a mesma do mixer

    /// <summary>Abre a janela (ou traz para a frente, se já estiver aberta).</summary>
    public static void ShowWindow()
    {
        if (s_open != null && !s_open.IsDisposed)
        {
            if (s_open.WindowState == FormWindowState.Minimized) s_open.WindowState = FormWindowState.Normal;
            s_open.Activate();
            return;
        }
        s_open = new SettingsForm();
        s_open.Show();
        s_open.Activate();
    }

    SettingsForm()
    {
        float k = DeviceDpi / 96f;
        Text = $"Volum — {Strings.Get(_settings.Language, "settings")}";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size((int)(780 * k), (int)(560 * k));
        MinimumSize = new Size((int)(620 * k), (int)(440 * k));
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // Fundo da cor da página, para não piscar branco enquanto o WebView2 carrega
        var bg = IsDark() ? Color.FromArgb(9, 9, 9) : Color.White;   // fundo #090909 da identidade do Volum
        BackColor = bg;
        _web.DefaultBackgroundColor = bg;
        _web.Dock = DockStyle.Fill;
        Controls.Add(_web);

        Water.Changed += OnWaterChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _ = InitAsync();
    }

    bool IsDark() => _settings.Theme == ThemeMode.System ? MixerForm.WindowsIsDark() : _settings.Theme == ThemeMode.Dark;

    // Barra de título clara ou escura, acompanhando o tema
    void ApplyTitleBar()
    {
        if (!IsHandleCreated) return;
        try
        {
            int dark = IsDark() ? 1 : 0;
            DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
            // Barra de título e borda na cor da página (Windows 11; no 10 é ignorado). COLORREF = 0x00BBGGRR
            int caption = dark == 1 ? 0x00090909 : 0x00FFFFFF, border = dark == 1 ? 0x001C1C1C : 0x00E6E6E6;
            DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));   // DWMWA_CAPTION_COLOR
            DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));    // DWMWA_BORDER_COLOR
        }
        catch { }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTitleBar();
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

            var sourceRoot = Program.DevMode ? MixerForm.FindSourceWwwroot() : null;
            core.SetVirtualHostNameToFolderMapping("app.volum",
                sourceRoot ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Allow);
            core.Navigate("https://app.volum/settings.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show(Strings.Get(_settings.Language, "webviewError") + "\n\n" + ex.Message,
                "Volum", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
        }
    }

    void Post(object message)
    {
        try { _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json)); }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    async Task SetAppNotesAsync(bool want)
    {
        bool ok = await IslandForm.SetAppNotesAsync(want);
        _settings.IslandAppNotes = want && ok;
        _settings.Save();
        if (!IsDisposed) Post(new { type = "appNotes", on = _settings.IslandAppNotes, denied = want && !ok });
    }

    async Task SetWeatherAsync(string city)
    {
        string? place = await IslandForm.WeatherCityChangedAsync(city);
        if (!IsDisposed) Post(new { type = "weatherCity", place, empty = city.Length == 0, error = place == null ? Weather.LastError : "" });
    }

    async Task SetCalendarAsync(string url)
    {
        _settings.CalendarUrl = url;
        _settings.Save();
        int n = await IslandForm.CalendarChangedAsync();
        if (!IsDisposed) Post(new { type = "calendar", count = n, empty = url.Length == 0, error = n < 0 ? Agenda.LastError : "" });
    }

    void PostSettings()
    {
        var w = Water.Data;
        Post(new
        {
            type = "settings",
            theme = _settings.Theme.ToString().ToLowerInvariant(),
            language = Strings.Normalize(_settings.Language),
            autostart = AutoStart.IsEnabled,
            lite = _settings.LightMode,
            systemDark = MixerForm.WindowsIsDark(),
            hotkey = new { mods = _settings.HotkeyMods, key = _settings.HotkeyKey },
            water = new { enabled = w.Enabled, goalMl = w.GoalMl, intervalMin = w.IntervalMin, cupMl = w.CupMl, todayMl = w.TodayMl, sound = w.Sound },
            version = Updater.Version,
            store = Updater.IsStore,
            brazil = Support.InBrazil,
            island = _settings.IslandEnabled,
            islandStyle = _settings.IslandStyle == "float" ? "float" : "notch",
            appNotes = _settings.IslandAppNotes && AppNotifications.Supported,
            appNotesSupported = AppNotifications.Supported,
            calendarUrl = _settings.CalendarUrl ?? "",
            weatherCity = _settings.WeatherCity ?? "",
            focus = new
            {
                pomodoro = _settings.PomodoroEnabled,
                focusMin = _settings.FocusMin,
                shortMin = _settings.ShortBreakMin,
                longMin = _settings.LongBreakMin,
                every = _settings.LongBreakEvery,
                pomoSound = _settings.PomodoroSound,
                osd = _settings.IslandOsd,
                otherScreen = _settings.IslandOtherScreen,
                standUp = _settings.StandUpEnabled,
                standMin = _settings.StandUpMin
            }
        });
    }

    void OnWaterChanged()
    {
        var w = Water.Data;
        if (!IsDisposed) Post(new { type = "water", water = new { enabled = w.Enabled, goalMl = w.GoalMl, intervalMin = w.IntervalMin, cupMl = w.CupMl, todayMl = w.TodayMl, sound = w.Sound } });
    }

    // Trocou o tema do Windows com a janela aberta
    void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.General || IsDisposed) return;
        BeginInvoke(new Action(() =>
        {
            ApplyTitleBar();
            Post(new { type = "systemTheme", dark = MixerForm.WindowsIsDark() });
        }));
    }

    void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "ready":
                    PostSettings();
                    break;

                case "setTheme":   // vale para o mixer na próxima vez que ele abrir
                    if (Enum.TryParse<ThemeMode>(root.GetProperty("mode").GetString(), true, out var mode))
                    {
                        _settings.Theme = mode;
                        _settings.Save();
                        ApplyTitleBar();
                    }
                    break;

                case "setLanguage":   // o menu da bandeja e o mixer leem na próxima vez que abrirem
                    _settings.Language = Strings.Normalize(root.GetProperty("lang").GetString());
                    _settings.Save();
                    Text = $"Volum — {Strings.Get(_settings.Language, "settings")}";
                    break;

                case "setLightMode":
                    _settings.LightMode = root.GetProperty("enabled").GetBoolean();
                    _settings.Save();
                    break;

                case "setAutostart":
                    AutoStart.Set(root.GetProperty("enabled").GetBoolean());
                    break;

                case "hotkeyRecord":   // começou/cancelou a gravação de um atalho novo
                    if (root.GetProperty("on").GetBoolean()) GlobalHotkey.Pause();
                    else GlobalHotkey.Resume();
                    break;

                case "setHotkey":      // gravou um atalho novo (key 0 = desligar)
                {
                    uint mods = root.GetProperty("mods").GetUInt32(), key = root.GetProperty("key").GetUInt32();
                    bool ok = GlobalHotkey.Set(mods, key);
                    if (ok)
                    {
                        _settings.HotkeyMods = mods;
                        _settings.HotkeyKey = key;
                        _settings.Save();
                    }
                    else GlobalHotkey.Set(_settings.HotkeyMods, _settings.HotkeyKey);   // em uso por outro app: volta o antigo
                    Post(new { type = "hotkey", ok, mods = _settings.HotkeyMods, key = _settings.HotkeyKey });
                    break;
                }

                case "setWater":   // lembrete de água
                    Water.Configure(
                        root.GetProperty("enabled").GetBoolean(),
                        root.GetProperty("goalMl").GetInt32(),
                        root.GetProperty("intervalMin").GetInt32(),
                        root.GetProperty("cupMl").GetInt32(),
                        !root.TryGetProperty("sound", out var snd) || snd.GetBoolean());
                    break;

                case "testWater":   // "Testar aviso": mostra o lembrete de água agora (pela ilha, se ligada)
                    Water.Test();
                    break;

                case "setIsland":   // liga/desliga a ilha no topo da tela
                    _settings.IslandEnabled = root.GetProperty("enabled").GetBoolean();
                    _settings.Save();
                    IslandForm.Apply(_settings.IslandEnabled);
                    break;

                case "setAppNotes":   // notificações dos outros apps na ilha (só Store): o Windows pede a permissão
                {
                    _ = SetAppNotesAsync(root.GetProperty("on").GetBoolean());   // continua nesta thread (a da janela)
                    break;
                }

                case "setWeather":   // cidade do clima: procura e baixa a previsão para conferir
                    _ = SetWeatherAsync((root.GetProperty("city").GetString() ?? "").Trim());
                    break;

                case "setCalendar":   // endereço iCal da agenda: salva e baixa para conferir
                {
                    _ = SetCalendarAsync((root.GetProperty("url").GetString() ?? "").Trim());
                    break;
                }

                case "setIslandStyle":   // formato da ilha: grudada no topo ou flutuante
                    _settings.IslandStyle = root.GetProperty("style").GetString() == "float" ? "float" : "notch";
                    _settings.Save();
                    IslandForm.ApplyStyle();
                    break;

                case "setFocus":   // Pomodoro e aviso de levantar (página Ilha)
                    int Get(string key, int def, int min, int max) =>
                        root.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? Math.Clamp(n, min, max) : def;
                    _settings.PomodoroEnabled = root.GetProperty("pomodoro").GetBoolean();
                    _settings.FocusMin = Get("focusMin", 25, 5, 120);
                    _settings.ShortBreakMin = Get("shortMin", 5, 1, 30);
                    _settings.LongBreakMin = Get("longMin", 15, 5, 60);
                    _settings.LongBreakEvery = Get("every", 4, 2, 8);
                    if (root.TryGetProperty("pomoSound", out var ps) && Array.IndexOf(PomodoroSound.Names, ps.GetString()) >= 0)
                        _settings.PomodoroSound = ps.GetString()!;
                    _settings.StandUpEnabled = root.GetProperty("standUp").GetBoolean();
                    if (root.TryGetProperty("osd", out var osd)) _settings.IslandOsd = osd.GetBoolean();
                    if (root.TryGetProperty("otherScreen", out var os)) _settings.IslandOtherScreen = os.GetBoolean();
                    _settings.StandUpMin = Get("standMin", 60, 20, 240);
                    _settings.Save();
                    if (!_settings.PomodoroEnabled) Pomodoro.Stop();
                    Pomodoro.Configure();
                    StandUp.Apply();
                    IslandForm.ApplyOsd();
                    IslandForm.SettingsChanged();
                    break;

                case "testPomoSound":   // escolheu um som do Pomodoro: toca para a pessoa ouvir
                    PomodoroSound.Play(root.GetProperty("sound").GetString());
                    break;

                case "testStand":   // "Testar aviso" do levantar
                    StandUp.Test();
                    break;

                case "copyPix":   // "Apoiar o Volum": copia o Pix copia e cola para colar no app do banco
                    try { Clipboard.SetText(Support.Pix); } catch (Exception ex) { Debug.WriteLine(ex); }
                    break;

                case "openSite":   // "Sobre": site, sugestões e GitHub (a janela continua aberta)
                    MixerForm.OpenSite(root.TryGetProperty("section", out var sec) ? sec.GetString() : null);
                    break;
            }
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        GlobalHotkey.Resume();   // fechou no meio da gravação de um atalho novo: volta o antigo
        Water.Changed -= OnWaterChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        if (s_open == this) s_open = null;
        base.OnFormClosed(e);   // janela aberta com Show(): o WinForms descarta ela (e o WebView2) sozinho
        BeginInvokeTrim();
    }

    // Depois de fechar, devolve a memória do WebView2 da janela
    static void BeginInvokeTrim()
    {
        var t = new System.Windows.Forms.Timer { Interval = 500 };
        t.Tick += (_, _) => { t.Dispose(); MemoryTrim.Run(); };
        t.Start();
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
