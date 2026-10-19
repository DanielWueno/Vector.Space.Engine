using MediatR;
using Reyma.Exceptions.Api;

namespace Reyma.VectorSpace.Engine.Microservice.Business.Validacion;

public sealed class ValidacionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is IValidable validable)
        {
            var errores = validable.Errores().ToArray();
            if (errores.Length > 0)
                throw new BadRequestException(string.Join(" ", errores));
        }

        return next(cancellationToken);
    }
}
