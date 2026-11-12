using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CreativeIntelligence.Web.Services.Meta;

/// <summary>
/// Lee un informe de anuncios exportado como CSV: una fila por anuncio y día. Reconoce las columnas por su nombre,
/// en español o en inglés, sin importar el orden, las mayúsculas ni los acentos; detecta el separador (coma, punto y
/// coma o tabulación) y entiende números con punto o con coma decimal. Todo son funciones puras.
/// Los nombres de columna aceptados están en <see cref="Columns"/>; un archivo sin las obligatorias no se lee.
/// </summary>
public static partial class MetaCsvParser
{
    /// <summary>Tamaño máximo de un archivo, en bytes.</summary>
    public const long MaxBytes = 10L * 1024 * 1024;

    /// <summary>Columnas que el archivo debe tener, con el nombre con que se le explican a la persona.</summary>
    public static readonly IReadOnlyList<string> Required = ["Identificador del anuncio", "Nombre del anuncio", "Día", "Importe gastado", "Impresiones"];

    /// <summary>Nombres de encabezado aceptados para cada columna, ya normalizados (minúsculas, sin acentos). El
    /// gasto se reconoce por cómo empieza, porque el encabezado lleva la moneda entre paréntesis.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Columns = new Dictionary<string, string[]>
    {
        ["AdId"] = ["identificador del anuncio", "id del anuncio", "ad id"],
        ["AdName"] = ["nombre del anuncio", "ad name"],
        ["Date"] = ["dia", "day", "inicio del informe", "reporting starts", "fecha", "date"],
        ["Spend"] = ["importe gastado", "amount spent"],
        ["Impressions"] = ["impresiones", "impressions"],
        ["Clicks"] = ["clics en el enlace", "link clicks", "clics (todos)", "clicks (all)", "clics", "clicks"],
        ["AccountId"] = ["identificador de la cuenta", "account id"],
        ["AccountName"] = ["nombre de la cuenta", "account name"],
        ["CampaignId"] = ["identificador de la campana", "campaign id"],
        ["CampaignName"] = ["nombre de la campana", "campaign name"],
        ["AdSetId"] = ["identificador del conjunto de anuncios", "ad set id"],
        ["AdSetName"] = ["nombre del conjunto de anuncios", "ad set name"],
        ["VideoPlays"] = ["reproducciones de video de 3 segundos", "3-second video plays"],
        ["ThruPlays"] = ["thruplays", "thruplay"],
        ["Purchases"] = ["compras", "purchases"],
        ["PurchaseValue"] = ["valor de conversion de compras", "purchases conversion value"],
    };

    /// <summary>Lee un informe.</summary>
    /// <param name="text">Contenido del archivo.</param>
    /// <returns>Las filas válidas, las columnas que faltan y por qué se descartó cada fila descartada.</returns>
    public static MetaCsvResult Parse(string text)
    {
        List<List<string>> table = ReadTable(text);
        if (table.Count == 0)
        {
            return new MetaCsvResult([], [.. Required], [], 0, null);
        }

        List<string> headers = table[0].Select(MessageRules.Normalize).ToList();
        Dictionary<string, int> index = [];
        foreach (KeyValuePair<string, string[]> column in Columns)
        {
            // El orden de los alias es la preferencia: "clics en el enlace" antes que "clics (todos)".
            int position = column.Value
                .Select(alias => headers.FindIndex(h => column.Key == "Spend" ? h.StartsWith(alias, StringComparison.Ordinal) : h == alias))
                .FirstOrDefault(p => p >= 0, -1);
            if (position >= 0)
            {
                index[column.Key] = position;
            }
        }

        string[] requiredKeys = ["AdId", "AdName", "Date", "Spend", "Impressions"];
        List<string> missing = requiredKeys.Select((key, i) => index.ContainsKey(key) ? null : Required[i]).OfType<string>().ToList();
        if (missing.Count > 0)
        {
            return new MetaCsvResult([], missing, [], 0, null);
        }

        Match currency = CurrencyInHeader().Match(table[0][index["Spend"]]);
        List<MetaCsvRow> rows = [];
        List<string> problems = [];
        int skipped = 0;
        for (int line = 1; line < table.Count; line++)
        {
            List<string> cells = table[line];
            string adId = Cell(cells, index, "AdId") ?? string.Empty;
            DateOnly? date = ParseDate(Cell(cells, index, "Date"));
            decimal? spend = ParseDecimal(Cell(cells, index, "Spend"));
            decimal? impressions = ParseDecimal(Cell(cells, index, "Impressions"));
            string? problem = adId.Length == 0 ? "no tiene identificador de anuncio (puede ser la fila de totales)"
                : date is null ? "la fecha no se entiende"
                : spend is < 0 || impressions is < 0 ? "tiene un valor negativo"
                : null;
            if (problem is not null)
            {
                skipped++;
                if (problems.Count < 10)
                {
                    problems.Add($"Fila {line + 1}: {problem}.");
                }

                continue;
            }

            rows.Add(new MetaCsvRow
            {
                AdId = adId,
                AdName = Cell(cells, index, "AdName") ?? adId,
                Date = date!.Value,
                Spend = spend ?? 0,
                Impressions = (long)(impressions ?? 0),
                Clicks = (long)(ParseDecimal(Cell(cells, index, "Clicks")) ?? 0),
                AccountId = Cell(cells, index, "AccountId"),
                AccountName = Cell(cells, index, "AccountName"),
                CampaignId = Cell(cells, index, "CampaignId"),
                CampaignName = Cell(cells, index, "CampaignName"),
                AdSetId = Cell(cells, index, "AdSetId"),
                AdSetName = Cell(cells, index, "AdSetName"),
                VideoPlays = (long?)ParseDecimal(Cell(cells, index, "VideoPlays")),
                ThruPlays = (long?)ParseDecimal(Cell(cells, index, "ThruPlays")),
                Purchases = ParseDecimal(Cell(cells, index, "Purchases")),
                PurchaseValue = ParseDecimal(Cell(cells, index, "PurchaseValue")),
            });
        }

        return new MetaCsvResult(rows, [], problems, skipped, currency.Success ? currency.Groups[1].Value.ToUpperInvariant() : null);
    }

