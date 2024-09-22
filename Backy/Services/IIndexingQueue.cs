public interface IIndexingQueue
{
    void EnqueueIndexing(int storageId);
    Task<int> DequeueAsync(CancellationToken cancellationToken);
}