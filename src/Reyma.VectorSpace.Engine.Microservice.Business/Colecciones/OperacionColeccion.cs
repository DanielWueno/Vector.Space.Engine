namespace Reyma.VectorSpace.Engine.Microservice.Business.Colecciones;

/// <summary>
/// Operacion que una concesion de <see cref="ControlDeAccesoOptions"/> otorga sobre una coleccion.
/// </summary>
public enum OperacionColeccion
{
    /// <summary>Calcular embeddings con el perfil de la coleccion y buscar en ella.</summary>
    Consultar,

    /// <summary>Ingerir documentos en la coleccion.</summary>
    Ingestar
}
