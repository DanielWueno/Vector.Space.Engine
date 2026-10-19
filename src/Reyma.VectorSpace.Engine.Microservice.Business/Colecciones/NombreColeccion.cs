using System.Text.RegularExpressions;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Colecciones;

/// <summary>
/// Regla de nombre de coleccion: minusculas, digitos, guion y guion bajo, de 1 a 63 caracteres,
/// empezando por letra o digito. Cubre las colecciones Qdrant ya existentes (rag-engine, micro-repo).
/// No se normaliza nada: un nombre que no cumple la regla no se compara contra las concesiones.
/// </summary>
public static partial class NombreColeccion
{
    // \z y no $: $ acepta un salto de linea final, y "propia\n" no debe pasar por "propia".
    public const string Patron = @"^[a-z0-9][a-z0-9_-]{0,62}\z";

    [GeneratedRegex(Patron, RegexOptions.CultureInvariant)]
    private static partial Regex Regla();

    public static bool EsValido(string? nombre) => nombre is not null && Regla().IsMatch(nombre);
}
