using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class OrganizationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Crear_una_organizacion_deja_al_creador_como_administrador_y_lo_audita_en_ella()
    {
        string userId = await fixture.NewUserAsync();
        int home = await fixture.NewOrganizationAsync();
        string name = $"Nueva {Guid.NewGuid():N}";
        int created;
        await using (AsyncServiceScope scope = fixture.Scope(home, userId))
        {
            OrganizationService organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();
            created = await organizations.CreateAsync(new CreateOrganizationBody { Name = name }, CancellationToken.None);

            await JobTests.AssertBusinessAsync(409, "ORGANIZATION_NAME_TAKEN",
                () => organizations.CreateAsync(new CreateOrganizationBody { Name = name }, CancellationToken.None));

            List<MembershipItem> memberships = await organizations.MembershipsOfUserAsync(userId, CancellationToken.None);
            MembershipItem membership = Assert.Single(memberships);
            Assert.Equal(created, membership.OrganizationId);
            Assert.Equal(OrganizationRole.Admin, membership.Role);
        }

        await using AsyncServiceScope createdScope = fixture.Scope(created);
        AuditService audit = createdScope.ServiceProvider.GetRequiredService<AuditService>();
        AuditItem entry = Assert.Single((await audit.ListAsync(new PageRequest(), CancellationToken.None)).Items);
        Assert.Equal("organization.create", entry.Action);
    }

    [Fact]
    public async Task Agregar_un_miembro_crea_el_usuario_y_no_permite_duplicarlo()
    {
        int org = await fixture.NewOrganizationAsync();
        int other = await fixture.NewOrganizationAsync();
        string email = $"{Guid.NewGuid():N}@test.local";
        await using (AsyncServiceScope scope = fixture.Scope(org))
        {
            OrganizationService organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();
            await JobTests.AssertBusinessAsync(400, "PASSWORD_REQUIRED",
                () => organizations.AddMemberAsync(new AddMemberBody { Email = email }, CancellationToken.None));
            await JobTests.AssertBusinessAsync(400, "USER_INVALID",
                () => organizations.AddMemberAsync(new AddMemberBody { Email = email, Password = "debil" }, CancellationToken.None));

            AddMemberBody body = new() { Email = email, FullName = "Ana Prueba", Password = "Prueba.2026", Role = OrganizationRole.Editor };
            await organizations.AddMemberAsync(body, CancellationToken.None);
            await JobTests.AssertBusinessAsync(409, "ALREADY_A_MEMBER",
                () => organizations.AddMemberAsync(body, CancellationToken.None));

            MemberItem member = Assert.Single(await organizations.ListMembersAsync(CancellationToken.None));
            Assert.Equal(email, member.Email);
            Assert.Equal(OrganizationRole.Editor, member.Role);
        }

        // El mismo usuario puede sumarse a otra organización sin contraseña; cada una ve sólo sus miembros.
        await using AsyncServiceScope otherScope = fixture.Scope(other);
        OrganizationService otherOrganizations = otherScope.ServiceProvider.GetRequiredService<OrganizationService>();
        await otherOrganizations.AddMemberAsync(new AddMemberBody { Email = email }, CancellationToken.None);
        MemberItem otherMember = Assert.Single(await otherOrganizations.ListMembersAsync(CancellationToken.None));
        Assert.Equal(OrganizationRole.Viewer, otherMember.Role);
    }

    [Fact]
    public async Task Un_usuario_no_puede_activar_una_organizacion_a_la_que_no_pertenece()
    {
        string userId = await fixture.NewUserAsync();
        int foreign = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(0, userId);
        OrganizationService organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();

        await JobTests.AssertBusinessAsync(403, "NOT_A_MEMBER",
            () => organizations.SwitchAsync(userId, foreign, CancellationToken.None));
    }

    [Fact]
    public async Task El_uso_de_IA_se_registra_en_la_organizacion_activa()
    {
        int org = await fixture.NewOrganizationAsync();
        int other = await fixture.NewOrganizationAsync();
        await using (AsyncServiceScope scope = fixture.Scope(org))
        {
            AiUsageService usage = scope.ServiceProvider.GetRequiredService<AiUsageService>();
            await usage.RecordAsync(
                new AiUsageEntry("segment.analysis", "prueba", "modelo-x", "v1", 1200, 300, 0.012345m, 850, null),
                CancellationToken.None);

            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            AiUsageRecord record = await db.AiUsageRecords.AsNoTracking().SingleAsync();
            Assert.Equal(org, record.OrganizationId);
            Assert.Equal(0.012345m, record.CostUsd);
        }

        await using AsyncServiceScope otherScope = fixture.Scope(other);
        Assert.Equal(0, await otherScope.ServiceProvider.GetRequiredService<AppDbContext>().AiUsageRecords.CountAsync());
    }

    [Fact]
    public async Task Guardar_una_entidad_de_negocio_sin_organizacion_activa_falla()
    {
        await using AsyncServiceScope scope = fixture.Scope(0);
        AiUsageService usage = scope.ServiceProvider.GetRequiredService<AiUsageService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => usage.RecordAsync(
            new AiUsageEntry("t", "p", "m", "v1", 1, 1, 0m, 1, null), CancellationToken.None));
    }
}
