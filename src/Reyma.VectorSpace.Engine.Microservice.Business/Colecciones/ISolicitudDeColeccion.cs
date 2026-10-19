namespace Reyma.VectorSpace.Engine.Microservice.Business.Colecciones;

/// <summary>
/// Toda solicitud que actua sobre una coleccion implementa esta interfaz; <see cref="AutorizacionColeccionBehavior{TRequest,TResponse}"/>
/// la intercepta antes del handler. Un test comprueba que ninguna solicitud con propiedad Coleccion quede fuera.
/// </summary>
public interface ISolicitudDeColeccion
{
    string Coleccion { get; }

    OperacionColeccion Operacion { get; }
}
