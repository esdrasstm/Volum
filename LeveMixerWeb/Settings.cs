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
        return new AppSettings();
    }

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
