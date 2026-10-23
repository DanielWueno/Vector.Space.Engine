using System.IO.Compression;
using System.Text;

namespace IngestaProyecto.Prototipo;

public sealed record LimitesZip
{
    /// <summary>Tamano maximo del .zip subido (se aplica tambien antes, como limite de cuerpo HTTP de la accion).</summary>
    public long MaxBytesZip { get; init; } = 100L * 1024 * 1024;

    /// <summary>Entradas del directorio central. Un zip con millones de entradas vacias agota memoria sin descomprimir nada.</summary>
    public int MaxEntradas { get; init; } = 20_000;

    /// <summary>Descomprimido total de lo que se llega a leer (solo entradas admitidas por el perfil).</summary>
    public long MaxBytesDescomprimidos { get; init; } = 256L * 1024 * 1024;

    /// <summary>Ratio descomprimido/comprimido declarado por entrada. El codigo fuente real comprime 3-10x.</summary>
    public double MaxRatio { get; init; } = 100;

    public int MaxCaracteresRuta { get; init; } = 1_024;
}

public sealed record DocumentoZip(string Ruta, string Contenido);

public sealed record ResultadoZip(
    string? Error,
    IReadOnlyList<DocumentoZip> Documentos,
    IReadOnlyList<(string Entrada, string Motivo)> Rechazadas,
    int Entradas,
    int FueraDePerfil,
    long BytesLeidos,
    string? PrefijoQuitado);

/// <summary>
/// Un .zip subido como origen de ingesta, leido a documentos EN MEMORIA, sin extraer nada a disco: el nombre de la entrada
/// solo se convierte en la "ruta" del documento (su identidad en la coleccion), nunca en una ruta del sistema de
/// archivos, asi que Zip Slip no tiene donde escribir. Aun asi los nombres se validan (sin "..", absolutas, unidades,
/// ':' ni nulos) porque esa ruta va al payload y al encabezado del chunk.
///
/// Solo se descomprimen las entradas que el perfil admitiria (extension y tamano por archivo, exclusiones de directorio,
/// .gitignore/.ragignore del propio zip): lo demas no se abre. Cada entrada se lee contando los bytes reales, no el
/// tamano que declara la cabecera, con tope por entrada y total; y antes de leer se rechaza un ratio declarado absurdo.
/// </summary>
public static class LectorZip
{
    public static ResultadoZip Leer(Stream zip, PerfilEscaneo perfil, LimitesZip limites, string? carpetaRaiz = null)
    {
        var rechazadas = new List<(string, string)>();
        var documentos = new List<DocumentoZip>();
        if (zip.CanSeek && zip.Length > limites.MaxBytesZip)
            return Fallo($"El .zip supera el maximo de {limites.MaxBytesZip} bytes.");

        ZipArchive archivo;
        try { archivo = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true); }
        catch (InvalidDataException ex) { return Fallo($"No es un .zip valido ({ex.Message})."); }

        using (archivo)
        {
            var entradas = archivo.Entries;
            if (entradas.Count > limites.MaxEntradas)
                return Fallo($"El .zip tiene {entradas.Count} entradas; el maximo es {limites.MaxEntradas}.", entradas.Count);

            // Nombres validados y normalizados a '/', antes de abrir nada.
            var validas = new List<(ZipArchiveEntry Entrada, string Ruta)>();
            foreach (var e in entradas)
            {
                if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) continue; // directorio
                var (ruta, motivo) = NormalizarRuta(e.FullName, limites.MaxCaracteresRuta);
                if (motivo is not null) { rechazadas.Add((e.FullName, motivo)); continue; }
                if (EsEnlaceSimbolico(e)) { rechazadas.Add((e.FullName, "enlace simbolico")); continue; }
                validas.Add((e, ruta!));
            }

            // "Descargar como zip" de GitHub/Azure DevOps envuelve todo en "repo-rama/". Se quita solo si el cliente lo
            // pide: adivinarlo confunde ese envoltorio con un repo cuyo contenido vive todo bajo src/ o docs/, y la
            // ruta es la identidad del documento (cambiarla revectoriza todo).
            string? prefijo = null;
            if (carpetaRaiz is not null)
            {
                prefijo = carpetaRaiz.Trim('/') + "/";
                var fuera = validas.Count(v => !v.Ruta.StartsWith(prefijo, StringComparison.Ordinal));
                if (fuera > 0)
                    return Fallo($"{fuera} entradas no estan bajo la carpeta raiz '{prefijo}'.", entradas.Count, rechazadas);
                validas = validas.Select(v => (v.Entrada, v.Ruta[prefijo.Length..])).ToList();
            }

