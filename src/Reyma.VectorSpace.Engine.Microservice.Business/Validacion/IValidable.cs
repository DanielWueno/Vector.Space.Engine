namespace Reyma.VectorSpace.Engine.Microservice.Business.Validacion;

/// <summary>
/// Solicitud que valida su propio contenido. <see cref="ValidacionBehavior{TRequest,TResponse}"/> convierte los
/// errores en 400. La mediacion corporativa no ejecuta validadores por si sola (solo trae los behaviors de
/// rendimiento y transaccion), por eso la validacion se engancha aqui.
/// </summary>
public interface IValidable
{
    IEnumerable<string> Errores();
}
