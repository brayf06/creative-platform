using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Data;

/// <summary>Usuario y organización iniciales (sección <c>InitialUser</c>). Valores de desarrollo en
/// <c>appsettings.json</c>; producción los sobreescribe.</summary>
public sealed class InitialUserOptions
{
    /// <summary>Email del administrador de plataforma inicial.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Contraseña inicial. Sólo se usa si el usuario todavía no existe.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Nombre para mostrar.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Nombre de la primera organización.</summary>
    public string OrganizationName { get; set; } = string.Empty;

    /// <summary>Si es verdadero, aplica las migraciones pendientes al arrancar la Web.</summary>
    public bool MigrateOnStartup { get; set; }
}

/// <summary>Deja la base lista al arrancar la Web: migraciones, rol de plataforma, usuario y organización iniciales.</summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="users">Gestor de usuarios.</param>
/// <param name="roles">Gestor de roles.</param>
/// <param name="options">Usuario y organización iniciales.</param>
public sealed class DatabaseInitializer(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    IOptions<InitialUserOptions> options)
{
    /// <summary>Rol de Identity del administrador de plataforma: crea organizaciones y las ve todas.</summary>
    public const string PlatformAdminRole = "PlatformAdmin";

    /// <summary>
    /// Aplica migraciones (si está habilitado) y crea lo que falte: el rol de plataforma, el usuario inicial, la
    /// organización inicial y su pertenencia como administrador. Es idempotente: no toca lo que ya existe.
    /// </summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="InvalidOperationException">Si falta configuración de <c>InitialUser</c> o Identity rechaza
    /// el usuario inicial: la aplicación no arranca.</exception>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        InitialUserOptions initial = options.Value;
        if (string.IsNullOrWhiteSpace(initial.Email)
            || string.IsNullOrWhiteSpace(initial.Password)
            || string.IsNullOrWhiteSpace(initial.OrganizationName))
        {
            throw new InvalidOperationException(
                "Falta la configuración obligatoria InitialUser (Email, Password, OrganizationName).");
        }

        if (initial.MigrateOnStartup)
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        if (!await roles.RoleExistsAsync(PlatformAdminRole))
        {
            Ensure(await roles.CreateAsync(new IdentityRole(PlatformAdminRole)));
        }

        ApplicationUser? user = await users.FindByEmailAsync(initial.Email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = initial.Email,
                Email = initial.Email,
                EmailConfirmed = true,
                FullName = initial.FullName,
            };
            Ensure(await users.CreateAsync(user, initial.Password));
            Ensure(await users.AddToRoleAsync(user, PlatformAdminRole));
        }

        Organization? organization = await db.Organizations
            .FirstOrDefaultAsync(o => o.Name == initial.OrganizationName, cancellationToken);
        if (organization is null)
        {
            organization = new Organization { Name = initial.OrganizationName };
            db.Organizations.Add(organization);
            await db.SaveChangesAsync(cancellationToken);
        }

        string userId = user.Id;
        int organizationId = organization.Id;
        bool isMember = await db.OrganizationMembers.IgnoreQueryFilters()
            .AnyAsync(m => m.UserId == userId && m.OrganizationId == organizationId, cancellationToken);
        if (!isMember)
        {
            db.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = organizationId,
                UserId = userId,
                Role = OrganizationRole.Admin,
            });
            user.LastOrganizationId ??= organizationId;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "No se pudo crear el usuario inicial: " + string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }
}
