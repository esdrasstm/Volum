using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Volum;

static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "Volum";
    const string OldAppName = "LeveMixer";   // nome antigo do app

    // Na versão da Microsoft Store (pacote MSIX) a chave Run não vale: o pacote declara uma "tarefa de
    // inicialização" (StartupTask, Store\AppxManifest.xml) que o Windows liga e desliga.
    const string StoreTaskId = "VolumStartup";

    static Windows.ApplicationModel.StartupTask? StoreTask()
    {
        try { return System.Threading.Tasks.Task.Run(async () => await Windows.ApplicationModel.StartupTask.GetAsync(StoreTaskId)).Result; }
        catch { return null; }
    }

    public static bool IsEnabled
    {
        get
        {
            if (Updater.IsStore)
            {
                var s = StoreTask()?.State;
                return s is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
            }
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(AppName) != null;
        }
    }

    // Rodando via "dotnet run" o ProcessPath é o dotnet.exe: não faz sentido registrar isso.
    public static bool CanRegister =>
        !string.Equals(System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath),
                       "dotnet", StringComparison.OrdinalIgnoreCase);

    public static void Set(bool on)
    {
        if (Updater.IsStore)
        {
            var task = StoreTask();
            if (task == null) return;
            // Se a pessoa desligou no Gerenciador de Tarefas, o Windows não deixa o app religar sozinho
            if (on) { try { System.Threading.Tasks.Task.Run(async () => await task.RequestEnableAsync()).Wait(); } catch { } }
            else task.Disable();
            return;
        }
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (k == null) return;
        if (on)
        {
            if (CanRegister) k.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
        }
        else k.DeleteValue(AppName, false);
    }

    // Se o usuário moveu o .exe de pasta, atualiza o caminho registrado (só se estiver ativado).
    public static void RefreshPath()
    {
        if (Updater.IsStore) return;   // no pacote o Windows sabe onde está o .exe
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (k == null || !CanRegister) return;

        // Estava registrado com o nome antigo (LeveMixer): troca pelo novo
        if (k.GetValue(OldAppName) != null)
        {
            k.DeleteValue(OldAppName, false);
            k.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
            return;
        }

        var current = k.GetValue(AppName) as string;
        if (current == null) return;
        var wanted = $"\"{Environment.ProcessPath}\"";
        if (!string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
            k.SetValue(AppName, wanted);
    }
}

/// <summary>Força o .NET a devolver memória ao Windows (mantém o app leve na bandeja).</summary>
static class MemoryTrim
{
    [DllImport("kernel32.dll")]
    static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    public static void Run()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var p = Process.GetCurrentProcess();
        SetProcessWorkingSetSize(p.Handle, (IntPtr)(-1), (IntPtr)(-1));
    }
}

/// <summary>
/// Textos do lado C# (menu da bandeja e avisos) nos 3 idiomas.
/// Os textos da interface ficam no app.js (I18N). Português é sempre o padrão.
/// </summary>
static class Strings
{
    public static readonly string[] Languages = { "pt", "en", "es" };

