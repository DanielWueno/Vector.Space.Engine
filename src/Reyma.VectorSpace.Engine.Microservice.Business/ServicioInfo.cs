namespace Reyma.VectorSpace.Engine.Microservice.Business;

/// <summary>
/// Identificadores del servicio, derivados de los parametros del generador (nombre, ruta base,
/// nombre de API y prefijo de permisos). Los valores de aqui son los marcadores que sustituye
/// `dotnet new` al generar.
/// </summary>
public static class ServicioInfo
{
    public const string Nombre = "Reyma.VectorSpace.Engine";

    public const string RutaBase = "/reyma/vectorspace/engine";

    public const string NombreApi = "rym.vectorspace.engine.api";

    public const string PrefijoPermisos = "VECTORSPACE.ENGINE";
}
