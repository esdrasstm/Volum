using System;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace Volum;

/// <summary>
/// Pomodoro da ilha: foco, pausa curta e, a cada N focos, pausa longa (tempos nas configurações → Ilha).
/// Só funciona com a ilha ligada. Um único timer, que dispara no fim da fase: nada roda a cada segundo
/// (a ilha calcula a contagem sozinha a partir do tempo que falta).
/// Fim do foco: começa a pausa sozinha e avisa. Fim da pausa: para e pergunta "Focar?".
/// </summary>
static class Pomodoro
{
    public enum Phase { Idle, Focus, Short, Long }

    public static Phase Current { get; private set; } = Phase.Idle;
    public static bool Paused { get; private set; }
    /// <summary>Focos completos no ciclo atual (zera na pausa longa).</summary>
    public static int Done { get; private set; }

    /// <summary>Algo mudou (fase, pausa, tempos): a ilha redesenha.</summary>
    public static event Action? Changed;

    static readonly Timer s_end = new();
    static DateTime s_endsUtc;
    static TimeSpan s_left;      // tempo que falta, quando pausado
    static int s_totalSec;

    static Pomodoro()
    {
        s_end.Tick += (_, _) => { s_end.Stop(); OnEnd(); };
    }

    static AppSettings S => AppSettings.Current;

    public static bool Running => Current != Phase.Idle && !Paused;

    public static TimeSpan Left => Current == Phase.Idle ? TimeSpan.Zero
        : Paused ? s_left : (s_endsUtc - DateTime.UtcNow) is var l && l > TimeSpan.Zero ? l : TimeSpan.Zero;

    public static int TotalSec => Current == Phase.Idle ? S.FocusMin * 60 : s_totalSec;

    public static void Start() => Begin(Phase.Focus);

    public static void Pause()
    {
        if (!Running) return;
        s_left = Left;
        s_end.Stop();
        Paused = true;
        Changed?.Invoke();
    }

    public static void Resume()
    {
        if (Current == Phase.Idle || !Paused) return;
        Paused = false;
        Arm(s_left);
        Changed?.Invoke();
    }

    /// <summary>Pula a fase atual: foco vai para a pausa (conta como feito); pausa vai direto para o foco.</summary>
    public static void Skip()
    {
        if (Current == Phase.Idle) return;
        if (Current == Phase.Focus) EndFocus(notify: false);
        else Begin(Phase.Focus);
    }

    public static void Stop()
    {
        s_end.Stop();
        Current = Phase.Idle;
        Paused = false;
        Done = 0;
        Changed?.Invoke();
    }

    /// <summary>Mudou algum tempo nas configurações: vale a partir da próxima fase.</summary>
    public static void Configure() => Changed?.Invoke();

    static void Begin(Phase p)
    {
        Current = p;
        Paused = false;
        int min = p switch { Phase.Focus => S.FocusMin, Phase.Short => S.ShortBreakMin, _ => S.LongBreakMin };
        s_totalSec = min * 60;
        Arm(TimeSpan.FromSeconds(s_totalSec));
        Changed?.Invoke();
    }

    static void Arm(TimeSpan left)
    {
        s_endsUtc = DateTime.UtcNow + left;
        s_end.Stop();
        s_end.Interval = Math.Max(1, (int)left.TotalMilliseconds);
        s_end.Start();
    }

    static void OnEnd()
    {
        if (Current == Phase.Focus) { EndFocus(notify: true); return; }

        // Fim da pausa: para e pergunta se quer focar de novo
        Current = Phase.Idle;
        Paused = false;
        Changed?.Invoke();
        PomodoroSound.Play(S.PomodoroSound);
        var lang = S.Language;
        IslandForm.Notify("timer", Strings.Get(lang, "pomoDone"), Strings.Get(lang, "pomoDoneText"),
            Strings.Get(lang, "pomoGo"), Start, 30_000);
    }

    static void EndFocus(bool notify)
    {
        Done++;
        bool longBreak = Done >= Math.Max(1, S.LongBreakEvery);
        if (longBreak) Done = 0;
        Begin(longBreak ? Phase.Long : Phase.Short);
        StandUp.BreakStarted();   // se o aviso de levantar estava esperando o foco acabar, sai agora
        if (!notify) return;
        PomodoroSound.Play(S.PomodoroSound);
        var lang = S.Language;
        int min = longBreak ? S.LongBreakMin : S.ShortBreakMin;
        IslandForm.Notify("timer", Strings.Get(lang, longBreak ? "pomoLong" : "pomoBreak"),
            string.Format(Strings.Get(lang, longBreak ? "pomoLongText" : "pomoBreakText"), min), null, null, 15_000);
    }
}

/// <summary>
/// Aviso para levantar e andar (pela ilha), depois de X minutos sentado. Não zera quando a pessoa para de
/// mexer no PC (pode estar vendo um vídeo ou ouvindo música): zera quando bloqueia a tela, o PC dorme
/// ou ela clica "Levantei". Durante um foco do Pomodoro, espera a pausa. Sem clique, lembra de novo em 15 min.
/// Um único timer; desligado (ou sem a ilha), não existe nada rodando.
/// </summary>
static class StandUp
{
    const int SnoozeMin = 15, RetryMin = 5;

    static readonly Timer s_timer = new();
    static DateTime s_sinceUtc;
    static bool s_on, s_pending;