    static readonly System.Collections.Generic.Dictionary<string, string[]> Texts = new()
    {
        //                 pt                          en                           es
        ["autostart"] = new[] { "Iniciar com o Windows", "Start with Windows", "Iniciar con Windows" },
        ["exit"]      = new[] { "Sair", "Exit", "Salir" },
        ["settings"]  = new[] { "Configurações", "Settings", "Configuración" },
        ["eq"]        = new[] { "Equalizador", "Equalizer", "Ecualizador" },
        // Travar o teclado para limpar (menu da bandeja e aviso na tela)
        ["kbLock"]    = new[] { "Travar teclado para limpar", "Lock keyboard to clean", "Bloquear teclado para limpiar" },
        ["kbLocked"]  = new[] { "Teclado travado", "Keyboard locked", "Teclado bloqueado" },
        ["kbLeft"]    = new[] { "Destrava sozinho em {0}", "Unlocks by itself in {0}", "Se desbloquea solo en {0}" },
        ["kbUnlock"]  = new[] { "Destravar", "Unlock", "Desbloquear" },
        // Aviso de que o Volum foi atualizado (perto da bandeja, uma vez por versão)
        ["updTitle"]  = new[] { "Volum atualizado", "Volum updated", "Volum actualizado" },
        ["updText"]   = new[] { "Versão {0}: veja o que tem de novo", "Version {0}: see what's new", "Versión {0}: mira las novedades" },
        ["updOpen"]   = new[] { "Ver novidades", "See what's new", "Ver novedades" },
        ["storeUpdTitle"]  = new[] { "Versão nova do Volum", "New Volum version", "Nueva versión de Volum" },
        ["storeUpdText"]   = new[] { "Já está na Microsoft Store", "Available on the Microsoft Store", "Ya está en la Microsoft Store" },
        ["storeUpdAction"] = new[] { "Atualizar", "Update", "Actualizar" },
        // Pomodoro e aviso de levantar (pela ilha)
        ["pomoBreak"]     = new[] { "Hora da pausa", "Break time", "Hora del descanso" },
        ["pomoBreakText"] = new[] { "{0} min para descansar", "{0} min to rest", "{0} min para descansar" },
        ["pomoLong"]      = new[] { "Pausa longa", "Long break", "Descanso largo" },
        ["pomoLongText"]  = new[] { "{0} min: levante e beba água", "{0} min: stand up and drink water", "{0} min: levántate y toma agua" },
        ["pomoDone"]      = new[] { "Pausa acabou", "Break's over", "Terminó el descanso" },
        ["pomoDoneText"]  = new[] { "Bora para mais um foco?", "Ready for another focus?", "¿Otro rato de enfoque?" },
        ["pomoGo"]        = new[] { "Focar", "Focus", "Enfocar" },
        ["standTitle"]    = new[] { "Hora de levantar", "Time to stand up", "Hora de levantarse" },
        ["standText"]     = new[] { "Sentado há {0}: dê uma volta", "Sitting for {0}: take a walk", "Sentado hace {0}: da una vuelta" },
        ["standOk"]       = new[] { "Levantei", "I'm up", "Ya me levanté" },
        // Indicadores na ilha (Caps Lock, Num Lock, volume e microfone)
        ["osdCapsOn"]  = new[] { "Caps Lock ativado", "Caps Lock on", "Bloq Mayús activado" },
        ["osdCapsOff"] = new[] { "Caps Lock desativado", "Caps Lock off", "Bloq Mayús desactivado" },
        ["osdNumOn"]   = new[] { "Num Lock ativado", "Num Lock on", "Bloq Num activado" },
        ["osdNumOff"]  = new[] { "Num Lock desativado", "Num Lock off", "Bloq Num desactivado" },
        ["osdVolume"]  = new[] { "Volume", "Volume", "Volumen" },
        ["osdMuted"]   = new[] { "Som mudo", "Sound muted", "Sonido silenciado" },
        ["osdMicOff"]  = new[] { "Microfone mudo", "Microphone muted", "Micrófono silenciado" },
        ["osdMicOn"]   = new[] { "Microfone ligado", "Microphone on", "Micrófono activado" },
        ["osdPlay"]    = new[] { "Tocando", "Playing", "Reproduciendo" },
        ["osdPause"]   = new[] { "Pausado", "Paused", "En pausa" },
        ["osdNext"]    = new[] { "Próxima", "Next", "Siguiente" },
        ["osdPrev"]    = new[] { "Anterior", "Previous", "Anterior" },
        ["osdBright"]  = new[] { "Brilho", "Brightness", "Brillo" },
        ["actCharging"]   = new[] { "Carregando", "Charging", "Cargando" },
        ["actOnBattery"]  = new[] { "Na bateria", "On battery", "Con batería" },
        ["actHeadphones"] = new[] { "Fone conectado", "Headphones connected", "Auriculares conectados" },
        ["actOutput"]     = new[] { "Saída de áudio", "Audio output", "Salida de audio" },
        ["actMicInUse"]   = new[] { "Microfone em uso", "Microphone in use", "Micrófono en uso" },
        ["actCamInUse"]   = new[] { "Câmera em uso", "Camera in use", "Cámara en uso" },
        ["calSoon"]       = new[] { "Em {0} min", "In {0} min", "En {0} min" },
        ["calNow"]        = new[] { "Começando agora", "Starting now", "Empieza ahora" },
        // Mascote dando oi quando o PC liga (pela ilha), conforme a hora do dia
        ["hiMorning"]     = new[] { "Bom dia!", "Good morning!", "¡Buenos días!" },
        ["hiAfternoon"]     = new[] { "Boa tarde!", "Good afternoon!", "¡Buenas tardes!" },
        ["hiEvening"]     = new[] { "Boa noite!", "Good evening!", "¡Buenas noches!" },
        // Lembrete de água (aviso na tela). {0} = quanto falta, {1} = meta do dia
        ["waterTitle"] = new[] { "Hora de beber água", "Time to drink water", "Hora de tomar agua" },
        ["waterLeft"]  = new[] { "Faltam {0} para a meta de hoje ({1})", "{0} left to today's goal ({1})", "Faltan {0} para la meta de hoy ({1})" },
        ["waterDrank"] = new[] { "Bebi um copo", "I drank a glass", "Tomé un vaso" },
        ["waterLeftShort"] = new[] { "Faltam {0}", "{0} to go", "Faltan {0}" },   // aviso curto, na ilha
        ["waterLater"] = new[] { "Agora não", "Not now", "Ahora no" },
        ["waterJustDrank"] = new[] { "Acabei de beber", "I just drank", "Acabo de beber" },
        ["waterNotYet"]    = new[] { "Não bebi :(", "Not yet :(", "Aún no :(" },
        ["webviewError"] = new[]
        {
            "Não foi possível iniciar a interface (WebView2).\n\nVerifique se o \"WebView2 Runtime\" está instalado (vem no Windows 11).",
            "Could not start the interface (WebView2).\n\nCheck that the \"WebView2 Runtime\" is installed (it comes with Windows 11).",
            "No se pudo iniciar la interfaz (WebView2).\n\nVerifica que el \"WebView2 Runtime\" esté instalado (viene con Windows 11)."
        },
    };

