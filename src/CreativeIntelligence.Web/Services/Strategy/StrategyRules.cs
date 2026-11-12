using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CreativeIntelligence.Web.Services.Strategy;

/// <summary>Detección de mensajes prohibidos de una marca dentro de un texto. Función pura.</summary>
public static class MessageRules
{
    /// <summary>
    /// Devuelve las frases prohibidas que aparecen en un texto. La comparación ignora mayúsculas, acentos y espacios
    /// repetidos: "Cura Garantizada" coincide con "cura  garantizada". Compara frases completas tal como están
    /// escritas; no entiende sinónimos ni reformulaciones.
    /// </summary>
    /// <param name="text">Texto a revisar.</param>
    /// <param name="forbidden">Frases prohibidas de la marca.</param>
    /// <returns>Las frases prohibidas encontradas, sin repetir.</returns>
    public static List<string> Violations(string? text, IEnumerable<string> forbidden)
    {
        string normalized = Normalize(text);
        return forbidden
            .Where(f => Normalize(f).Length > 0 && normalized.Contains(Normalize(f), StringComparison.Ordinal))
            .Distinct()
            .ToList();
    }

    /// <summary>Pasa un texto a minúsculas, sin acentos y con un solo espacio entre palabras.</summary>
    /// <param name="text">Texto.</param>
    /// <returns>El texto normalizado.</returns>
    public static string Normalize(string? text)
    {
        StringBuilder result = new();
        foreach (char character in (text ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                result.Append(char.IsWhiteSpace(character) ? ' ' : char.ToLowerInvariant(character));
            }
        }

        return string.Join(' ', result.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>
/// Interpreta el texto libre de un brief y deduce lo que está dicho de forma explícita: cuántos videos, de cuánto,
/// para qué plataforma, para qué ocasión y si pide referencias del mercado. Es determinista, sin IA ni costo, y
/// sólo completa lo evidente; el público, el mensaje y los enfoques quedan para que los escriba la persona o los
/// lea el motor estratégico del texto original.
/// </summary>
public static partial class BriefTextParser
{
    private static readonly (string Pattern, string Name)[] Occasions =
    [
        ("navidad|navide", "Navidad"),
        ("ano nuevo|fin de ano", "Año Nuevo"),
        ("reyes", "Reyes"),
        ("san valentin|dia de los enamorados", "San Valentín"),
        ("dia de la madre|dia de las madres", "Día de la Madre"),
        ("dia del padre", "Día del Padre"),
        ("dia del nino|dia de la ninez", "Día del Niño"),
        ("black friday", "Black Friday"),
        ("cyber monday|cyber lunes", "Cyber Monday"),
        ("hot sale", "Hot Sale"),
        ("vuelta a clases|vuelta al cole", "Vuelta a clases"),
        ("halloween", "Halloween"),
        ("verano", "Verano"),
        ("invierno", "Invierno"),
        ("primavera", "Primavera"),
        ("otono", "Otoño"),
    ];

    /// <summary>Interpreta el texto libre de un brief.</summary>
    /// <param name="text">Lo que escribió la persona.</param>
    /// <returns>Lo que se pudo deducir; los campos que el texto no dice quedan nulos.</returns>
    public static BriefInterpretation Parse(string? text)
    {
        string normalized = MessageRules.Normalize(text);
        List<string> understood = [];

        int? count = null;
        Match countMatch = Count().Match(normalized);
        if (countMatch.Success && int.Parse(countMatch.Groups[1].Value, CultureInfo.InvariantCulture) is >= 1 and <= 50)
        {
            count = int.Parse(countMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            understood.Add($"{count} videos");
        }

        int? duration = null;
        Match durationMatch = Duration().Match(normalized);
        if (durationMatch.Success && int.Parse(durationMatch.Groups[1].Value, CultureInfo.InvariantCulture) is >= 3 and <= 180)
        {
            duration = int.Parse(durationMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            understood.Add($"de {duration} segundos");
        }

        BriefPlatform? platform = Reels().IsMatch(normalized) ? BriefPlatform.MetaReels
            : Stories().IsMatch(normalized) ? BriefPlatform.MetaStories
            : Feed().IsMatch(normalized) ? BriefPlatform.MetaFeed
            : null;
        if (platform is not null)
        {
            understood.Add($"plataforma: {platform}");
        }

        string? occasion = Occasions
            .Where(o => Regex.IsMatch(normalized, $@"\b(?:{o.Pattern})", RegexOptions.CultureInvariant))
            .Select(o => o.Name)
            .FirstOrDefault();
        if (occasion is not null)
        {
            understood.Add($"ocasión: {occasion}");
        }

        bool? research = Research().IsMatch(normalized) ? true : null;
        if (research == true)
        {
            understood.Add("con investigación externa");
        }

        return new BriefInterpretation(count, duration, platform, occasion, research, understood);
    }

    [GeneratedRegex(@"\b(\d{1,3})\s+(?:anuncios?|videos?|creatividades|piezas|variantes|avisos?)\b")]
    private static partial Regex Count();

    [GeneratedRegex(@"\b(\d{1,3})\s*(?:segundos?|segs?)\b")]
    private static partial Regex Duration();

    [GeneratedRegex(@"\breels?\b")]
    private static partial Regex Reels();

    [GeneratedRegex(@"\b(?:stories|historias)\b")]
    private static partial Regex Stories();

    [GeneratedRegex(@"\bfeed\b")]
    private static partial Regex Feed();

    [GeneratedRegex(@"\b(?:referencias (?:actuales )?del mercado|investig\w+|tendencias|competencia|competidores)\b")]
    private static partial Regex Research();
}
