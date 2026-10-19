using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Reyma.IdentityService.Tools.Abstractions;
using Reyma.Persistence.Base.Postgres;

namespace Reyma.VectorSpace.Engine.Microservice.Persistence
{
    public sealed class ApplicationDbContext : BaseDbContext
    {
        public ApplicationDbContext(DbContextOptions options, ICurrentUserAccessor userAccessor, IOptions<ReymaPersistenceOptions> persistenceOptions)
            : base(options, userAccessor, persistenceOptions)
        {
        }
    }
}