    public static string Normalize(string? lang) =>
        Array.IndexOf(Languages, lang) >= 0 ? lang! : "pt";

    public static string Get(string? lang, string key) =>
        Texts[key][Array.IndexOf(Languages, Normalize(lang))];
}

/// <summary>
/// O app da frente está em tela cheia (jogo, vídeo, apresentação)? Usado para a ilha sumir e o lembrete de
/// água esperar. O Volum mesmo confere o tamanho da janela da frente: a resposta "ocupado" do Windows
/// (SHQueryUserNotificationState) também aparece com apps que cobrem a tela com uma janela invisível
/// (ex.: apps de ilha como o Nusa), e aí o lembrete nunca chegava.
/// </summary>
static class Fullscreen
{
    /// <param name="primaryOnly">Só conta se for na tela principal (onde fica a ilha).</param>
    public static bool ForegroundIsFullscreen(bool primaryOnly = false) => ForegroundFullscreenWindow(primaryOnly) != System.IntPtr.Zero;

    /// <summary>A janela da frente, se estiver em tela cheia (senão, zero).</summary>
    public static System.IntPtr ForegroundFullscreenWindow(bool primaryOnly = false)
    {
        var fg = GetForegroundWindow();
        return IsFullscreenWindow(fg, primaryOnly) ? fg : System.IntPtr.Zero;
    }

    /// <summary>Esta janela continua aberta, visível e cobrindo a tela (mesmo sem estar na frente)?</summary>
    public static bool StillFullscreen(System.IntPtr hwnd, bool primaryOnly = false) =>
        hwnd != System.IntPtr.Zero && IsWindow(hwnd) && IsWindowVisible(hwnd) && !IsIconic(hwnd) && IsFullscreenWindow(hwnd, primaryOnly);

