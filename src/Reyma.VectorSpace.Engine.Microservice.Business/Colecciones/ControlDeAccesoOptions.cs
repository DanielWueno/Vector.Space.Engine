using Microsoft.Extensions.Options;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Colecciones;

/// <summary>
/// Tabla de acceso por coleccion (seccion <c>ControlDeAcceso</c>). Sin concesiones no se alcanza ninguna
/// coleccion: el acceso se niega por defecto y solo se otorga por una concesion explicita.
/// </summary>
public sealed class ControlDeAccesoOptions
{
    public const string Seccion = "ControlDeAcceso";

    public List<ConcesionColeccion> Concesiones { get; set; } = [];
}

/// <summary>
/// Otorga <see cref="Operaciones"/> sobre <see cref="Coleccion"/> a quien coincida con al menos un sujeto.
/// Los sujetos se leen con las mismas extensiones corporativas que el login de la plantilla
/// (<c>Reyma.IdentityService.Extensions</c>), no con nombres de claim propios.
/// </summary>
public sealed class ConcesionColeccion
{
    public string Coleccion { get; set; } = string.Empty;

    public List<OperacionColeccion> Operaciones { get; set; } = [];

    /// <summary>Id de usuario (claim <c>sub</c>, GUID) o nombre de usuario (<c>preferred_username</c>, sin distinguir mayusculas).</summary>
    public List<string> Usuarios { get; set; } = [];

    /// <summary>Cliente organizacional Reyma (claim <c>cli_id</c>), no el client_id OAuth de la aplicacion.</summary>
    public List<Guid> Clientes { get; set; } = [];

    /// <summary>Roles del token (claim <c>role</c>), comparados de forma exacta.</summary>
    public List<string> Roles { get; set; } = [];
}

/// <summary>
/// Rechaza al arrancar una tabla que otorgaria mas de lo escrito: nombres invalidos, concesiones sin
/// operaciones o sin sujetos, y sujetos vacios (un GUID vacio coincidiria con cualquier token sin ese claim).
/// </summary>
public sealed class ControlDeAccesoOptionsValidator : IValidateOptions<ControlDeAccesoOptions>
{
    public ValidateOptionsResult Validate(string? name, ControlDeAccesoOptions options)
    {
        var errores = new List<string>();

        for (var i = 0; i < options.Concesiones.Count; i++)
        {
            var c = options.Concesiones[i];
            var donde = $"{ControlDeAccesoOptions.Seccion}:Concesiones:{i}";

            if (!NombreColeccion.EsValido(c.Coleccion))
                errores.Add($"{donde}:Coleccion no cumple {NombreColeccion.Patron}.");
            if (c.Operaciones.Count == 0)
                errores.Add($"{donde}:Operaciones esta vacia.");
            if (c.Operaciones.Any(o => !Enum.IsDefined(o)))
                errores.Add($"{donde}:Operaciones tiene un valor desconocido.");
            if (c.Usuarios.Count + c.Clientes.Count + c.Roles.Count == 0)
                errores.Add($"{donde} no tiene Usuarios, Clientes ni Roles.");
            if (c.Usuarios.Any(u => string.IsNullOrWhiteSpace(u) || Guid.TryParse(u, out var g) && g == Guid.Empty))
                errores.Add($"{donde}:Usuarios tiene un valor vacio.");
            if (c.Clientes.Any(g => g == Guid.Empty))
                errores.Add($"{donde}:Clientes tiene un GUID vacio.");
            if (c.Roles.Any(string.IsNullOrWhiteSpace))
                errores.Add($"{donde}:Roles tiene un valor vacio.");
        }

        return errores.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errores);
    }
}
