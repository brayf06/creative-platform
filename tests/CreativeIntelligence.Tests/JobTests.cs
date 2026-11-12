using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class JobTests(DatabaseFixture fixture)
{
    private static readonly PageRequest FirstPage = new();

    [Fact]
    public async Task Encolar_dos_veces_la_misma_clave_crea_un_solo_trabajo()
    {
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        JobService jobs = scope.ServiceProvider.GetRequiredService<JobService>();

        EnqueueResult first = await jobs.EnqueueAsync("x.test", "x:1:v1", "{}", CancellationToken.None);
        EnqueueResult second = await jobs.EnqueueAsync("x.test", "x:1:v1", "{}", CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, (await jobs.ListAsync(FirstPage, CancellationToken.None)).Total);
    }

    [Fact]
    public async Task Un_trabajo_no_es_visible_ni_operable_desde_otra_organizacion()
    {
        int orgA = await fixture.NewOrganizationAsync();
        int orgB = await fixture.NewOrganizationAsync();
        long id = await EnqueueDiagnosticAsync(orgA, new DiagnosticJobBody());

        await using AsyncServiceScope scopeB = fixture.Scope(orgB);
        JobService jobsB = scopeB.ServiceProvider.GetRequiredService<JobService>();
        Assert.Equal(0, (await jobsB.ListAsync(FirstPage, CancellationToken.None)).Total);
        Assert.Equal(0, (await jobsB.SummaryAsync(CancellationToken.None)).Pending);
        await AssertBusinessAsync(404, "JOB_NOT_FOUND", () => jobsB.GetAsync(id, CancellationToken.None));
        await AssertBusinessAsync(404, "JOB_NOT_FOUND", () => jobsB.CancelAsync(id, CancellationToken.None));
        await AssertBusinessAsync(404, "JOB_NOT_FOUND", () => jobsB.RetryAsync(id, CancellationToken.None));

        await using AsyncServiceScope noOrg = fixture.Scope(0);
        JobService jobsNone = noOrg.ServiceProvider.GetRequiredService<JobService>();
        Assert.Equal(0, (await jobsNone.ListAsync(FirstPage, CancellationToken.None)).Total);
        Assert.Equal(BackgroundJobStatus.Pending, (await GetAsync(orgA, id)).Status);
    }

    [Fact]
    public async Task Dos_workers_en_paralelo_no_toman_el_mismo_trabajo()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        await EnqueueDiagnosticAsync(org, new DiagnosticJobBody());

        await using AsyncServiceScope scope1 = fixture.Scope(0);
        await using AsyncServiceScope scope2 = fixture.Scope(0);
        ClaimedJob?[] claimed = await Task.WhenAll(
            scope1.ServiceProvider.GetRequiredService<JobQueue>().ClaimNextAsync("w1", CancellationToken.None),
            scope2.ServiceProvider.GetRequiredService<JobQueue>().ClaimNextAsync("w2", CancellationToken.None));

        Assert.Single(claimed, c => c is not null);
    }

    [Fact]
    public async Task Un_trabajo_con_el_arriendo_vencido_lo_retoma_otro_worker()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        long id = await EnqueueDiagnosticAsync(org, new DiagnosticJobBody());

        await using AsyncServiceScope scope = fixture.Scope(0);
        JobQueue queue = scope.ServiceProvider.GetRequiredService<JobQueue>();
        Assert.NotNull(await queue.ClaimNextAsync("w1", CancellationToken.None));
        Assert.Null(await queue.ClaimNextAsync("w2", CancellationToken.None));

        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.BackgroundJobs.IgnoreQueryFilters().Where(j => j.Id == id).ExecuteUpdateAsync(
            s => s.SetProperty(j => j.LeaseExpiresAt, j => (DateTime?)AppDbContext.UtcNow().AddSeconds(-1)));

        ClaimedJob? reclaimed = await queue.ClaimNextAsync("w2", CancellationToken.None);
        Assert.NotNull(reclaimed);
        Assert.Equal(id, reclaimed.Id);
        Assert.Equal(2, reclaimed.Attempts);

        // El worker original perdió el arriendo: su latido le dice que se detenga y no puede cerrar el trabajo.
        Assert.True(await queue.HeartbeatAsync(id, "w1", CancellationToken.None));
        await queue.CompleteAsync(id, "w1", CancellationToken.None);
        Assert.Equal(BackgroundJobStatus.Running, (await GetAsync(org, id)).Status);
    }

    [Fact]
    public async Task El_runner_ejecuta_el_trabajo_y_lo_deja_terminado_con_progreso_100()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        long id = await EnqueueDiagnosticAsync(org, new DiagnosticJobBody { Steps = 3, StepDelayMs = 0 });
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();

        Assert.True(await runner.ProcessNextAsync(CancellationToken.None));
        Assert.False(await runner.ProcessNextAsync(CancellationToken.None));

        JobItem job = await GetAsync(org, id);
        Assert.Equal(BackgroundJobStatus.Succeeded, job.Status);
        Assert.Equal(100, job.ProgressPercent);
        Assert.Equal("Paso 3 de 3", job.ProgressStep);
        Assert.Equal(1, job.Attempts);
        Assert.NotNull(job.FinishedAt);
    }

    [Fact]
    public async Task Un_fallo_se_reintenta_y_el_trabajo_termina_bien_en_el_segundo_intento()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        long id = await EnqueueDiagnosticAsync(org, new DiagnosticJobBody { Steps = 2, StepDelayMs = 0, FailFirstAttempts = 1 });
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();

        await runner.ProcessNextAsync(CancellationToken.None);
        JobItem afterFirst = await GetAsync(org, id);
        Assert.Equal(BackgroundJobStatus.Pending, afterFirst.Status);
        Assert.Equal("Fallo simulado en el intento 1.", afterFirst.Error);

        await runner.ProcessNextAsync(CancellationToken.None);
        JobItem afterSecond = await GetAsync(org, id);
        Assert.Equal(BackgroundJobStatus.Succeeded, afterSecond.Status);
        Assert.Equal(2, afterSecond.Attempts);
        Assert.Null(afterSecond.Error);
    }

    [Fact]
    public async Task Al_agotar_los_intentos_el_trabajo_queda_fallido_con_el_error()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        long id = await EnqueueDiagnosticAsync(org, new DiagnosticJobBody { Steps = 2, StepDelayMs = 0, FailFirstAttempts = 10 });
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();

        for (int i = 0; i < 3; i++)
        {
            Assert.True(await runner.ProcessNextAsync(CancellationToken.None));
        }

        Assert.False(await runner.ProcessNextAsync(CancellationToken.None));
        JobItem job = await GetAsync(org, id);
        Assert.Equal(BackgroundJobStatus.Failed, job.Status);
        Assert.Equal(3, job.Attempts);
        Assert.Equal("Fallo simulado en el intento 3.", job.Error);
    }

    [Fact]
    public async Task Un_tipo_sin_manejador_falla_sin_reintentar()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        long id;
        await using (AsyncServiceScope scope = fixture.Scope(org))
        {
            JobService jobs = scope.ServiceProvider.GetRequiredService<JobService>();
            id = (await jobs.EnqueueAsync("no.existe", "no.existe:1", "{}", CancellationToken.None)).Id;
        }

        await fixture.Services.GetRequiredService<JobRunner>().ProcessNextAsync(CancellationToken.None);

        JobItem job = await GetAsync(org, id);
        Assert.Equal(BackgroundJobStatus.Failed, job.Status);
        Assert.Equal(1, job.Attempts);
    }

    [Fact]
    public async Task Cancelar_un_trabajo_en_cola_lo_deja_cancelado_y_no_se_puede_cancelar_dos_veces()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        long id = await EnqueueDiagnosticAsync(org, new DiagnosticJobBody());

        await using AsyncServiceScope scope = fixture.Scope(org);
        JobService jobs = scope.ServiceProvider.GetRequiredService<JobService>();
        await jobs.CancelAsync(id, CancellationToken.None);

        Assert.Equal(BackgroundJobStatus.Cancelled, (await jobs.GetAsync(id, CancellationToken.None)).Status);
        await AssertBusinessAsync(409, "JOB_NOT_CANCELLABLE", () => jobs.CancelAsync(id, CancellationToken.None));
        Assert.False(await fixture.Services.GetRequiredService<JobRunner>().ProcessNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Cancelar_un_trabajo_en_curso_detiene_al_manejador()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        long id = await EnqueueDiagnosticAsync(org, new DiagnosticJobBody { Steps = 60, StepDelayMs = 200 });
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();

        Task<bool> running = runner.ProcessNextAsync(CancellationToken.None);
        await WaitUntilAsync(async () => (await GetAsync(org, id)).ProgressPercent > 0);

        await using (AsyncServiceScope scope = fixture.Scope(org))
        {
            await scope.ServiceProvider.GetRequiredService<JobService>().CancelAsync(id, CancellationToken.None);
        }

        Assert.True(await running.WaitAsync(TimeSpan.FromSeconds(10)));
        JobItem job = await GetAsync(org, id);
        Assert.Equal(BackgroundJobStatus.Cancelled, job.Status);
        Assert.InRange(job.ProgressPercent, 1, 99);
    }

    [Fact]
    public async Task Reintentar_un_trabajo_fallido_lo_vuelve_a_la_cola_con_intentos_en_cero()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        long id = await EnqueueDiagnosticAsync(org, new DiagnosticJobBody());

        await using AsyncServiceScope scope = fixture.Scope(org);
        JobService jobs = scope.ServiceProvider.GetRequiredService<JobService>();
        await AssertBusinessAsync(409, "JOB_NOT_RETRYABLE", () => jobs.RetryAsync(id, CancellationToken.None));

        await jobs.CancelAsync(id, CancellationToken.None);
        await jobs.RetryAsync(id, CancellationToken.None);

        JobItem job = await jobs.GetAsync(id, CancellationToken.None);
        Assert.Equal(BackgroundJobStatus.Pending, job.Status);
        Assert.Equal(0, job.Attempts);
        Assert.Null(job.FinishedAt);
    }

    [Fact]
    public async Task Encolar_cancelar_y_reintentar_dejan_auditoria_solo_en_su_organizacion()
    {
        int org = await fixture.NewOrganizationAsync();
        int other = await fixture.NewOrganizationAsync();
        await using (AsyncServiceScope scope = fixture.Scope(org))
        {
            JobService jobs = scope.ServiceProvider.GetRequiredService<JobService>();
            long id = (await jobs.EnqueueAsync("x.test", "audit:1", "{}", CancellationToken.None)).Id;
            await jobs.CancelAsync(id, CancellationToken.None);
            await jobs.RetryAsync(id, CancellationToken.None);

            AuditService audit = scope.ServiceProvider.GetRequiredService<AuditService>();
            PagedResult<AuditItem> entries = await audit.ListAsync(FirstPage, CancellationToken.None);
            Assert.Equal(["job.retry", "job.cancel", "job.enqueue"], entries.Items.Select(e => e.Action));
            Assert.All(entries.Items, e => Assert.Equal(id.ToString(), e.EntityId));
            Assert.All(entries.Items, e => Assert.Equal("tester", e.UserName));
        }

        await using AsyncServiceScope otherScope = fixture.Scope(other);
        AuditService otherAudit = otherScope.ServiceProvider.GetRequiredService<AuditService>();
        Assert.Equal(0, (await otherAudit.ListAsync(FirstPage, CancellationToken.None)).Total);
    }

    private async Task<long> EnqueueDiagnosticAsync(int organizationId, DiagnosticJobBody body)
    {
        await using AsyncServiceScope scope = fixture.Scope(organizationId);
        JobService jobs = scope.ServiceProvider.GetRequiredService<JobService>();
        return (await jobs.EnqueueDiagnosticAsync(body, CancellationToken.None)).Id;
    }

    private async Task<JobItem> GetAsync(int organizationId, long id)
    {
        await using AsyncServiceScope scope = fixture.Scope(organizationId);
        return await scope.ServiceProvider.GetRequiredService<JobService>().GetAsync(id, CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "La condición no se cumplió en 10 segundos.");
            await Task.Delay(50);
        }
    }

    internal static async Task AssertBusinessAsync(int status, string code, Func<Task> action)
    {
        BusinessException exception = await Assert.ThrowsAsync<BusinessException>(action);
        Assert.Equal(status, exception.Status);
        Assert.Equal(code, exception.Code);
    }
}