    static StandUp()
    {
        s_timer.Tick += (_, _) => { s_timer.Stop(); Due(); };
        Microsoft.Win32.SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) Reset();   // voltou depois de sair
        };
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume) Reset();                     // o PC dormiu
        };
    }

    /// <summary>Liga/desliga conforme as configurações e a ilha.</summary>
    public static void Apply()
    {
        bool on = AppSettings.Current.StandUpEnabled && IslandForm.IsOn;
        if (on == s_on && on) { Schedule(AppSettings.Current.StandUpMin - Sitting()); return; }   // mudou o tempo
        s_on = on;
        if (on) Reset();
        else { s_timer.Stop(); s_pending = false; }
    }

    static double Sitting() => (DateTime.UtcNow - s_sinceUtc).TotalMinutes;

    static void Reset()
    {
        if (!s_on) return;
        s_sinceUtc = DateTime.UtcNow;
        s_pending = false;
        Schedule(AppSettings.Current.StandUpMin);
    }

    static void Schedule(double minutes)
    {
        s_timer.Stop();
        s_timer.Interval = (int)Math.Max(60_000, minutes * 60_000);
        s_timer.Start();
    }

    static void Due()
    {
        if (!s_on) return;
        if (Pomodoro.Current == Pomodoro.Phase.Focus && !Pomodoro.Paused) { s_pending = true; return; }   // espera a pausa
        Show();
    }

    /// <summary>Começou uma pausa do Pomodoro: se o aviso estava esperando, aparece agora.</summary>
    public static void BreakStarted()
    {
        if (!s_pending) return;
        s_pending = false;
        Show();
    }

    static void Show()
    {
        var lang = AppSettings.Current.Language;
        int min = (int)Sitting();
        string time = min < 60 ? $"{min} min" : min % 60 == 0 ? $"{min / 60} h" : $"{min / 60} h {min % 60:00}";
        bool shown = IslandForm.Notify("walk", Strings.Get(lang, "standTitle"),
            string.Format(Strings.Get(lang, "standText"), time), Strings.Get(lang, "standOk"), Reset, 30_000);
        if (shown) ChimeSound.Play();
        Schedule(shown ? SnoozeMin : RetryMin);   // sem "Levantei", insiste; ilha escondida (tela cheia), tenta logo
    }

    /// <summary>Para testar o visual (configurações → "Testar aviso" do levantar).</summary>
    public static void Test()
    {
        if (!IslandForm.IsOn) return;
        if (!s_on) s_sinceUtc = DateTime.UtcNow.AddMinutes(-AppSettings.Current.StandUpMin);
        Show();
    }
}

/// <summary>Sininho suave de duas notas (gerado por código, como o som da água), para o Pomodoro e o levantar.</summary>
static class ChimeSound
{
    const double Volume = 0.14;
    static System.Media.SoundPlayer? s_player;

    public static void Play()
    {
        try { (s_player ??= Create()).Play(); }
        catch { /* sem dispositivo de áudio: fica em silêncio */ }
    }

    static System.Media.SoundPlayer Create()
    {
        const int rate = 44100;
        int samples = rate * 900 / 1000;
        var data = new double[samples];
        AddTone(data, rate, 0.00, 659.25);   // mi
        AddTone(data, rate, 0.16, 987.77);   // si

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

    // Nota com ataque curto e queda suave, mais um harmônico baixinho (som de sino)
    static void AddTone(double[] data, int rate, double start, double hz)
    {
        for (int i = (int)(start * rate); i < data.Length; i++)
        {
            double t = i / (double)rate - start;
            double env = Math.Min(1, t / 0.006) * Math.Exp(-t * 5.5);
            data[i] += env * (Math.Sin(2 * Math.PI * hz * t) * 0.8 + Math.Sin(2 * Math.PI * hz * 2.01 * t) * 0.2);
        }
    }
}

/// <summary>
/// Som do fim do foco e da pausa do Pomodoro, escolhido nas configurações (Ilha → Pomodoro).
/// Todos curtos e baixinhos, para não assustar: sininho (padrão), gotinhas, campainha suave ou sem som.
/// </summary>
static class PomodoroSound
{
    public static readonly string[] Names = { "chime", "drops", "bell", "none" };

    public static void Play(string? name)
    {
        switch (name)
        {
            case "none": break;
            case "drops": DropSound.Play(); break;
            case "bell": BellSound.Play(); break;
            default: ChimeSound.Play(); break;
        }
    }
}

/// <summary>Campainha suave: duas batidinhas de sino, a segunda mais fraca (gerado por código).</summary>
static class BellSound
{
    const double Volume = 0.12;
    static System.Media.SoundPlayer? s_player;

    public static void Play()
    {
        try { (s_player ??= Create()).Play(); }
        catch { }
    }

    static System.Media.SoundPlayer Create()
    {
        const int rate = 44100;
        int samples = rate * 1400 / 1000;
        var data = new double[samples];
        Ding(data, rate, 0.00, 1.0);
        Ding(data, rate, 0.45, 0.6);

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

    // Sino: fundamental + parciais "inarmônicos" (o que dá o timbre de metal), queda longa e suave
    static void Ding(double[] data, int rate, double start, double gain)
    {
        const double hz = 880;
        for (int i = (int)(start * rate); i < data.Length; i++)
        {
            double t = i / (double)rate - start;
            double env = Math.Min(1, t / 0.004) * Math.Exp(-t * 3.2) * gain;
            data[i] += env * (Math.Sin(2 * Math.PI * hz * t) * 0.6
                            + Math.Sin(2 * Math.PI * hz * 2.76 * t) * 0.25 * Math.Exp(-t * 4)
                            + Math.Sin(2 * Math.PI * hz * 5.4 * t) * 0.1 * Math.Exp(-t * 8));
        }
    }
}
