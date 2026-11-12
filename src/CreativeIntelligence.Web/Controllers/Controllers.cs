using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.Controllers;

/// <summary>Sesión: entrar, salir y cambiar de organización.</summary>
/// <param name="accounts">Servicio de sesión.</param>
public sealed class AccountController(AccountService accounts) : Controller
{
    /// <summary>GET /Account/Login — muestra el formulario de inicio de sesión.</summary>
    /// <param name="returnUrl">Ruta local a la que volver después de entrar.</param>
    /// <response code="200">Formulario.</response>
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    /// <summary>POST /Account/Login — valida las credenciales y abre la sesión.</summary>
    /// <param name="model">Credenciales.</param>
    /// <response code="302">Sesión abierta: va a <c>ReturnUrl</c> si es local, si no al inicio. Con credenciales
    /// inválidas o cuenta bloqueada vuelve al formulario con el aviso.</response>
    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        await accounts.SignInAsync(model);
        return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl : "/");
    }

    /// <summary>POST /Account/Logout — cierra la sesión.</summary>
    /// <response code="302">Va al formulario de inicio de sesión.</response>
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await accounts.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    /// <summary>POST /Account/SwitchOrganization — cambia la organización activa de la sesión.</summary>
    /// <param name="organizationId">Organización elegida.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Va al inicio con la organización nueva; si no es miembro, vuelve con el aviso.</response>
    [HttpPost]
    public async Task<IActionResult> SwitchOrganization(int organizationId, CancellationToken cancellationToken)
    {
        await accounts.SwitchOrganizationAsync(User, organizationId, cancellationToken);
        return LocalRedirect("/");
    }

    /// <summary>GET /Account/AccessDenied — explica que la sesión no tiene permiso para la página pedida.</summary>
    /// <response code="200">Página de acceso denegado.</response>
    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }
}

/// <summary>Inicio y página de error.</summary>
/// <param name="dashboard">Resumen de la organización.</param>
/// <param name="jobs">Trabajos.</param>
public sealed class HomeController(DashboardService dashboard, JobService jobs) : Controller
{
    /// <summary>GET / — inicio de la organización activa: qué hay en cada etapa y el paso siguiente.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Inicio.</response>
    [Authorize(Policy = Policies.OrgMember)]
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(new DashboardViewModel(await dashboard.OverviewAsync(cancellationToken), await jobs.SummaryAsync(cancellationToken)));
    }

    /// <summary>/Home/Error — página de error inesperado (la usa el manejador global fuera de desarrollo).</summary>
    /// <response code="500">Página de error.</response>
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View(new ErrorViewModel(500, "Ocurrió un error inesperado. Quedó registrado."));
    }
}

/// <summary>Trabajos en segundo plano de la organización activa.</summary>
/// <param name="jobs">Trabajos.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class JobsController(JobService jobs) : Controller
{
    /// <summary>GET /Jobs — lista paginada de trabajos; la página se actualiza en vivo por SignalR.</summary>
    /// <param name="paging">Paginación.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Listado.</response>
    [HttpGet]
    public async Task<IActionResult> Index(PageRequest paging, CancellationToken cancellationToken)
    {
        return View(await jobs.ListAsync(paging, cancellationToken));
    }

    /// <summary>POST /Jobs/Diagnostic — encola un trabajo de diagnóstico (AJAX).</summary>
    /// <param name="body">Pasos, espera y fallos simulados.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Trabajo encolado; devuelve su identificador.</response>
    /// <response code="400">Parámetros fuera de rango.</response>
    /// <response code="403">Sólo un administrador de la plataforma lanza trabajos de diagnóstico.</response>
    [Authorize(Policy = Policies.PlatformAdmin)]
    [HttpPost]
    public async Task<IActionResult> Diagnostic(DiagnosticJobBody body, CancellationToken cancellationToken)
    {
        return Json(await jobs.EnqueueDiagnosticAsync(body, cancellationToken));
    }

    /// <summary>POST /Jobs/Cancel/{id} — cancela un trabajo en cola o pide detener uno en curso (AJAX).</summary>
    /// <param name="id">Trabajo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Cancelación registrada.</response>
    /// <response code="404">No existe en la organización activa.</response>
    /// <response code="409">Ya terminó o ya tenía la cancelación pedida.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Cancel(long id, CancellationToken cancellationToken)
    {
        await jobs.CancelAsync(id, cancellationToken);
        return Json(new OkResponse());
    }

    /// <summary>POST /Jobs/Retry/{id} — vuelve a encolar un trabajo fallido o cancelado (AJAX).</summary>
    /// <param name="id">Trabajo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Trabajo reencolado.</response>
    /// <response code="404">No existe en la organización activa.</response>
    /// <response code="409">No está fallido ni cancelado.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Retry(long id, CancellationToken cancellationToken)
    {
        await jobs.RetryAsync(id, cancellationToken);
        return Json(new OkResponse());
    }
}

/// <summary>Organizaciones de la plataforma. Sólo para el administrador de plataforma.</summary>
/// <param name="organizations">Organizaciones.</param>
[Authorize(Policy = Policies.PlatformAdmin)]
public sealed class OrganizationsController(OrganizationService organizations) : Controller
{
    /// <summary>GET /Organizations — lista todas las organizaciones.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Listado con el formulario de alta.</response>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await organizations.ListAsync(cancellationToken));
    }

    /// <summary>POST /Organizations/Create — crea una organización y deja a quien la crea como administrador.</summary>
    /// <param name="body">Nombre.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve al listado, con aviso de éxito o con el error (nombre repetido o inválido).</response>
    [HttpPost]
    public async Task<IActionResult> Create(CreateOrganizationBody body, CancellationToken cancellationToken)
    {
        await organizations.CreateAsync(body, cancellationToken);
        TempData["Ok"] = "Organización creada. Ya podés elegirla en el selector.";
        return RedirectToAction(nameof(Index));
    }
}

/// <summary>Miembros de la organización activa. Sólo para su administrador.</summary>
/// <param name="organizations">Organizaciones.</param>
[Authorize(Policy = Policies.OrgAdmin)]
public sealed class MembersController(OrganizationService organizations) : Controller
{
    /// <summary>GET /Members — lista los miembros de la organización activa.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Listado con el formulario de alta.</response>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await organizations.ListMembersAsync(cancellationToken));
    }

    /// <summary>POST /Members/Create — agrega un miembro; crea el usuario si el email no existe.</summary>
    /// <param name="body">Email, nombre, contraseña inicial y rol.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve al listado, con aviso de éxito o con el error (ya es miembro, falta contraseña,
    /// contraseña débil).</response>
    [HttpPost]
    public async Task<IActionResult> Create(AddMemberBody body, CancellationToken cancellationToken)
    {
        await organizations.AddMemberAsync(body, cancellationToken);
        TempData["Ok"] = "Miembro agregado.";
        return RedirectToAction(nameof(Index));
    }
}

/// <summary>Auditoría de la organización activa. Sólo para su administrador.</summary>
/// <param name="audit">Auditoría.</param>
[Authorize(Policy = Policies.OrgAdmin)]
public sealed class AuditController(AuditService audit) : Controller
{
    /// <summary>GET /Audit — registros de auditoría, del más reciente al más antiguo.</summary>
    /// <param name="paging">Paginación.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Listado paginado.</response>
    [HttpGet]
    public async Task<IActionResult> Index(PageRequest paging, CancellationToken cancellationToken)
    {
        return View(await audit.ListAsync(paging, cancellationToken));
    }
}
