using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.VectorStore;

/// <summary>
/// Filtro exacto de modulo (item 1.21.1). Un modulo <c>m</c> coincide con un punto si su <c>namespace</c> es <c>m</c> o
/// empieza por <c>m + "."</c>, o si su <c>relative_path</c> es <c>m</c> o empieza por <c>m + "/"</c>; comparacion
/// ordinal, sin recorte ni minusculas. Para resolverlo con un indice keyword exacto (no con prefijos de texto, que no
/// respetan fronteras), cada punto guarda en <see cref="PayloadKey"/> el valor completo de ambos campos y cada prefijo
/// que termina justo antes de un separador: el predicado equivale a "m pertenece a esa lista".
/// <para>
/// Preparacion de la coleccion: <see cref="VersionPayloadKey"/> (= <see cref="Version"/>) en cada punto, indices
/// <c>keyword</c>/<c>integer</c> de ambos campos y la marca <see cref="MetadataKey"/> de la coleccion. Una coleccion
/// nueva nace marcada <see cref="MarcaCompleta"/>; una anterior la obtiene con <see cref="RellenoDePrefijosDeModulo"/>.
/// <c>en_curso:&lt;run&gt;:&lt;expiraUnixMs&gt;</c> significa relleno en marcha o interrumpido: rechaza ingestas.
/// </para>
/// </summary>
public static class ModulePrefixes
{
    public const string PayloadKey = "module_prefixes";
    public const string VersionPayloadKey = "module_prefixes_version";
    public const int Version = 1;

    /// <summary>Clave de metadata de la coleccion con el estado de la preparacion para el filtro de modulo.</summary>
    public const string MetadataKey = "module_prefixes";
    public const string MarcaCompleta = "completo:1";
    public const string PrefijoMarcaEnCurso = "en_curso:";

    /// <summary>Prefijos de modulo de un punto: union ordinal, sin duplicados y ordenada, de los de ambos campos.</summary>
    public static IReadOnlyList<string> Derive(string? @namespace, string? relativePath)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        Agregar(set, @namespace, '.');
        Agregar(set, relativePath, '/');
        return set.ToList();
    }

    private static void Agregar(SortedSet<string> set, string? valor, char separador)
    {
        if (string.IsNullOrEmpty(valor))
            return;
        for (var i = 0; i < valor.Length; i++)
            if (valor[i] == separador && i > 0)
                set.Add(valor[..i]);
        set.Add(valor);
    }

    internal static Value ToPayloadValue(IReadOnlyList<string> prefijos)
    {
        var lista = new ListValue();
        lista.Values.AddRange(prefijos.Select(p => new Value { StringValue = p }));
        return new Value { ListValue = lista };
    }

    /// <summary>Filtro "punto sin preparar": falta la version o no es la actual.</summary>
    internal static Filter SinPreparar() => new() { MustNot = { Conditions.Match(VersionPayloadKey, Version) } };

    /// <summary>Marca de la coleccion por REST (el gRPC de Qdrant 1.19 no expone metadata); null si no la tiene.</summary>
    internal static async Task<string?> LeerMarcaAsync(HttpClient rest, string coleccion, CancellationToken ct)
    {
        using var response = await rest.GetAsync($"collections/{Uri.EscapeDataString(coleccion)}", ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Qdrant respondio {(int)response.StatusCode} al leer la coleccion.");

        using var cuerpo = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
        if (cuerpo is null ||
            !cuerpo.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("config", out var config) ||
            !config.TryGetProperty("metadata", out var metadata) ||
            metadata.ValueKind != JsonValueKind.Object ||
            !metadata.TryGetProperty(MetadataKey, out var marca) ||
            marca.ValueKind != JsonValueKind.String)
            return null;
        return marca.GetString();
    }

    /// <summary>Escribe solo la clave de la marca (Qdrant fusiona la metadata; el resto del perfil no se toca).</summary>
    internal static async Task EscribirMarcaAsync(HttpClient rest, string coleccion, string marca, CancellationToken ct)
    {
        var cuerpo = new { metadata = new Dictionary<string, object> { [MetadataKey] = marca } };
        using var response = await rest.PatchAsJsonAsync($"collections/{Uri.EscapeDataString(coleccion)}", cuerpo, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Qdrant respondio {(int)response.StatusCode} al escribir la marca de modulo.");
    }

    internal static string MarcaEnCurso(string run, DateTimeOffset expira) =>
        $"{PrefijoMarcaEnCurso}{run}:{expira.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}";

    internal static bool TryParseEnCurso(string? marca, out string run, out DateTimeOffset expira)
    {
        run = "";
        expira = DateTimeOffset.MinValue;
        if (marca is null || !marca.StartsWith(PrefijoMarcaEnCurso, StringComparison.Ordinal))
            return false;
        var resto = marca[PrefijoMarcaEnCurso.Length..];
        var sep = resto.LastIndexOf(':');
        if (sep <= 0 || !long.TryParse(resto[(sep + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
            return true; // en curso con formato desconocido: se trata como lease expirado de otro run
        run = resto[..sep];
        expira = DateTimeOffset.FromUnixTimeMilliseconds(ms);
        return true;
    }

    /// <summary>Si los indices del filtro existen con el tipo esperado (keyword e integer).</summary>
    internal static async Task<(bool Prefijos, bool Version, string? Conflicto)> LeerIndicesAsync(
        QdrantClient client, string coleccion, CancellationToken ct)
    {
        var info = await client.GetCollectionInfoAsync(coleccion, ct);
        var prefijos = false;
        var version = false;
        string? conflicto = null;
        if (info.PayloadSchema.TryGetValue(PayloadKey, out var p))
        {
            if (p.DataType == PayloadSchemaType.Keyword) prefijos = true;
            else conflicto = $"'{PayloadKey}' indexado como {p.DataType}, se esperaba Keyword";
        }
        if (info.PayloadSchema.TryGetValue(VersionPayloadKey, out var v))
        {
            if (v.DataType == PayloadSchemaType.Integer) version = true;
            else conflicto = $"'{VersionPayloadKey}' indexado como {v.DataType}, se esperaba Integer";
        }
        return (prefijos, version, conflicto);
    }

    /// <summary>Puntos sin preparar (conteo exacto).</summary>
    internal static Task<ulong> ContarSinPrepararAsync(QdrantClient client, string coleccion, CancellationToken ct) =>
        client.CountAsync(coleccion, SinPreparar(), exact: true, cancellationToken: ct);
}
