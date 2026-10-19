using Reyma.Dates.Abstractions;

namespace Reyma.VectorSpace.Engine.Microservice.Business;

public sealed class UtcDateTime : IDateTime
{
    public DateTime Now => DateTime.UtcNow;
}
