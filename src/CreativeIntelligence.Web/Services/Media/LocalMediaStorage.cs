using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Services.Media;

/// <summary>Configuración del almacenamiento de medios (sección <c>Storage</c>).</summary>
public sealed class StorageOptions
{
    /// <summary>Carpeta raíz. Si es relativa, se resuelve contra el directorio de trabajo del proceso; la Web y
    /// el Worker deben apuntar a la misma carpeta.</summary>
    public string RootPath { get; set; } = string.Empty;
}

/// <summary>
/// Almacenamiento de medios en el disco local. Cada organización tiene su carpeta; los originales van en
/// <c>org-{id}/originals/{2 primeros del hash}/{hash}</c>, sin extensión, para que el mismo contenido ocupe un
/// solo archivo aunque se cargue con otro nombre. Las miniaturas van en <c>org-{id}/thumbnails/</c>.
/// </summary>
/// <param name="options">Carpeta raíz.</param>
public sealed class LocalMediaStorage(IOptions<StorageOptions> options) : IMediaStorage
{
    private readonly string _root = Path.GetFullPath(options.Value.RootPath);

    /// <inheritdoc />
    public async Task<StoredOriginal> SaveOriginalAsync(int organizationId, Stream content, CancellationToken cancellationToken)
    {
        string organizationFolder = $"org-{organizationId}";
        string tempFolder = Path.Combine(_root, organizationFolder, "tmp");
        Directory.CreateDirectory(tempFolder);
        string tempPath = Path.Combine(tempFolder, Guid.NewGuid().ToString("N"));

        // Se escribe a un temporal mientras se calcula el hash: el nombre definitivo depende del contenido.
        long size = 0;
        string sha256;
        using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            await using (FileStream target = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    size += read;
                }
            }

            sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        if (size == 0)
        {
            File.Delete(tempPath);
            return new StoredOriginal(sha256, 0, string.Empty);
        }

        string relativePath = Path.Combine(organizationFolder, "originals", sha256[..2], sha256);
        string finalPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        try
        {
            if (!File.Exists(finalPath))
            {
                File.Move(tempPath, finalPath);
            }
        }
        catch (IOException) when (File.Exists(finalPath))
        {
            // Otra carga del mismo contenido lo guardó primero: el original ya está y es idéntico.
        }
        finally
        {
            File.Delete(tempPath);
        }

        return new StoredOriginal(sha256, size, relativePath.Replace('\\', '/'));
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Si la ruta sale de la carpeta raíz.</exception>
    public Stream OpenRead(string relativePath)
    {
        return new FileStream(GetLocalPath(relativePath), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Si la ruta sale de la carpeta raíz.</exception>
    public string GetLocalPath(string relativePath)
    {
        string fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("La ruta pedida está fuera del almacenamiento.");
        }

        return fullPath;
    }

    /// <inheritdoc />
    public string ReserveThumbnailPath(int organizationId, string sha256)
    {
        string relativePath = Path.Combine($"org-{organizationId}", "thumbnails", sha256[..2], sha256 + ".jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_root, relativePath))!);
        return relativePath.Replace('\\', '/');
    }

    /// <inheritdoc />
    public string ReserveSegmentThumbnailPath(int organizationId, string sha256, int startMs, int endMs)
    {
        string relativePath = Path.Combine($"org-{organizationId}", "segments", sha256[..2], sha256, $"{startMs}-{endMs}.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_root, relativePath))!);
        return relativePath.Replace('\\', '/');
    }

    /// <inheritdoc />
    public string ReserveVariantPath(int organizationId, long variantId)
    {
        string relativePath = Path.Combine($"org-{organizationId}", "variants", $"{variantId}.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_root, relativePath))!);
        return relativePath.Replace('\\', '/');
    }
}
