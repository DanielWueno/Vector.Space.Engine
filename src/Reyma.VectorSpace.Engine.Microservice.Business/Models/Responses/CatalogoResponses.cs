namespace Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;

// Catalogo de lo que se puede pedir a esta instancia (GET api/catalogo/*). Sale de la configuracion y del codigo con
// los que arranco el servicio -- lo mismo que valida cada solicitud --, no de un listado mantenido a mano. Nunca lleva
// URLs de servidores, claves ni certificados.

/// <summary>
/// Un perfil de embeddings que esta instancia acepta en el campo <c>perfil</c> de ingestas, busquedas y embeddings.
/// </summary>
/// <param name="Nombre">Valor exacto que se envia en <c>perfil</c>.</param>
/// <param name="Modelo">Modelo que calcula los vectores.</param>
/// <param name="Dimension">Dimension de los vectores guardados.</param>
/// <param name="EsPorDefecto">true en el perfil que se usa cuando <c>perfil</c> se omite al crear una coleccion.</param>
/// <param name="UsadoPorResumen">true en el perfil que vectoriza los resumenes de negocio (espacio <c>resumen</c>).</param>
/// <param name="Disponible">Resultado del sondeo en vivo: el servidor respondio y, si lista modelos, incluye este.</param>
/// <param name="ModeloVerificado">true si el servidor confirmo que sirve este modelo; false si solo se comprobo que responde.</param>
/// <param name="VentanaTokens">Longitud maxima de entrada que reporta el servidor; null si no la expone.</param>
/// <param name="Detalle">Motivo cuando no esta disponible o el modelo no se pudo verificar; null si todo esta bien.</param>
public sealed record PerfilEmbeddingsInfoResponse(
    string Nombre, string Modelo, int Dimension, bool EsPorDefecto, bool UsadoPorResumen,
    bool Disponible, bool ModeloVerificado, int? VentanaTokens, string? Detalle);

/// <summary>
/// Un perfil de LLM que esta instancia acepta en el campo <c>perfilLlm</c> de las ingestas: el modelo que escribe el
/// resumen de negocio de cada chunk cuando la coleccion tiene <c>resumen</c>.
/// </summary>
/// <param name="Nombre">Valor exacto que se envia en <c>perfilLlm</c>.</param>
/// <param name="Modelo">Modelo del servidor de LLM.</param>
/// <param name="MaxTokens">Tope de tokens de cada resumen.</param>
/// <param name="Razonamiento">Valor de <c>enable_thinking</c> que se envia al modelo; null si no se envia.</param>
/// <param name="EsPorDefecto">true en el perfil que se usa cuando <c>perfilLlm</c> se omite.</param>
/// <param name="Disponible">Resultado del sondeo en vivo (ver <see cref="PerfilEmbeddingsInfoResponse"/>). false si
/// el resumen de negocio no esta habilitado en esta instancia.</param>
/// <param name="ModeloVerificado">true si el servidor confirmo que este modelo esta habilitado para la credencial del servicio.</param>
/// <param name="VentanaTokens">Ventana de contexto que reporta el servidor; null si no la expone.</param>
/// <param name="Detalle">Motivo cuando no esta disponible o el modelo no se pudo verificar; null si todo esta bien.</param>
public sealed record PerfilLlmInfoResponse(
    string Nombre, string Modelo, int MaxTokens, bool? Razonamiento, bool EsPorDefecto,
    bool Disponible, bool ModeloVerificado, int? VentanaTokens, string? Detalle);

/// <summary>Un valor que esta instancia acepta en el campo <c>perfilIngesta</c> de las ingestas.</summary>
/// <param name="Nombre">Valor exacto que se envia en <c>perfilIngesta</c>.</param>
/// <param name="Descripcion">Que cambia respecto a la ingesta nativa (la que se usa si se omite <c>perfilIngesta</c>).</param>
/// <param name="RequiereRepositorioLogico">true si exige tambien el campo <c>repositorioLogico</c>.</param>
public sealed record PerfilIngestaInfoResponse(string Nombre, string Descripcion, bool RequiereRepositorioLogico);

/// <summary>Un origen desde el que <c>POST {coleccion}/ingestas/git</c> acepta clonar. Toda otra URL responde 400.</summary>
/// <param name="Esquema">Esquema exacto (<c>https</c> o <c>http</c>).</param>
/// <param name="Host">Host exacto, sin comodines ni subdominios.</param>
/// <param name="Puerto">Puerto exacto.</param>
/// <param name="PrefijoRuta">Prefijo obligatorio de la ruta (p. ej. la organizacion); null si cualquier ruta del host vale.</param>
/// <param name="Ejemplo">Forma de una URL aceptada por este origen.</param>
public sealed record OrigenGitInfoResponse(string Esquema, string Host, int Puerto, string? PrefijoRuta, string Ejemplo);
