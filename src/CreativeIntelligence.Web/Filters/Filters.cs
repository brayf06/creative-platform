using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace CreativeIntelligence.Web.Filters;

/// <summary>
/// Convierte un modelo inválido en un <see cref="BusinessException"/> 400 <c>VALIDATION</c> antes de entrar a la
/// acción, para que los controllers no repitan la verificación y el error salga por el mismo camino que los demás.
/// </summary>
public sealed class ValidateModelFilter : IActionFilter
{
    /// <inheritdoc />
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid)
        {
            return;
        }

        string message = string.Join(
            " ",
            context.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Where(m => m.Length > 0));
        throw new BusinessException(400, message.Length > 0 ? message : "Los datos enviados no son válidos.", "VALIDATION");
    }

    /// <inheritdoc />
    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}

/// <summary>
/// Traduce los errores de negocio: JSON con código y mensaje para AJAX; aviso y vuelta a la página de origen para
/// un formulario; página de error en cualquier otro caso. Los errores que no son de negocio siguen de largo.
/// </summary>
/// <param name="tempData">Fábrica de TempData, para el aviso tras la redirección.</param>
/// <param name="metadata">Metadatos de modelo, para construir la vista de error.</param>
public sealed class BusinessExceptionFilter(ITempDataDictionaryFactory tempData, IModelMetadataProvider metadata)
    : IExceptionFilter
{
    /// <inheritdoc />
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not BusinessException exception)
        {
            return;
        }

        HttpRequest request = context.HttpContext.Request;
        string? back = LocalReferer(request);
        if (request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            context.Result = new JsonResult(new ErrorResponse(exception.Code, exception.Message))
            {
                StatusCode = exception.Status,
            };
        }
        else if (HttpMethods.IsPost(request.Method) && back is not null)
        {
            tempData.GetTempData(context.HttpContext)["Error"] = exception.Message;
            context.Result = new LocalRedirectResult(back);
        }
        else
        {
            context.Result = new ViewResult
            {
                ViewName = "Error",
                StatusCode = exception.Status,
                ViewData = new ViewDataDictionary<ErrorViewModel>(metadata, context.ModelState)
                {
                    Model = new ErrorViewModel(exception.Status, exception.Message),
                },
            };
        }

        context.ExceptionHandled = true;
    }

    // Sólo se vuelve a una página de este mismo sitio: el encabezado Referer lo controla el cliente.
    private static string? LocalReferer(HttpRequest request)
    {
        return Uri.TryCreate(request.Headers.Referer, UriKind.Absolute, out Uri? referer)
            && string.Equals(referer.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)
            ? referer.PathAndQuery
            : null;
    }
}
