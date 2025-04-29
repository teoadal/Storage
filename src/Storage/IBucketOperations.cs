
namespace Storage;

public interface IBucketOperations
{
	Task<bool> CreateBucket(CancellationToken ct);
	Task<bool> DeleteBucket(CancellationToken ct);
	Task<bool> IsBucketExists(CancellationToken ct);
}
