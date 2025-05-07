namespace Storage;

public interface IS3BucketClient: IBucketOperations, IFileOperations
{
	string Bucket { get; }
	Uri Endpoint { get; }
}
