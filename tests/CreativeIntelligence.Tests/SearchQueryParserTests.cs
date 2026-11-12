namespace CreativeIntelligence.Tests;

/// <summary>El intérprete de consultas es una función pura: separa los filtros escritos en la consulta del texto a buscar.</summary>
public sealed class SearchQueryParserTests
{
    [Theory]
    [InlineData("hooks de menos de 4 segundos que muestren uñas terminadas", CreativeFunction.Hook, 4.0, false, false, "muestren uñas terminadas")]
    [InlineData("Quiero los testimonios autorizados de hasta 10 s", CreativeFunction.SocialProof, 10.0, true, false, "")]
    [InlineData("buscar un CTA para producción", CreativeFunction.Cta, null, false, true, "")]
    [InlineData("b-roll de manos con audio limpio", CreativeFunction.Broll, null, false, false, "manos con audio limpio")]
    [InlineData("demostraciones como máximo 2,5 segundos", CreativeFunction.Demonstration, 2.5, false, false, "")]
    [InlineData("escenas de menos de 2 segundos autorizadas", null, 2.0, true, false, "")]
    [InlineData("escenas de uñas de menos de 2 segundos", null, 2.0, false, false, "escenas de uñas")]
    [InlineData("ganchos seleccionados con autorización de reuso sobre practicidad", CreativeFunction.Hook, null, true, true, "sobre practicidad")]
    public void Los_filtros_escritos_en_la_consulta_se_separan_del_texto(
        string query, CreativeFunction? function, double? maxSeconds, bool authorized, bool selected, string text)
    {
        ParsedQuery parsed = SearchQueryParser.Parse(query);

        Assert.Equal(function, parsed.Function);
        Assert.Equal(maxSeconds is null ? null : (decimal?)Convert.ToDecimal(maxSeconds.Value), parsed.MaxDurationSeconds);
        Assert.Equal(authorized, parsed.OnlyAuthorized);
        Assert.Equal(selected, parsed.OnlySelected);
        Assert.Equal(text, parsed.Text);
    }

    [Theory]
    [InlineData("uñas terminadas que transmitan practicidad")]
    [InlineData("una clienta cuenta su problema con el esmalte")]
    [InlineData("Descripción de la escena 2 Mensaje 2")]
    [InlineData("escena-2")]
    [InlineData("dura menos de 5 minutos en secar")]
    [InlineData("demora 30 segundos")]
    [InlineData("")]
    public void Una_consulta_sin_filtros_reconocibles_queda_igual(string query)
    {
        ParsedQuery parsed = SearchQueryParser.Parse(query);

        Assert.Null(parsed.Function);
        Assert.Null(parsed.MaxDurationSeconds);
        Assert.False(parsed.OnlyAuthorized);
        Assert.False(parsed.OnlySelected);
        Assert.Equal(query, parsed.Text);
    }
}