    /// <summary>Separa un CSV en filas y celdas. Respeta las comillas (una celda entre comillas puede tener el
    /// separador, saltos de línea y comillas dobles escapadas) y descarta las líneas vacías.</summary>
    /// <param name="text">Contenido del archivo.</param>
    /// <returns>Filas, cada una con sus celdas sin espacios sobrantes.</returns>
    public static List<List<string>> ReadTable(string text)
    {
        string content = text.TrimStart('﻿');
        string firstLine = content.Split('\n', 2)[0];
        char separator = new[] { ',', ';', '\t' }.MaxBy(c => firstLine.Count(x => x == c));
        List<List<string>> table = [];
        List<string> row = [];
        StringBuilder cell = new();
        bool quoted = false;
        for (int i = 0; i < content.Length; i++)
        {
            char character = content[i];
            if (quoted)
            {
                if (character == '"' && i + 1 < content.Length && content[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(character);
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == separator)
            {
                row.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else if (character == '\n')
            {
                row.Add(cell.ToString().Trim());
                cell.Clear();
                if (row.Any(c => c.Length > 0))
                {
                    table.Add(row);
                }

                row = [];
            }
            else if (character != '\r')
            {
                cell.Append(character);
            }
        }

        row.Add(cell.ToString().Trim());
        if (row.Any(c => c.Length > 0))
        {
            table.Add(row);
        }

        return table;
    }

    /// <summary>
    /// Lee un número escrito con punto o con coma decimal, con o sin separador de miles: <c>1234.56</c>,
    /// <c>1,234.56</c>, <c>1.234,56</c>, <c>1234,56</c>. Si sólo hay un separador seguido de exactamente tres
    /// cifras (<c>1,234</c>) se lo toma como de miles, que es el caso habitual en impresiones.
    /// </summary>
    /// <param name="text">Texto de la celda.</param>
    /// <returns>El número, o nulo si la celda está vacía o no es un número.</returns>
    public static decimal? ParseDecimal(string? text)
    {
        string value = (text ?? string.Empty).Replace(" ", string.Empty).Replace(" ", string.Empty).Trim('$', '%');
        if (value.Length == 0)
        {
            return null;
        }

        int dot = value.LastIndexOf('.');
        int comma = value.LastIndexOf(',');
        if (dot >= 0 && comma >= 0)
        {
            // El que aparece último es el decimal; el otro, el de miles.
            value = dot > comma ? value.Replace(",", string.Empty) : value.Replace(".", string.Empty).Replace(',', '.');
        }
        else if (dot >= 0 || comma >= 0)
        {
            char separator = dot >= 0 ? '.' : ',';
            bool thousands = value.Count(c => c == separator) > 1 || value.Length - Math.Max(dot, comma) - 1 == 3;
            value = thousands ? value.Replace(separator.ToString(), string.Empty) : value.Replace(',', '.');
        }

        return decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number)
            ? number
            : null;
    }

    /// <summary>Lee una fecha en formato <c>aaaa-mm-dd</c> o <c>dd/mm/aaaa</c>.</summary>
    /// <param name="text">Texto de la celda.</param>
    /// <returns>La fecha, o nulo si no se entiende.</returns>
    public static DateOnly? ParseDate(string? text)
    {
        return DateOnly.TryParseExact(
            text?.Trim(), ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? date
            : null;
    }

    private static string? Cell(List<string> cells, Dictionary<string, int> index, string column)
    {
        return index.TryGetValue(column, out int position) && position < cells.Count && cells[position].Length > 0 ? cells[position] : null;
    }

    [GeneratedRegex(@"\(([A-Za-z]{3})\)")]
    private static partial Regex CurrencyInHeader();
}
