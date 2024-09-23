using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Backy.Services
{
    public class IndexingQueue : IIndexingQueue
    {
        private readonly ConcurrentQueue<Guid> _queue = new ConcurrentQueue<Guid>();
        private readonly SemaphoreSlim _queueSignal = new SemaphoreSlim(0);

        public void EnqueueIndexing(Guid storageId)
        {
            _queue.Enqueue(storageId);
            _queueSignal.Release();
        }

        public async Task<Guid> DequeueAsync(CancellationToken cancellationToken)
        {
            await _queueSignal.WaitAsync(cancellationToken);
            _queue.TryDequeue(out var storageId);
            return storageId;
        }
    }
}
