using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Organizations;

/// <summary>Organizaciones y sus miembros.</summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="users">Gestor de usuarios de Identity.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
public sealed class OrganizationService(
    AppDbContext db, UserManager<ApplicationUser> users, AuditService audit, CurrentContext current)
{
    /// <summary>Lista todas las organizaciones de la plataforma. Sólo para el administrador de plataforma. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Organizaciones ordenadas por nombre.</returns>
    public async Task<List<OrganizationItem>> ListAsync(CancellationToken cancellationToken)
    {
        return await db.Organizations.AsNoTracking()
            .OrderBy(o => o.Name)
            .Select(o => new OrganizationItem(
                o.Id,
                o.Name,
                o.IsActive,
                db.OrganizationMembers.IgnoreQueryFilters().Count(m => m.OrganizationId == o.Id),
                o.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Crea una organización y deja a quien la crea como administrador de ella. Escribe la organización, la
    /// pertenencia y la auditoría (en la organización nueva) en una transacción.
    /// </summary>
    /// <param name="body">Datos de la organización.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador de la organización creada.</returns>
    /// <exception cref="BusinessException">409 <c>ORGANIZATION_NAME_TAKEN</c> si ya existe una con ese nombre.</exception>
    public async Task<int> CreateAsync(CreateOrganizationBody body, CancellationToken cancellationToken)
    {
        string name = body.Name.Trim();
        if (await db.Organizations.AnyAsync(o => o.Name == name, cancellationToken))
        {
            throw new BusinessException(409, "Ya existe una organización con ese nombre.", "ORGANIZATION_NAME_TAKEN");
        }

        Organization organization = new() { Name = name };
        return await db.InTransactionAsync(
            async () =>
            {
                db.Organizations.Add(organization);
                await db.SaveChangesAsync(cancellationToken);
                if (current.UserId is not null)
                {
                    db.OrganizationMembers.Add(new OrganizationMember
                    {
                        OrganizationId = organization.Id,
                        UserId = current.UserId,
                        Role = OrganizationRole.Admin,
                    });
                }

                audit.Record(
                    "organization.create", nameof(Organization), organization.Id.ToString(),
                    $"Creó la organización {name}.", organization.Id);
                await db.SaveChangesAsync(cancellationToken);
                return organization.Id;
            },
            cancellationToken);
    }

    /// <summary>
    /// Organizaciones activas a las que pertenece un usuario. Cruza organizaciones a propósito: se usa al iniciar
    /// sesión y en el selector, antes de que haya una organización activa. No escribe.
    /// </summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Pertenencias activas, ordenadas por nombre de organización.</returns>
    public async Task<List<MembershipItem>> MembershipsOfUserAsync(string userId, CancellationToken cancellationToken)
    {
        return await db.OrganizationMembers.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.UserId == userId && m.IsActive && m.Organization!.IsActive)
            .OrderBy(m => m.Organization!.Name)
            .Select(m => new MembershipItem(m.OrganizationId, m.Organization!.Name, m.Role))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Cambia la organización activa de un usuario: guarda la elección en <c>LastOrganizationId</c>. Quien llama
    /// debe renovar la sesión para que los claims la reflejen.
    /// </summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="organizationId">Organización elegida.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">403 <c>NOT_A_MEMBER</c> si el usuario no es miembro activo de esa
    /// organización.</exception>
    public async Task SwitchAsync(string userId, int organizationId, CancellationToken cancellationToken)
    {
        bool isMember = await db.OrganizationMembers.IgnoreQueryFilters().AnyAsync(
            m => m.UserId == userId && m.OrganizationId == organizationId && m.IsActive && m.Organization!.IsActive,
            cancellationToken);
        if (!isMember)
        {
            throw new BusinessException(403, "No pertenecés a esa organización.", "NOT_A_MEMBER");
        }

        ApplicationUser user = await db.Users.FirstAsync(u => u.Id == userId, cancellationToken);
        user.LastOrganizationId = organizationId;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Lista los miembros de la organización activa. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Miembros ordenados por email.</returns>
    public async Task<List<MemberItem>> ListMembersAsync(CancellationToken cancellationToken)
    {
        return await db.OrganizationMembers.AsNoTracking()
            .OrderBy(m => m.User!.Email)
            .Select(m => new MemberItem(m.Id, m.User!.Email!, m.User!.FullName, m.Role, m.IsActive, m.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Agrega un miembro a la organización activa. Si el email no existe en la plataforma crea el usuario con la
    /// contraseña indicada. Escribe el usuario (si es nuevo), la pertenencia y la auditoría en una transacción.
    /// </summary>
    /// <param name="body">Email, nombre, contraseña inicial y rol.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador de la pertenencia creada.</returns>
    /// <exception cref="BusinessException">400 <c>PASSWORD_REQUIRED</c> si el usuario no existe y no se indicó
    /// contraseña; 400 <c>USER_INVALID</c> si Identity rechaza los datos (por ejemplo, contraseña débil);
    /// 409 <c>ALREADY_A_MEMBER</c> si ya pertenece a la organización.</exception>
    public Task<int> AddMemberAsync(AddMemberBody body, CancellationToken cancellationToken)
    {
        string email = body.Email.Trim();
        return db.InTransactionAsync(
            async () =>
            {
                ApplicationUser? user = await users.FindByEmailAsync(email);
                if (user is null)
                {
                    if (string.IsNullOrWhiteSpace(body.Password))
                    {
                        throw new BusinessException(400, "El usuario no existe: indicá una contraseña inicial.", "PASSWORD_REQUIRED");
                    }

                    user = new ApplicationUser
                    {
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true,
                        FullName = body.FullName?.Trim() ?? string.Empty,
                    };
                    IdentityResult result = await users.CreateAsync(user, body.Password);
                    if (!result.Succeeded)
                    {
                        throw new BusinessException(400, string.Join(" ", result.Errors.Select(e => e.Description)), "USER_INVALID");
                    }
                }

                string userId = user.Id;
                if (await db.OrganizationMembers.AnyAsync(m => m.UserId == userId, cancellationToken))
                {
                    throw new BusinessException(409, "Ese usuario ya es miembro de la organización.", "ALREADY_A_MEMBER");
                }

                OrganizationMember member = new() { UserId = userId, Role = body.Role };
                db.OrganizationMembers.Add(member);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record(
                    "member.add", nameof(OrganizationMember), member.Id.ToString(),
                    $"Agregó a {email} con rol {body.Role}.");
                await db.SaveChangesAsync(cancellationToken);
                return member.Id;
            },
            cancellationToken);
    }
}
