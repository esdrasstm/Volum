using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Volum;

enum ThemeMode { System, Dark, Light }

/// <summary>Configurações salvas em %AppData%\Volum\settings.json</summary>
sealed class AppSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    // Na primeira execução o app se registra para abrir junto com o Windows
    public bool FirstRunDone { get; set; }

    // Modo leve: sem vidro (acrílico), sem animações e medidores mais lentos
    public bool LightMode { get; set; }

    // Tema visual da ilha e do mixer (Configurações → Temas): "default", "christmas" (Natal), "snowy" (Noite de neve) "candy" (Bengala) ou "sleigh" (Trenó); os de inverno são grátis. Separado do claro/escuro.
    public string Skin { get; set; } = "default";

    // Idioma da interface: "pt" (padrão), "en" ou "es"
    public string Language { get; set; } = "pt";

    // Atalho global para abrir o mixer (ex.: no meio de um jogo). Padrão: Ctrl+Alt+V.
    // Mods = soma de GlobalHotkey.Alt/Ctrl/Shift; Key = código da tecla do Windows (VK); Key 0 = desligado.
    public uint HotkeyMods { get; set; } = GlobalHotkey.Ctrl | GlobalHotkey.Alt;
    public uint HotkeyKey { get; set; } = 0x56;   // V

    // Última versão cujas novidades a pessoa já viu (popup "O que tem de novo" no mixer).
    // Vazio em quem atualizou de uma versão sem o popup: essas pessoas veem as novidades.
    public string? LastSeenVersion { get; set; }

    // Última versão em que o aviso "Volum atualizado" (perto da bandeja) já apareceu: aparece uma vez por versão
    public string? UpdateNoticeVersion { get; set; }

    // Ilha (estilo Dynamic Island) no topo da tela: desligada por padrão (fica visível o tempo todo e gasta memória)
    public bool IslandEnabled { get; set; }

    // A 0.3.0 liga a ilha uma única vez (atualização ou instalação nova), para o Pomodoro, o mascote e o
    // aviso de levantar já chegarem funcionando. Se a pessoa desligar depois, não liga de novo.
    public bool IslandAutoOnDone { get; set; }

    // Indicadores na ilha: Caps Lock, Num Lock, volume e microfone mudo (aparecem por ~1,5 s)
    public bool IslandOsd { get; set; } = true;

    // Com o monitor principal em tela cheia (jogo, vídeo), a ilha vai para o topo de outro monitor (se houver)
    public bool IslandOtherScreen { get; set; } = true;
    // Sair da frente dos apps: quando uma janela chega no topo (abas, menus), a ilha some. Desligado: fica sempre à vista
    public bool IslandTuck { get; set; } = true;

    // Esconder a ilha quando não estiver em uso (pedido para telas OLED). Desligado por padrão: o Volum é para a ilha ficar à vista.
    // Some depois de IslandAutoHideSec segundos parada e volta quando o mouse passa no lugar dela; avisos continuam aparecendo.
    public bool IslandAutoHide { get; set; }
    public int IslandAutoHideSec { get; set; } = 10;

    // Monitor da ilha: "primary" (principal, padrão) ou o nome do monitor no Windows (ex.: \\.\DISPLAY2); "other" = o primeiro que não é o principal
    public string IslandMonitor { get; set; } = "primary";

    // Posição da ilha: na horizontal "left" | "center" (padrão) | "right"; na vertical "top" (padrão) | "bottom" (acima da barra de tarefas, sempre flutuante)
    public string IslandPosH { get; set; } = "center";
    public string IslandPosV { get; set; } = "top";
    /// <summary>Formato da ilha: "notch" (grudada na borda de cima, com as orelhas) ou "float" (pílula solta, um pouco abaixo do topo).</summary>
    public string IslandStyle { get; set; } = "notch";
    /// <summary>Notificações dos outros apps na ilha (só na versão da Store; precisa da permissão do Windows).</summary>
    public bool IslandAppNotes { get; set; }
    /// <summary>Endereço iCal (.ics) da agenda (Google Agenda, Outlook). Vazio = sem agenda. É um endereço secreto:
    /// fica salvo protegido pelo Windows (CalendarUrlSafe), que só este usuário, neste PC, consegue abrir.</summary>
    [JsonIgnore]
    public string CalendarUrl { get => Secret.Unprotect(CalendarUrlSafe); set => CalendarUrlSafe = Secret.Protect(value); }
    public string CalendarUrlSafe { get; set; } = "";
    /// <summary>Como era salvo antes (texto puro): lido uma vez, passa para o CalendarUrlSafe e não é mais gravado.</summary>
    [JsonPropertyName("CalendarUrl"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CalendarUrlOld { get => null; set { if (!string.IsNullOrEmpty(value)) CalendarUrl = value; } }

    // Clima na ilha (Open-Meteo): cidade digitada pela pessoa (vazio = desligado), nome achado e coordenadas
    public string WeatherCity { get; set; } = "";
    public string WeatherPlace { get; set; } = "";
    public double WeatherLat { get; set; }
    public double WeatherLon { get; set; }
    // Na ilha fechada: "both" (ícone + temperatura, padrão), "icon" (só o ícone) ou "off"
    public string WeatherShow { get; set; } = "both";
    // Unidade: "auto" (pela região do Windows: °F nos EUA e em poucos outros países), "c" ou "f"
    public string WeatherUnit { get; set; } = "auto";

    // Pomodoro na ilha (só funciona com a ilha ligada): minutos de foco, pausa curta, pausa longa e
    // de quantos em quantos focos vem a pausa longa
    public bool PomodoroEnabled { get; set; } = true;
    public int FocusMin { get; set; } = 25;
    public int ShortBreakMin { get; set; } = 5;
    public int LongBreakMin { get; set; } = 15;
    public int LongBreakEvery { get; set; } = 4;
    // Som do fim do foco e da pausa: "chime" (sininho, padrão), "drops" (gotinhas), "bell" (campainha suave) ou "none"
    public string PomodoroSound { get; set; } = "chime";

    // Aviso para levantar e andar (pela ilha): depois de X minutos sentado
    public bool StandUpEnabled { get; set; } = true;
    public int StandUpMin { get; set; } = 60;

    static string Folder(string app) => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), app);

    /// <summary>Pasta dos dados (configurações e água). A cópia de teste (pasta bin / --dev) usa outra,
    /// para os testes não mexerem na instalada (ex.: marcar as novidades como vistas).</summary>
    public static string DataFolder => Folder(Updater.IsRelease ? "Volum" : "VolumDev");

    static string FilePath => System.IO.Path.Combine(DataFolder, "settings.json");

    // Nome antigo do app: as configurações são trazidas na primeira vez que o Volum abre
    static string OldFilePath => System.IO.Path.Combine(Folder("LeveMixer"), "settings.json");

    /// <summary>As configurações do app, uma só para o mixer e a janela de configurações
    /// (se cada janela tivesse a sua cópia, uma apagaria o que a outra salvou).</summary>
    public static AppSettings Current => s_current ??= Load();
    static AppSettings? s_current;

    public static AppSettings Load()
    {
        try
        {
            var path = File.Exists(FilePath) ? FilePath : OldFilePath;
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings { Language = WindowsLanguage() };   // primeira vez: o idioma do Windows
    }

    /// <summary>Idioma do Windows entre os do Volum: português, espanhol ou, para qualquer outro, inglês.</summary>
    static string WindowsLanguage() =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch { "pt" => "pt", "es" => "es", _ => "en" };

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