            var ignore = ReglasDelZip(validas, perfil);
            var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fueraDePerfil = 0;
            long leidos = 0;
            foreach (var (e, ruta) in validas)
            {
                var segmentos = ruta.Split('/');
                if (!perfil.Extensiones.Contains(Path.GetExtension(ruta))
                    || segmentos[..^1].Any(s => perfil.Excluir.Any(x => string.Equals(s, x, StringComparison.OrdinalIgnoreCase)))
                    || ignore.IsIgnored(ruta, isDirectory: false))
                {
                    fueraDePerfil++;
                    continue;
                }
                if (e.Length > perfil.MaxBytesPorArchivo) { fueraDePerfil++; continue; } // como el escaner: se salta
                if (e.CompressedLength > 0 && (double)e.Length / e.CompressedLength > limites.MaxRatio)
                {
                    rechazadas.Add((e.FullName, $"ratio de compresion declarado {e.Length / Math.Max(1, e.CompressedLength)}:1"));
                    continue;
                }
                if (!vistas.Add(ruta)) { rechazadas.Add((e.FullName, "ruta repetida")); continue; }

                // Bytes reales, con tope: la cabecera puede mentir sobre el tamano descomprimido.
                var tope = (int)perfil.MaxBytesPorArchivo + 1;
                byte[] contenido;
                try
                {
                    using var s = e.Open();
                    contenido = LeerConTope(s, tope);
                }
                catch (InvalidDataException ex) { rechazadas.Add((e.FullName, $"entrada corrupta ({ex.Message})")); continue; }
                leidos += contenido.Length;
                if (contenido.Length >= tope)
                {
                    rechazadas.Add((e.FullName, $"descomprime mas de lo que declara ({e.Length} bytes declarados)"));
                    continue;
                }
                if (leidos > limites.MaxBytesDescomprimidos)
                    return Fallo($"El contenido descomprimido supera el maximo de {limites.MaxBytesDescomprimidos} bytes.",
                        entradas.Count, rechazadas);

                // Igual que File.ReadAllText del pipeline del CLI: UTF-8 con deteccion de BOM.
                using var lector = new StreamReader(new MemoryStream(contenido), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                documentos.Add(new DocumentoZip(ruta, lector.ReadToEnd()));
            }

            return new ResultadoZip(null, documentos, rechazadas, entradas.Count, fueraDePerfil, leidos, prefijo);
        }

        ResultadoZip Fallo(string error, int entradas = 0, List<(string, string)>? r = null) =>
            new(error, [], r ?? rechazadas, entradas, 0, 0, null);
    }

    public static (string? Ruta, string? Motivo) NormalizarRuta(string nombre, int maxCaracteres)
    {
        if (nombre.Length == 0) return (null, "nombre vacio");
        if (nombre.Length > maxCaracteres) return (null, "ruta demasiado larga");
        if (nombre.Any(char.IsControl)) return (null, "caracteres de control en el nombre");
        if (nombre.Contains(':')) return (null, "':' en el nombre (unidad o flujo alterno)");
        var ruta = nombre.Replace('\\', '/');
        if (ruta.StartsWith('/')) return (null, "ruta absoluta");
        var segmentos = ruta.Split('/');
        if (segmentos.Any(s => s is "" or "." or ".."))
            return (null, "ruta fuera de la raiz o con segmentos vacios");
        return (ruta, null);
    }

    private static bool EsEnlaceSimbolico(ZipArchiveEntry e)
    {
        // Unix guarda el modo en los 16 bits altos de los atributos externos; S_IFLNK = 0xA000.
        var modo = (e.ExternalAttributes >> 16) & 0xF000;
        return modo == 0xA000;
    }

    private static IgnoreRules ReglasDelZip(List<(ZipArchiveEntry Entrada, string Ruta)> validas, PerfilEscaneo perfil)
    {
        var lineas = new List<string>();
        foreach (var nombre in new[] { IgnoreRules.GitIgnoreFileName, IgnoreRules.RagIgnoreFileName })
        {
            var e = validas.FirstOrDefault(v => v.Ruta == nombre).Entrada;
            if (e is null || e.Length > perfil.MaxBytesPorArchivo) continue;
            using var s = e.Open();
            var texto = Encoding.UTF8.GetString(LeerConTope(s, (int)perfil.MaxBytesPorArchivo));
            lineas.AddRange(texto.Split('\n'));
        }
        return lineas.Count == 0 ? IgnoreRules.Empty : IgnoreRules.FromLines(lineas);
    }

    private static byte[] LeerConTope(Stream s, int tope)
    {
        var buffer = new MemoryStream();
        var bloque = new byte[81_920];
        int n;
        while (buffer.Length < tope && (n = s.Read(bloque, 0, (int)Math.Min(bloque.Length, tope - buffer.Length))) > 0)
            buffer.Write(bloque, 0, n);
        return buffer.ToArray();
    }

    /// <summary>
    /// Lo que NO hay que hacer, para medirlo: extraer cada entrada a Path.Combine(destino, entrada.FullName). Solo escribe
    /// de verdad dentro de <paramref name="cajon"/> (la carpeta del banco); lo que caeria fuera de el se reporta sin
    /// escribirlo. Devuelve, por entrada que sale de <paramref name="destino"/>, donde quedo o donde quedaria.
    /// </summary>
    public static List<(string Entrada, string Destino, bool Escrito)> ExtraerIngenuo(Stream zip, string destino, string cajon)
    {
        var fuera = new List<(string, string, bool)>();
        using var archivo = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        var raiz = Path.GetFullPath(destino) + Path.DirectorySeparatorChar;
        var raizCajon = Path.GetFullPath(cajon) + Path.DirectorySeparatorChar;
        foreach (var e in archivo.Entries)
        {
            if (e.FullName.EndsWith('/')) continue;
            string ruta;
            try { ruta = Path.GetFullPath(Path.Combine(destino, e.FullName)); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { continue; }
            if (ruta.StartsWith(raiz, StringComparison.OrdinalIgnoreCase))
            {
                Extraer(e, ruta);
                continue;
            }
            var dentroDelCajon = ruta.StartsWith(raizCajon, StringComparison.OrdinalIgnoreCase);
            fuera.Add((e.FullName, ruta, dentroDelCajon && Extraer(e, ruta)));
        }
        return fuera;

        static bool Extraer(ZipArchiveEntry e, string ruta)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
                e.ExtractToFile(ruta, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                return false;
            }
        }
    }
}
