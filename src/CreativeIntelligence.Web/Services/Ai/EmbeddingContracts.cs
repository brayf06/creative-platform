using System.Runtime.InteropServices;

namespace CreativeIntelligence.Web.Services.Ai;

/// <summary>Embeddings de texto para la búsqueda semántica. Es un límite externo: hoy lo implementa Gemini.</summary>
public interface IEmbeddingProvider
{
    /// <summary>Falso si falta la clave del proveedor: entonces la búsqueda cae a palabras clave.</summary>
    bool IsConfigured { get; }

    /// <summary>Modelo que se usa. Los vectores de modelos distintos no son comparables entre sí.</summary>
    string Model { get; }

    /// <summary>Convierte textos en vectores.</summary>
    /// <param name="texts">Textos, al menos uno.</param>
    /// <param name="purpose">Si son documentos a indexar o una consulta.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Un vector normalizado por texto, en el mismo orden.</returns>
    /// <exception cref="AiProviderException">Si el proveedor rechaza el pedido, falla o devuelve algo ilegible.</exception>
    Task<EmbeddingOutcome> EmbedAsync(List<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken);
}

/// <summary>Operaciones sobre vectores de embeddings.</summary>
public static class VectorMath
{
    /// <summary>Serializa un vector como bytes (float de 32 bits), para guardarlo en la base.</summary>
    /// <param name="vector">Vector.</param>
    /// <returns>Cuatro bytes por componente.</returns>
    public static byte[] ToBytes(float[] vector)
    {
        return MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();
    }

    /// <summary>Reconstruye un vector guardado con <see cref="ToBytes"/>.</summary>
    /// <param name="bytes">Bytes del vector.</param>
    /// <returns>El vector.</returns>
    public static float[] FromBytes(byte[] bytes)
    {
        return MemoryMarshal.Cast<byte, float>(bytes).ToArray();
    }

    /// <summary>Devuelve el vector llevado a longitud 1, para que el producto punto sea la similitud coseno.</summary>
    /// <param name="vector">Vector.</param>
    /// <returns>El vector normalizado; el mismo si su longitud es 0.</returns>
    public static float[] Normalize(float[] vector)
    {
        double length = Math.Sqrt(vector.Sum(v => (double)v * v));
        return length == 0 ? vector : vector.Select(v => (float)(v / length)).ToArray();
    }

    /// <summary>Producto punto de dos vectores; con vectores normalizados es su similitud coseno (de -1 a 1).</summary>
    /// <param name="left">Primer vector.</param>
    /// <param name="right">Segundo vector.</param>
    /// <returns>La similitud; 0 si las dimensiones no coinciden.</returns>
    public static double Dot(float[] left, float[] right)
    {
        if (left.Length != right.Length)
        {
            return 0;
        }

        double sum = 0;
        for (int i = 0; i < left.Length; i++)
        {
            sum += left[i] * right[i];
        }

        return sum;
    }
}
