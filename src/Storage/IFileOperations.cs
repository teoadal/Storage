namespace Storage;

public interface IFileOperations
{
	string BuildFileUrl(string fileName, TimeSpan expiration);
	Task DeleteFile(string fileName, CancellationToken ct);
	Task<S3File> GetFile(string fileName, CancellationToken ct);
	Task<Stream> GetFileStream(string fileName, CancellationToken ct);
	Task<string?> GetFileUrl(string fileName, TimeSpan expiration, CancellationToken ct);
	Task<bool> IsFileExists(string fileName, CancellationToken ct);
	IAsyncEnumerable<string> List(string? prefix, CancellationToken ct);
	Task<bool> UploadFile(string fileName, string contentType, byte[] data, CancellationToken ct);
	Task<S3Upload> UploadFile(string fileName, string contentType, CancellationToken ct);
	Task<bool> UploadFile(string fileName, string contentType, Stream data, CancellationToken ct);
}
