using System.Globalization;
using System.Text;

namespace CreativeIntelligence.Web.Services.Composition;

/// <summary>
/// Arma los textos que se queman en un video generado: los subtítulos (lo que se dice en cada tramo, llevado a los
/// tiempos del montaje) y los textos sobre el video. Todo son funciones puras; el resultado es un archivo ASS, el
/// formato de subtítulos que FFmpeg sabe dibujar.
/// </summary>
public static class SubtitleBuilder
{
    /// <summary>Cuánto dura un texto de apertura o de cierre, en milisegundos.</summary>
    public const int MomentMs = 3000;

    /// <summary>Un subtítulo más corto que esto no se muestra: no llega a leerse.</summary>
    public const int MinLineMs = 200;

    /// <summary>
    /// Lleva las frases transcriptas de cada original a los tiempos del montaje. De cada tramo entran las frases
    /// cuyo punto medio cae dentro del tramo (la misma regla con la que la biblioteca muestra lo que se dice en una
    /// escena), recortadas a los límites del tramo.
    /// </summary>
    /// <param name="clips">Tramos del montaje, en orden, cada uno con las frases de su original.</param>
    /// <returns>Subtítulos con tiempos del montaje, en orden.</returns>
    public static List<TimedText> FromTranscript(IReadOnlyList<ClipSpeech> clips)
    {
        List<TimedText> result = [];
        int offset = 0;
        foreach (ClipSpeech clip in clips)
        {
            foreach (TranscriptLineItem line in clip.Lines.OrderBy(l => l.StartMs))
            {
                int middle = (line.StartMs + line.EndMs) / 2;
                if (middle < clip.StartMs || middle >= clip.EndMs)
                {
                    continue;
                }

                int start = offset + Math.Max(line.StartMs, clip.StartMs) - clip.StartMs;
                int end = offset + Math.Min(line.EndMs, clip.EndMs) - clip.StartMs;
                if (end - start >= MinLineMs && !string.IsNullOrWhiteSpace(line.Text))
                {
                    result.Add(new TimedText(start, end, line.Text.Trim(), null));
                }
            }

            offset += clip.EndMs - clip.StartMs;
        }

        return result;
    }

    /// <summary>Convierte los textos de una receta en textos con tiempos: todo el video, sus primeros segundos o
    /// los últimos. En un video más corto que <see cref="MomentMs"/> la apertura y el cierre ocupan todo.</summary>
    /// <param name="totalMs">Duración del montaje.</param>
    /// <param name="overlays">Textos de la receta.</param>
    /// <returns>Textos con tiempos del montaje.</returns>
    public static List<TimedText> FromOverlays(int totalMs, IReadOnlyList<OverlayView> overlays)
    {
        return overlays
            .Select(o => o.Moment switch
            {
                OverlayMoment.Opening => new TimedText(0, Math.Min(MomentMs, totalMs), o.Text, o.Placement),
                OverlayMoment.Closing => new TimedText(Math.Max(0, totalMs - MomentMs), totalMs, o.Text, o.Placement),
                _ => new TimedText(0, totalMs, o.Text, o.Placement),
            })
            .ToList();
    }

    /// <summary>Escribe el archivo ASS con los textos. Los tamaños son proporcionales al alto del video, para que
    /// se vean igual en cualquier formato.</summary>
    /// <param name="width">Ancho del video.</param>
    /// <param name="height">Alto del video.</param>
    /// <param name="texts">Subtítulos y textos con sus tiempos.</param>
    /// <returns>Contenido del archivo.</returns>
    public static string BuildAss(int width, int height, IReadOnlyList<TimedText> texts)
    {
        int subtitleSize = height / 26;
        int overlaySize = height / 18;
        int outline = Math.Max(2, height / 400);
        int side = width / 14;
        const string format =
            "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, "
            + "StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding";
        StringBuilder ass = new();
        ass.Append("[Script Info]\nScriptType: v4.00+\nWrapStyle: 0\nScaledBorderAndShadow: yes\n");
        ass.Append(CultureInfo.InvariantCulture, $"PlayResX: {width}\nPlayResY: {height}\n\n[V4+ Styles]\n{format}\n");

        // Alineación ASS: 2 abajo al centro, 5 centro, 8 arriba al centro.
        ass.Append(Style("Sub", subtitleSize, outline, 2, side, height / 12));
        ass.Append(Style("Top", overlaySize, outline, 8, side, height / 10));
        ass.Append(Style("Center", overlaySize, outline, 5, side, 0));
        ass.Append(Style("Bottom", overlaySize, outline, 2, side, height / 4));
        ass.Append("\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n");
        foreach (TimedText text in texts)
        {
            ass.Append(CultureInfo.InvariantCulture,
                $"Dialogue: 0,{Time(text.StartMs)},{Time(text.EndMs)},{text.Placement?.ToString() ?? "Sub"},,0,0,0,,{Escape(text.Text)}\n");
        }

        return ass.ToString();
    }

    private static string Style(string name, int size, int outline, int alignment, int side, int vertical)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Style: {name},Arial,{size},&H00FFFFFF,&H000000FF,&H00000000,&H80000000,-1,0,0,0,100,100,0,0,1,{outline},1,{alignment},{side},{side},{vertical},1\n");
    }

    // Tiempo ASS: horas:minutos:segundos.centésimas.
    private static string Time(int milliseconds)
    {
        int centiseconds = milliseconds / 10;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{centiseconds / 360000}:{centiseconds / 6000 % 60:00}:{centiseconds / 100 % 60:00}.{centiseconds % 100:00}");
    }

    // En ASS las llaves abren órdenes de formato y la barra invertida, escapes: un texto de usuario no debe poder
    // escribir ninguna de las dos cosas.
    private static string Escape(string text)
    {
        return text.Replace('{', '(').Replace('}', ')').Replace('\\', '/').Replace("\r", string.Empty).Replace("\n", "\\N");
    }
}
