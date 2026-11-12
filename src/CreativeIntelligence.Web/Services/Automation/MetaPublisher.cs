namespace CreativeIntelligence.Web.Services.Automation;

/// <summary>
/// Publicador de anuncios en Meta: el único lugar por donde la plataforma escribe en una cuenta publicitaria. Es
/// un límite externo. Hoy no hay implementación contra la Marketing API (faltan la app y el token); los tests usan
/// una falsa que cuenta las llamadas.
/// </summary>
public interface IMetaPublisher
{
    /// <summary>Si hay conexión con Meta para publicar.</summary>
    bool IsConfigured { get; }

    /// <summary>Crea un anuncio con un video en un conjunto. Debe ser idempotente por
    /// <see cref="MetaPublishRequest.IdempotencyKey"/>.</summary>
    /// <param name="request">Qué publicar y dónde.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El identificador del anuncio creado en Meta.</returns>
    /// <exception cref="MetaPublishException">Si Meta rechaza o falla; indica si conviene reintentar.</exception>
    Task<string> PublishAsync(MetaPublishRequest request, CancellationToken cancellationToken);
}

/// <summary>Fallo al publicar en Meta.</summary>
/// <param name="message">Qué pasó.</param>
/// <param name="retryable">Si reintentar puede arreglarlo.</param>
public sealed class MetaPublishException(string message, bool retryable) : Exception(message)
{
    /// <summary>Si reintentar puede arreglarlo.</summary>
    public bool Retryable { get; } = retryable;
}

/// <summary>Publicador de una instalación sin conexión con Meta: se declara no configurado y, si alguien lo llama
/// igual, falla sin escribir nada.</summary>
public sealed class NotConnectedMetaPublisher : IMetaPublisher
{
    /// <inheritdoc />
    public bool IsConfigured => false;

    /// <inheritdoc />
    public Task<string> PublishAsync(MetaPublishRequest request, CancellationToken cancellationToken)
    {
        throw new MetaPublishException("No hay conexión con Meta: no se puede publicar.", false);
    }
}
