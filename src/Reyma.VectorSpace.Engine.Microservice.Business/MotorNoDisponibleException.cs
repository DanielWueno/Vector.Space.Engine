using System.Net;
using Reyma.Exceptions.Api;

namespace Reyma.VectorSpace.Engine.Microservice.Business;

/// <summary>
/// Una dependencia del motor (vector store o sidecar de embeddings) no respondio o respondio con error (503). El mensaje
/// es para el cliente: no lleva hosts, puertos ni el detalle de la excepcion original, que queda solo en el log.
/// </summary>
public sealed class MotorNoDisponibleException(string message) : HttpException(message, HttpStatusCode.ServiceUnavailable);
