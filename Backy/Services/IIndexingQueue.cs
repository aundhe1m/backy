using System;
using System.Threading;
using System.Threading.Tasks;

namespace Backy.Services
{
    public interface IIndexingQueue
    {
        void EnqueueIndexing(Guid storageId);
        Task<Guid> DequeueAsync(CancellationToken cancellationToken);
    }
}