    static bool IsFullscreenWindow(System.IntPtr fg, bool primaryOnly)
    {
        if (fg == System.IntPtr.Zero || fg == GetShellWindow() || fg == GetDesktopWindow()) return false;
        var cls = new System.Text.StringBuilder(64);
        GetClassName(fg, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;   // área de trabalho, barra de tarefas
        GetWindowThreadProcessId(fg, out uint pid);
        if (pid == System.Environment.ProcessId) return false;                          // janela do próprio Volum
        if (!GetWindowRect(fg, out var r)) return false;
        var screen = System.Windows.Forms.Screen.FromHandle(fg);
        var b = screen.Bounds;
        bool covers = r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
        return covers && (!primaryOnly || screen.Primary);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern System.IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern System.IntPtr GetShellWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindow(System.IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowVisible(System.IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsIconic(System.IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern System.IntPtr GetDesktopWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(System.IntPtr hwnd, out RECT rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(System.IntPtr hwnd, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int GetClassName(System.IntPtr hwnd, System.Text.StringBuilder name, int max);
}

/// <summary>Só no modo --dev: anota tempos em %TEMP%\Volum-dev.log (para medir a abertura do mixer).</summary>
static class DevLog
{
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static readonly string FilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Volum-dev.log");

    public static void Write(string message)
    {
        if (!Program.DevMode) return;
        try { System.IO.File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} +{Clock.ElapsedMilliseconds,6} ms  {message}\r\n"); }
        catch { }
    }
}

/// <summary>
/// "Tick" bem baixinho ao mexer no volume. O som é gerado por código (sem arquivo .wav)
/// e só é criado no primeiro uso. Para mudar: Volume (0 a 1), FreqHz e DecayMs.
/// </summary>
static class TickSound
{
    const double Volume = 0.06;    // bem sutil (1.0 = volume máximo do arquivo)
    const double FreqHz = 1500;
    const double DecayMs = 7;      // quanto tempo o "tic" demora para sumir

    static System.Media.SoundPlayer? _player;

    public static void Play()
    {
        try
        {
            _player ??= Create();
            _player.Play();        // assíncrono; um tick novo interrompe o anterior
        }
        catch { /* sem dispositivo de áudio: fica em silêncio */ }
    }

    static System.Media.SoundPlayer Create()
    {
        const int rate = 44100;
        int samples = rate * 45 / 1000;                 // 45 ms
        var ms = new System.IO.MemoryStream();
        var w = new System.IO.BinaryWriter(ms);

        // Cabeçalho WAV (PCM 16 bits, mono)
        w.Write("RIFF"u8); w.Write(36 + samples * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(samples * 2);

        for (int i = 0; i < samples; i++)
        {
            double t = i / (double)rate;
            double attack = Math.Min(1, t / 0.001);     // 1 ms de subida (evita estalo)
            double env = attack * Math.Exp(-t * 1000 / DecayMs);
            double s = Math.Sin(2 * Math.PI * FreqHz * t) + 0.3 * Math.Sin(2 * Math.PI * FreqHz * 2 * t);
            w.Write((short)(s / 1.3 * env * Volume * short.MaxValue));
        }

        w.Flush();
        ms.Position = 0;
        var player = new System.Media.SoundPlayer(ms);
        player.Load();
        return player;
    }
}

/// <summary>
/// Som do lembrete de água: duas gotinhas ("plic-ploc"), gerado por código como o TickSound.
/// Cada gota é um tom curto que sobe rápido de altura e some logo, como uma gota caindo na água.
/// </summary>
static class DropSound
{
    const double Volume = 0.16;    // sutil, mas dá para ouvir com música tocando

    static System.Media.SoundPlayer? _player, _one;

    public static void Play()
    {
        try
        {
            _player ??= Create(single: false);
            _player.Play();
        }
        catch { /* sem dispositivo de áudio: fica em silêncio */ }
    }

    /// <summary>Uma gotinha só, mais baixinha: confirma o clique em "+ água" (mixer, ilha e aviso).</summary>
    public static void PlayOne()
    {
        try
        {
            _one ??= Create(single: true);
            _one.Play();
        }
        catch { }
    }

    static System.Media.SoundPlayer Create(bool single)
    {
        const int rate = 44100;
        int samples = rate * (single ? 200 : 360) / 1000;
        var data = new double[samples];
        AddDrop(data, rate, start: 0.000, baseHz: 520, topHz: 1500, gain: single ? 0.75 : 1.0);
        if (!single) AddDrop(data, rate, start: 0.120, baseHz: 640, topHz: 1850, gain: 0.55);

        var ms = new System.IO.MemoryStream();
        var w = new System.IO.BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + samples * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(samples * 2);
        foreach (var s in data) w.Write((short)(Math.Clamp(s, -1, 1) * Volume * short.MaxValue));

        w.Flush();
        ms.Position = 0;
        var player = new System.Media.SoundPlayer(ms);
        player.Load();
        return player;
    }

    // Uma gota: a altura sobe de baseHz até topHz em poucos ms e o volume cai em ~40 ms
    static void AddDrop(double[] data, int rate, double start, double baseHz, double topHz, double gain)
    {
        double phase = 0;
        for (int i = (int)(start * rate); i < data.Length; i++)
        {
            double t = i / (double)rate - start;
            double freq = baseHz + (topHz - baseHz) * (1 - Math.Exp(-t / 0.012));
            phase += 2 * Math.PI * freq / rate;
            double env = Math.Min(1, t / 0.002) * Math.Exp(-t / 0.04);   // 2 ms de subida (sem estalo)
            if (env < 0.0005 && t > 0.05) break;
            data[i] += gain * env * Math.Sin(phase);
        }
    }
}

/// <summary>Gera o ícone da bandeja por código (sem precisar de arquivo .ico).</summary>
static class IconFactory
{
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    public static System.Drawing.Icon Create()
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);

            // Círculo preto com uma borda clarinha (não some na barra de tarefas escura) e o logo branco.
            // Mesmo desenho do Volum.ico, dos ícones da Store e do site (Store\make-assets.ps1).
            using var bg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 9, 9, 9));
            g.FillEllipse(bg, 0, 0, 31, 31);
            using var ring = new System.Drawing.Pen(System.Drawing.Color.FromArgb(70, 255, 255, 255), 1f);
            g.DrawEllipse(ring, 0.5f, 0.5f, 30, 30);
            using var line = new System.Drawing.Pen(System.Drawing.Color.White, 2.5f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            };
            using var knob = new System.Drawing.SolidBrush(System.Drawing.Color.White);

            int[] xs = { 9, 16, 23 };
            int[] ys = { 20, 11, 17 };
            for (int i = 0; i < 3; i++)
            {
                g.DrawLine(line, xs[i], 7, xs[i], 25);
                g.FillEllipse(knob, xs[i] - 3.5f, ys[i] - 3.5f, 7, 7);
            }
        }

        IntPtr h = bmp.GetHicon();
        try
        {
            using var tmp = System.Drawing.Icon.FromHandle(h);
            return (System.Drawing.Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(h);
        }
    }
}
