using System.Net;

namespace Storage;

public interface IStorageClient : IDisposable
{
    /// <summary>
    /// Builds pre-signed file url
    /// </summary>
    /// <remarks>This method will not check if the file exists or not: use <see cref="StorageClient.GetFileUrl"/> for check existing of file</remarks>
    /// <param name="fileName">Name of file</param>
    /// <param name="expiration">Time of url expiration</param>
    /// <returns>Pre-signed URL of the file</returns>
    string BuildFileUrl(string fileName, TimeSpan expiration);

    Task<bool> CreateBucket(CancellationToken cancellation);

    Task<bool> BucketExists(CancellationToken cancellation);

    Task<bool> DeleteBucket(CancellationToken cancellation);

    /// <summary>
    /// Deletes the file from storage
    /// </summary>
    /// <param name="fileName">Name of the file to be deleted</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <remarks>Storage sends the same response in the following cases: the file was indeed deleted, the file did not exist</remarks>
    /// <exception cref="HttpRequestException">Connection problems or status code isn't <see cref="HttpStatusCode.NoContent"/></exception>
    Task DeleteFile(string fileName, CancellationToken cancellation);

    Task<bool> FileExists(string fileName, CancellationToken cancellation);

    /// <summary>
    /// Gets file data from the storage
    /// </summary>
    /// <param name="fileName">File name than will be received</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns>Wrapper around <see cref="HttpResponseMessage"/> with the data of the file from storage</returns>
    /// <exception cref="HttpRequestException">Connection problems or status code isn't isn't <see cref="HttpStatusCode.OK"/> or <see cref="HttpStatusCode.NotFound"/></exception>
    Task<StorageFile> GetFile(string fileName, CancellationToken cancellation);

    /// <summary>
    /// Gets file stream from the storage
    /// </summary>
    /// <param name="fileName">File name than will be received</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns>Stream from <see cref="HttpResponseMessage"/> with the data of the file from storage</returns>
    /// <exception cref="HttpRequestException">Connection problems or status code isn't <see cref="HttpStatusCode.OK"/> or <see cref="HttpStatusCode.NotFound"/></exception>
    Task<Stream> GetFileStream(string fileName, CancellationToken cancellation);

    /// <summary>
    /// Ensures a file exists in the storage and returns pre-signed URL of the file 
    /// </summary>
    /// <param name="fileName">Name of a file that exists in the storage</param>
    /// <param name="expiration">Time of url expiration</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns>Returns <see cref="string"/> with pre-signed URL of file or <b>null</b> if there isn't a file</returns>
    /// <exception cref="HttpRequestException">Connection problems or other unexpected <see cref="HttpStatusCode"/></exception>
    Task<string?> GetFileUrl(string fileName, TimeSpan expiration, CancellationToken cancellation);

    /// <summary>
    /// Returns a list of files (only 1000 first) by <paramref name="prefix"/>  
    /// </summary>
    /// <param name="prefix">Prefix of file names</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns>Async collection of file names</returns>
    IAsyncEnumerable<string> List(string? prefix, CancellationToken cancellation);

    Task<bool> MultipartAbort(string fileName, string uploadId, CancellationToken cancellation);

    /// <summary>
    /// Completes multipart upload operation
    /// </summary>
    /// <param name="fileName">Name of file</param>
    /// <param name="uploadId">Identity of upload</param>
    /// <param name="partTags">Tags of parts, sorted by partNumber</param>
    /// <param name="tagsCount">Count of parts in <paramref name="partTags"/></param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns>Returns <b>true</b> if file has been uploaded or <b>false</b> if not</returns>
    Task<bool> MultipartComplete(
        string fileName, string uploadId, string[] partTags, int tagsCount,
        CancellationToken cancellation);

    Task<string> MultipartStart(string fileName, string fileType, CancellationToken cancellation);

    Task<string?> MultipartUpload(
        string fileName, string uploadId,
        int partNumber, byte[] partData, int partSize,
        CancellationToken cancellation);

    Task<bool> PutFile(string fileName, Stream data, string contentType, CancellationToken cancellation);

    Task<bool> PutFile(
        string fileName, byte[] data, string contentType,
        CancellationToken cancellation);

    Task<bool> PutFile(
        string fileName, byte[] data, int offset, int count, string contentType,
        CancellationToken cancellation);

    Task<bool> PutFileMultipart(
        string fileName, Stream data, string contentType,
        CancellationToken cancellation);

    Task<bool> PutFileMultipart(
        string fileName, Stream data, string contentType, int partSize,
        CancellationToken cancellation);

    /// <summary>
    /// Uploads a file data to the storage
    /// </summary>
    /// <remarks>If length of <paramref name="data"/> greater 5 Mb then will use multipart method of upload instead just direct upload</remarks>
    /// <param name="fileName">Name of data</param>
    /// <param name="data">Stream with file data</param>
    /// <param name="contentType">Type of file data in MIME format</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns>Returns <b>true</b> if file has been uploaded or <b>false</b> if not</returns>
    /// <exception cref="HttpRequestException">Connection problems or other unexpected <see cref="HttpStatusCode"/></exception>
    Task<bool> UploadFile(string fileName, Stream data, string contentType, CancellationToken cancellation);
}