// Backy/Services/IndexingQueue.cs
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

public class IndexingQueue : IIndexingQueue
{
    private readonly ConcurrentQueue<int> _queue = new ConcurrentQueue<int>();
    private readonly SemaphoreSlim _queueSignal = new SemaphoreSlim(0);

    public void EnqueueIndexing(int storageId)
    {
        _queue.Enqueue(storageId);
        _queueSignal.Release();
    }

    public async Task<int> DequeueAsync(CancellationToken cancellationToken)
    {
        await _queueSignal.WaitAsync(cancellationToken);
        _queue.TryDequeue(out var storageId);
        return storageId;
    }
}
