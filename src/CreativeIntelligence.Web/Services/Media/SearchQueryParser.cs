using System.Globalization;
using System.Text.RegularExpressions;

namespace CreativeIntelligence.Web.Services.Media;

/// <summary>
/// Interpreta una consulta en lenguaje natural y separa lo que en realidad son filtros (función, duración,
/// autorización, selección) del texto a buscar. Es determinista, sin IA: reconoce giros concretos del español y,
/// ante la duda, deja la palabra en el texto. Así "hooks de menos de 4 segundos que muestren uñas terminadas" se
/// convierte en función = Hook, duración ≤ 4 s y texto = "muestren uñas terminadas".
/// </summary>
public static partial class SearchQueryParser
{
    // La función sólo se toma si es de lo que trata la consulta: su primera palabra, tras un verbo o un artículo.
    // "problema" en medio de una frase es contenido, no un filtro.
    private static readonly (Regex Pattern, CreativeFunction Function)[] Functions =
    [
        (Leading(@"hooks?|ganchos?"), CreativeFunction.Hook),
        (Leading(@"ctas?|llamad[oa]s?\s+a\s+la\s+acci[oó]n"), CreativeFunction.Cta),
        (Leading(@"testimoni(?:os?|ales?)|pruebas?\s+social(?:es)?|rese[nñ]as?"), CreativeFunction.SocialProof),
        (Leading(@"demostraci(?:[oó]n|ones)|demos?"), CreativeFunction.Demonstration),
        (Leading(@"beneficios?"), CreativeFunction.Benefit),
        (Leading(@"problemas?"), CreativeFunction.Problem),
        (Leading(@"ofertas?|promo(?:ci[oó]n|ciones|s)?"), CreativeFunction.Offer),
        (Leading(@"comparaci(?:[oó]n|ones)|antes\s+y\s+despu[eé]s"), CreativeFunction.Comparison),
        (Leading(@"objeci(?:[oó]n|ones)"), CreativeFunction.Objection),
        (Leading(@"cierres?"), CreativeFunction.Closing),
        (Leading(@"b-?rolls?"), CreativeFunction.Broll),
        (Leading(@"presentaci(?:[oó]n|ones)"), CreativeFunction.Presentation),
    ];

    /// <summary>Interpreta una consulta.</summary>
    /// <param name="query">Lo que escribió la persona.</param>
    /// <returns>El texto que queda para buscar y los filtros deducidos.</returns>
    public static ParsedQuery Parse(string query)
    {
        string text = query.Trim();
        CreativeFunction? function = null;
        foreach ((Regex pattern, CreativeFunction candidate) in Functions)
        {
            Match match = pattern.Match(text);
            if (match.Success)
            {
                function = candidate;
                text = text[match.Length..];
                break;
            }
        }

        decimal? maxDuration = null;
        Match duration = MaxDuration().Match(text);
        if (duration.Success
            && decimal.TryParse(duration.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal seconds)
            && seconds is >= 0.1m and <= 600m)
        {
            maxDuration = seconds;
            text = text.Remove(duration.Index, duration.Length);
        }

        bool onlyAuthorized = Authorized().IsMatch(text);
        text = Authorized().Replace(text, " ");
        bool onlySelected = Selected().IsMatch(text);
        text = Selected().Replace(text, " ");

        // Lo que queda puede empezar con el conector que unía el filtro con el contenido ("… que muestren…").
        text = Spaces().Replace(text, " ").Trim(' ', ',', '.');
        text = LeadingConnector().Replace(text, string.Empty).Trim(' ', ',', '.');

        // "escenas de menos de 3 segundos": si se dedujo algún filtro y sólo queda el sustantivo genérico, no hay
        // nada que buscar por texto.
        bool deduced = function is not null || maxDuration is not null || onlyAuthorized || onlySelected;
        if (deduced && GenericNoun().IsMatch(text))
        {
            text = string.Empty;
        }

        return new ParsedQuery(text, function, maxDuration, onlyAuthorized, onlySelected);
    }

    private static Regex Leading(string words)
    {
        return new Regex(
            @"^(?:(?:buscar|busc[aá]|buscame|quiero|necesito|mostrame|mostrar|dame|ver)\s+)?(?:(?:los|las|unos|unas|un|una|el|la)\s+)?(?:"
            + words + @")\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    [GeneratedRegex(
        @"(?:\bde\s+)?\b(?:menos\s+de|hasta|como\s+m[aá]ximo|m[aá]ximo(?:\s+de)?|no\s+m[aá]s\s+de|menores?\s+(?:a|de)|que\s+no\s+superen?)\s+(\d+(?:[.,]\d+)?)\s*(?:segundos?|segs?\.?|s)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MaxDuration();

    [GeneratedRegex(@"\b(?:con\s+autorizaci[oó]n(?:\s+de\s+(?:re)?uso|\s+de\s+reutilizaci[oó]n)?|autorizad[oa]s?(?:\s+para\s+reutilizar)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Authorized();

    [GeneratedRegex(@"\b(?:(?:seleccionad[oa]s?|elegid[oa]s?)(?:\s+para\s+producci[oó]n)?|para\s+producci[oó]n)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Selected();

    [GeneratedRegex(@"^(?:(?:los|las|todos\s+los|todas\s+las)\s+)?(?:escenas?|fragmentos?|clips?|tomas?|planos?|tramos?|videos?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GenericNoun();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^(?:(?:que|de|con|y|donde|en\s+(?:los|las)\s+que)\s+)+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingConnector();
}
