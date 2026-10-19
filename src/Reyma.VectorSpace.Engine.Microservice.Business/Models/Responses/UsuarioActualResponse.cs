namespace Reyma.VectorSpace.Engine.Microservice.Business.Models.Responses;

public sealed record UsuarioActualResponse(
    string? UserId,
    string? UserName,
    string? EmpleadoIdentityId,
    string? ClientId,
    IReadOnlyCollection<string> Roles);
