using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Reyma.VectorSpace.Engine.Microservice.Infrastructure.Model;

namespace Reyma.VectorSpace.Engine.Microservice.Infrastructure.Resumen;

/// <summary>
/// Regla determinista de 1.27 que decide QUE NO se resume, antes de la cache y del LLM (ver
/// <see cref="FiltroPrevioOptions"/>). Hasta 1.27 el propio modelo decidia el centinela y ambos modelos medidos resumian
/// entre 7 y 21 de cada 100 chunks tecnicos (envoltorios de FastReport, codigo de disenador, .csproj, propiedades
/// triviales). La regla se aplica solo a puntos pendientes: los ya resumidos no se tocan.
/// </summary>
public sealed class FiltroPrevioDeResumen
{
    /// <summary>Tope por patron configurado: una expresion catastrofica no debe colgar la fase de resumen.</summary>
    private static readonly TimeSpan TiempoMaximoPorPatron = TimeSpan.FromMilliseconds(100);

    private const int LineasDeCabecera = 5;

    private static readonly Regex AccesoresAutomaticos = new(
        @"\{\s*get\s*;\s*((public|private|protected|internal)\s+)*((set|init)\s*;\s*)?\}",
        RegexOptions.CultureInvariant, TiempoMaximoPorPatron);

    private readonly bool _habilitado;
    private readonly Regex[] _rutas;
    private readonly HashSet<string> _tipos;
    private readonly bool _propiedadesAutomaticas;
    private readonly bool _cabeceraAutoGenerada;

    public FiltroPrevioDeResumen(FiltroPrevioOptions opciones)
    {
        _habilitado = opciones.Habilitado;
        _rutas = opciones.PatronesDeRutaEfectivos
            .Select(p => new Regex(p, RegexOptions.CultureInvariant, TiempoMaximoPorPatron))
            .ToArray();
        _tipos = new HashSet<string>(opciones.TiposDeChunkEfectivos, StringComparer.Ordinal);
        _propiedadesAutomaticas = opciones.PropiedadesAutomaticasDeUnaLinea;
        _cabeceraAutoGenerada = opciones.CabeceraAutoGenerada;

        Modelo = $"filtro-previo:v{opciones.Version}";
        Identidad = CalcularIdentidad(opciones);
    }

    /// <summary>Lo que se guarda en <c>resumen_modelo</c> de un punto excluido, p. ej. <c>filtro-previo:v1</c>.</summary>
    public string Modelo { get; }

    /// <summary>
    /// Hash (8 hex) de la regla efectiva completa, que se guarda en <c>resumen_prompt_version</c> de un punto excluido:
    /// distingue dos configuraciones con la misma <see cref="FiltroPrevioOptions.Version"/>.
    /// </summary>
    public string Identidad { get; }

    /// <summary>
    /// Null si el chunk se resume normalmente; si no, el motivo de la exclusion (<c>ruta</c>, <c>tipo</c>,
    /// <c>propiedad-automatica</c> o <c>auto-generado</c>), solo para registro y pruebas.
    /// </summary>
    public string? Motivo(string rutaRelativa, string? tipoDeChunk, string contenido)
    {
        if (!_habilitado)
            return null;

        var ruta = rutaRelativa.Replace('\\', '/');
        if (_rutas.Any(r => CoincideSinColgarse(r, ruta)))
            return "ruta";

        if (tipoDeChunk is not null && _tipos.Contains(tipoDeChunk))
            return "tipo";

        if (_propiedadesAutomaticas && tipoDeChunk == nameof(ChunkType.Property) && EsPropiedadAutomaticaDeUnaLinea(contenido))
            return "propiedad-automatica";

        if (_cabeceraAutoGenerada && TieneCabeceraAutoGenerada(contenido))
            return "auto-generado";

        return null;
    }

    /// <summary>Nulo si la configuracion es valida; si no, el motivo (patron invalido, tipo inexistente, version).</summary>
    public static string? ErrorDeConfiguracion(FiltroPrevioOptions opciones)
    {
        if (opciones.Version <= 0)
            return "Motor:Resumen:FiltroPrevio:Version debe ser positiva.";

        foreach (var patron in opciones.PatronesDeRutaEfectivos)
        {
            if (string.IsNullOrWhiteSpace(patron))
                return "Motor:Resumen:FiltroPrevio:PatronesDeRuta no admite patrones vacios.";
            try
            {
                _ = new Regex(patron, RegexOptions.CultureInvariant, TiempoMaximoPorPatron);
            }
            catch (ArgumentException)
            {
                return $"Motor:Resumen:FiltroPrevio:PatronesDeRuta contiene una expresion regular invalida: '{patron}'.";
            }
        }

        var desconocidos = opciones.TiposDeChunkEfectivos.Where(t => !Enum.GetNames<ChunkType>().Contains(t, StringComparer.Ordinal)).ToArray();
        return desconocidos.Length == 0
            ? null
            : $"Motor:Resumen:FiltroPrevio:TiposDeChunk contiene tipos que no existen: {string.Join(", ", desconocidos)} " +
              $"(validos: {string.Join(", ", Enum.GetNames<ChunkType>())}).";
    }

    /// <summary>Un patron que agota su tiempo no excluye: ante la duda el chunk va al LLM, como antes de 1.27.</summary>
    private static bool CoincideSinColgarse(Regex patron, string texto)
    {
        try
        {
            return patron.IsMatch(texto);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool EsPropiedadAutomaticaDeUnaLinea(string contenido)
    {
        var codigo = contenido.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("//", StringComparison.Ordinal) && !l.StartsWith('['))
            .Take(2)
            .ToArray();
        return codigo.Length == 1 && !codigo[0].Contains("=>", StringComparison.Ordinal) &&
               CoincideSinColgarse(AccesoresAutomaticos, codigo[0]);
    }

    private static bool TieneCabeceraAutoGenerada(string contenido) =>
        contenido.Split('\n', LineasDeCabecera + 1).Take(LineasDeCabecera)
            .Any(l => l.Contains("<auto-generated", StringComparison.OrdinalIgnoreCase));

    private static string CalcularIdentidad(FiltroPrevioOptions o)
    {
        var entrada = string.Join('\n',
            $"filtro-previo|v{o.Version}|habilitado={o.Habilitado}",
            "rutas=" + string.Join('\u001f', o.PatronesDeRutaEfectivos),
            "tipos=" + string.Join('\u001f', o.TiposDeChunkEfectivos),
            $"propiedades={o.PropiedadesAutomaticasDeUnaLinea}|cabecera={o.CabeceraAutoGenerada}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entrada)))[..8].ToLowerInvariant();
    }
}
