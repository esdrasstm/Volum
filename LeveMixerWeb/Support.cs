using System;
using System.Globalization;
using System.Linq;

namespace Volum;

/// <summary>
/// "Apoiar o Volum" (Configurações → Sobre). No Brasil, o botão principal copia o Pix copia e cola
/// (código fixo, sem valor: a pessoa escolhe quanto doar no app do banco); de fora, abre o Ko-fi.
/// </summary>
static class Support
{
    public const string Pix =
        "00020101021126580014BR.GOV.BCB.PIX013601169a5b-bd84-4373-9526-9108e3a33e055204000053039865802BR5912Esdras Silva6009SAO PAULO62080504daqr6304297F";

    // Fusos do Windows que só existem no Brasil
    static readonly string[] BrazilZones =
    {
        "E. South America Standard Time", "Bahia Standard Time", "Tocantins Standard Time", "Central Brazilian Standard Time",
    };

    /// <summary>Região do Windows = Brasil, ou fuso horário brasileiro.</summary>
    public static bool InBrazil
    {
        get
        {
            try { if (RegionInfo.CurrentRegion.TwoLetterISORegionName == "BR") return true; } catch { }
            try { return BrazilZones.Contains(TimeZoneInfo.Local.Id); } catch { return false; }
        }
    }
}
