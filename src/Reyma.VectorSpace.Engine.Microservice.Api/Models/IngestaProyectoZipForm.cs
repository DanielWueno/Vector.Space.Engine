using Microsoft.AspNetCore.Http;

namespace Reyma.VectorSpace.Engine.Microservice.Api.Models;

/// <summary>
/// Formulario multipart de <c>POST {coleccion}/ingestas/proyecto</c>. Es una clase (no parametros sueltos) para que
/// Swagger muestre la descripcion de cada campo. Los nombres de campo del formulario no cambiaron.
/// </summary>
public sealed class IngestaProyectoZipForm
{
    /// <summary>
    /// Obligatorio. El proyecto completo en un .zip (hasta 100 MiB), por ejemplo el "descargar como zip" de GitHub o
    /// Azure DevOps. Se indexa en la coleccion de la ruta: lo que ya estaba en ella y no viene en este .zip se borra.
    /// Solo entran las extensiones de codigo y documentacion que admite el servicio; el resto se ignora.
    /// </summary>
    public IFormFile? Zip { get; set; }

    /// <summary>
    /// Opcional. Carpeta envoltorio del .zip que se quita de las rutas, p. ej. <c>MiRepo-main</c> cuando el .zip trae
    /// <c>MiRepo-main/src/...</c>: asi el documento queda como <c>src/...</c>. Todas las entradas deben estar debajo
    /// de ella (si no, 400). Ausente = las rutas se guardan tal como vienen en el .zip. Usar siempre el mismo valor
    /// para un proyecto: la ruta identifica al documento y cambiarla lo reindexa entero.
    /// </summary>
    public string? CarpetaRaiz { get; set; }

    /// <summary>
    /// Opcional. Perfil de embeddings (modelo + dimension) de la coleccion; valores en
    /// GET api/catalogo/perfiles-embeddings. Se fija al crear la coleccion; ausente = el perfil por defecto o el que ya
    /// tiene la coleccion. Uno distinto al de la coleccion responde 409.
    /// </summary>
    public string? Perfil { get; set; }

    /// <summary>
    /// Opcional. true = guardar tambien un resumen de negocio en lenguaje natural de cada fragmento, escrito por un LLM
    /// (ver <c>perfilLlm</c>), para buscar con <c>espacio=resumen</c>. Solo cuenta al crear la coleccion; no se puede
    /// agregar despues (409). Gasta cuota de tokens del LLM.
    /// </summary>
    public bool? Resumen { get; set; }

    /// <summary>
    /// Opcional. Perfil de LLM que escribe los resumenes de esta ingesta (solo si la coleccion tiene resumen); valores
    /// en GET api/catalogo/perfiles-llm. Ausente = el perfil por defecto.
    /// </summary>
    public string? PerfilLlm { get; set; }

    /// <summary>
    /// Opcional. Forma de fragmentar e identificar los documentos; ausente = ingesta nativa. Valores en
    /// GET api/catalogo/perfiles-ingesta (hoy <c>rag-local-1.12</c>). Exige <c>repositorioLogico</c>.
    /// </summary>
    public string? PerfilIngesta { get; set; }

    /// <summary>
    /// Solo con <c>perfilIngesta</c> (ambos o ninguno). Nombre logico del repositorio (1 a 128 caracteres, sin
    /// <c>/</c> ni <c>\</c>) que identifica los documentos en vez del nombre de la coleccion. Inmutable.
    /// </summary>
    public string? RepositorioLogico { get; set; }
}
