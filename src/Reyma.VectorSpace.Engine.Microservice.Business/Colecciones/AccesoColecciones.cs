using System.Security.Claims;
using Microsoft.Extensions.Options;
using Reyma.IdentityService.Extensions;
using Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Colecciones;

/// <summary>
/// Resuelve que colecciones alcanza un usuario autenticado segun <see cref="ControlDeAccesoOptions"/>.
/// </summary>
public interface IAccesoColecciones
{
    bool Permite(ClaimsPrincipal? usuario, string? coleccion, OperacionColeccion operacion);

    IReadOnlyList<ColeccionAccesibleResponse> Accesibles(ClaimsPrincipal? usuario);
}

public sealed class AccesoColecciones(IOptionsMonitor<ControlDeAccesoOptions> opciones) : IAccesoColecciones
{
    public bool Permite(ClaimsPrincipal? usuario, string? coleccion, OperacionColeccion operacion) =>
        NombreColeccion.EsValido(coleccion)
        && Concesiones(usuario).Any(c => c.Coleccion == coleccion && c.Operaciones.Contains(operacion));

    public IReadOnlyList<ColeccionAccesibleResponse> Accesibles(ClaimsPrincipal? usuario) =>
        Concesiones(usuario)
            .GroupBy(c => c.Coleccion, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ColeccionAccesibleResponse(
                g.Key,
                g.SelectMany(c => c.Operaciones).Distinct().Order().ToArray()))
            .ToArray();

    private IEnumerable<ConcesionColeccion> Concesiones(ClaimsPrincipal? usuario)
    {
        if (usuario?.Identity?.IsAuthenticated != true)
            return [];

        // Las extensiones devuelven Guid.Empty / null / vacio cuando falta el claim; esos valores nunca coinciden.
        var id = usuario.GetUserId();
        var nombre = usuario.GetUserName();
        var cliente = usuario.GetClientId();
        var roles = (usuario.GetRoles() ?? [])
            .Select(r => r?.ToString())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToHashSet(StringComparer.Ordinal);

        return opciones.CurrentValue.Concesiones.Where(c =>
            c.Usuarios.Any(u => id != Guid.Empty && Guid.TryParse(u, out var g) && g == id
                                || !string.IsNullOrWhiteSpace(nombre) && string.Equals(u, nombre, StringComparison.OrdinalIgnoreCase))
            || cliente != Guid.Empty && c.Clientes.Contains(cliente)
            || c.Roles.Any(roles.Contains));
    }
}
