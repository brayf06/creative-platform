using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class ArchitectureTests(DatabaseFixture fixture)
{
    private static readonly (string Name, Regex Pattern)[] Forbidden =
    [
        ("var", new Regex(@"\bvar\s+\w+\s*(=|in\b)")),
        ("tipo anónimo", new Regex(@"\bnew\s*\{")),
        ("SQL embebido", new Regex(@"FromSql|ExecuteSql|SqlQuery|SqlCommand|Dapper")),
        ("dynamic", new Regex(@"\bdynamic\b")),
        ("catch vacío", new Regex(@"catch\s*(\([^)]*\))?\s*\{\s*\}")),
    ];

    [Fact]
    public void El_codigo_fuente_no_usa_construcciones_prohibidas()
    {
        List<string> violations = [];
        foreach (string file in SourceFiles())
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimStart();
                if (line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach ((string name, Regex pattern) in Forbidden)
                {
                    if (pattern.IsMatch(line))
                    {
                        violations.Add($"{name}: {Path.GetFileName(file)}:{i + 1}");
                    }
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void Los_controllers_no_acceden_a_datos_directamente()
    {
        string[] offenders = SourceFiles()
            .Where(f => f.Contains($"{Path.DirectorySeparatorChar}Controllers{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("DbContext", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public async Task Toda_entidad_de_negocio_pertenece_a_una_organizacion_y_tiene_filtro_global()
    {
        Type[] exempt = [typeof(Organization), typeof(ApplicationUser)];
        await using AsyncServiceScope scope = fixture.Scope(0);
        IModel model = scope.ServiceProvider.GetRequiredService<AppDbContext>().Model;
        List<IEntityType> domainEntities = model.GetEntityTypes()
            .Where(e => e.ClrType.Assembly == typeof(Organization).Assembly && !exempt.Contains(e.ClrType))
            .ToList();

        Assert.NotEmpty(domainEntities);
        Assert.All(domainEntities, e => Assert.True(
            typeof(IOrganizationScoped).IsAssignableFrom(e.ClrType), $"{e.ClrType.Name} no implementa IOrganizationScoped."));
        Assert.All(domainEntities, e => Assert.True(
            e.GetDeclaredQueryFilters().Count > 0, $"{e.ClrType.Name} no tiene filtro multiempresa."));
    }

    [Fact]
    public void Sin_cadena_de_conexion_la_aplicacion_no_arranca()
    {
        IConfiguration empty = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructure(empty));
    }

    private static IEnumerable<string> SourceFiles()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CreativeIntelligence.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        char separator = Path.DirectorySeparatorChar;
        string[] excluded = [$"{separator}obj{separator}", $"{separator}bin{separator}", $"{separator}Migrations{separator}"];
        return Directory.EnumerateFiles(Path.Combine(root.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !excluded.Any(f.Contains));
    }
}
