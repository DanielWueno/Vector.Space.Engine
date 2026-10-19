using System.Net;
using Reyma.Exceptions.Api;

namespace Reyma.VectorSpace.Engine.Microservice.Business;

/// <summary>
/// Parte del contrato ya publicada pero sin implementacion todavia (501). El filtro de excepciones la traduce.
/// </summary>
public sealed class NoImplementadoException(string message) : HttpException(message, HttpStatusCode.NotImplemented);
