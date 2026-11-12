using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;

// Los números no dependen del idioma del sistema operativo. Un campo numérico del navegador envía siempre punto
// decimal; con un servidor en español "42.5" se leía como 425. Con cultura invariante se lee y se escribe con punto.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication(builder.Configuration);
builder.Services.Configure<InitialUserOptions>(builder.Configuration.GetSection("InitialUser"));
builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddScoped<OrganizationService>();
builder.Services.AddScoped<AccountService>();

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(o => o.User.RequireUniqueEmail = true)
    .AddEntityFrameworkStores<AppDbContext>()
    .AddClaimsPrincipalFactory<OrganizationClaimsFactory>();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.OrgMember, p => p.RequireClaim(OrgClaims.OrganizationId))
    .AddPolicy(Policies.OrgEditor, p => p.RequireClaim(
        OrgClaims.Role, nameof(OrganizationRole.Admin), nameof(OrganizationRole.Editor)))
    .AddPolicy(Policies.OrgAdmin, p => p.RequireClaim(OrgClaims.Role, nameof(OrganizationRole.Admin)))
    .AddPolicy(Policies.PlatformAdmin, p => p.RequireRole(DatabaseInitializer.PlatformAdminRole));

builder.Services.AddControllersWithViews(o =>
{
    // Todo requiere sesión salvo lo marcado [AllowAnonymous]; todo POST valida antiforgery.
    o.Filters.Add(new AuthorizeFilter());
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    o.Filters.Add<ValidateModelFilter>();
    o.Filters.Add<BusinessExceptionFilter>();
});
builder.Services.AddSignalR();
builder.Services.AddHostedService<JobProgressNotifier>();

// Los trabajos en segundo plano corren dentro de este mismo proceso: el bucle que ejecuta la cola y el programador
// de carpetas vigiladas. FFmpeg, PySceneDetect y Whisper siguen siendo procesos externos que se lanzan desde acá.
builder.Services.AddHostedService<JobProcessor>();
builder.Services.AddHostedService<FolderScanScheduler>();
builder.Services.AddSingleton<AutoAnalysisScheduler>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<AutoAnalysisScheduler>());

WebApplication app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().RunAsync(CancellationToken.None);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<CurrentContextMiddleware>();

app.MapStaticAssets();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
app.MapHub<JobsHub>(JobsHub.Path);

app.Run();
