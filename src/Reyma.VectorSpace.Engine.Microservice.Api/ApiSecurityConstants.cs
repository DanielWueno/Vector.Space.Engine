namespace Reyma.VectorSpace.Engine.Microservice.Api
{
    internal static class ApiSecurityConstants
    {
        /// <summary>
        /// Permisos corporativos previstos para las operaciones sobre colecciones. Todavia NO se aplican con
        /// [Authorize(Policy = ...)]: el validador corporativo consulta el servicio de permisos por cada llamada y,
        /// mientras no esten registrados ahi, toda llamada real responderia 403. Hoy no hay autorizacion por
        /// coleccion (item 1.14 retiro ControlDeAcceso): [Authorize] basta con estar autenticado. Estas constantes
        /// quedan como referencia para una futura decision de usar permisos del IdP corporativo por recurso, no
        /// implementada todavia (ver docs/servicio/contrato-api.md).
        /// </summary>
        internal static class Policies
        {
            public const string ColeccionesConsultar = "VECTORSPACE.ENGINE.API.COLECCIONES.CONSULTAR";

            public const string ColeccionesIngestar = "VECTORSPACE.ENGINE.API.COLECCIONES.INGESTAR";
        }
    }
}
