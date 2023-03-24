using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Storage.Utils;
using static Storage.Utils.HashHelper;

namespace Storage;

[DebuggerDisplay("Client for '{_bucket}'")]
[SuppressMessage("ReSharper", "SwitchStatementHandlesSomeKnownEnumValuesWithDefault")]
public sealed class StorageClient : IDisposable
{
    private const int DefaultPartSize = 5 * 1024 * 1024; // 5 Mb

    private static readonly string[] S3Headers = // trimmed, lower invariant, ordered
    {
        "host",
        "x-amz-content-sha256",
        "x-amz-date"
    };

    public readonly string Bucket;

    private readonly string _bucket;
    private readonly string _endpoint;

    private readonly HttpHelper _http;
    private readonly HttpClient _client;
    private readonly Signature _signature;
    private readonly bool _useHttp2;

    public StorageClient(StorageSettings settings, HttpClient? client = null)
    {
        Bucket = settings.Bucket;

        var bucket = Bucket.ToLower();
        var scheme = settings.UseHttps ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
        var port = settings.Port.HasValue ? $":{settings.Port}" : string.Empty;

        _bucket = $"{scheme}://{settings.EndPoint}{port}/{bucket}";
        _client = client ?? new HttpClient();
        _endpoint = $"{settings.EndPoint}{port}";
        _http = new HttpHelper(settings.AccessKey, settings.Region, settings.Service, S3Headers);
        _signature = new Signature(settings.SecretKey, settings.Region, settings.Service);
        _useHttp2 = settings.UseHttp2;
    }

    /// <summary>
    /// Builds pre-signed file url
    /// </summary>
    /// <remarks>This method will not check if the file exists or not: use <see cref="GetFileUrl"/> for check existing of file</remarks>
    /// <param name="fileName">Name of file</param>
    /// <param name="expiration">Time of url expiration</param>
    /// <returns>Pre-signed URL of the file</returns>
    public string BuildFileUrl(string fileName, TimeSpan expiration)
    {
        var now = DateTime.UtcNow;
        var url = _http.BuildUrl(_bucket, fileName, now, expiration);
        var signature = _signature.Calculate(url, now);

        return $"{url}&X-Amz-Signature={signature}";
    }

    public async Task<bool> CreateBucket(CancellationToken cancellation)
    {
        HttpResponseMessage response;
        using (var request = CreateRequest(HttpMethod.Put))
        {
            response = await Send(request, EmptyPayloadHash, cancellation).ConfigureAwait(false);
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                response.Dispose();
                return true;
            case HttpStatusCode.Conflict: // already exists
                response.Dispose();
                return false;
            default:
                Errors.UnexpectedResult(response);
                return false;
        }
    }

    public async Task<bool> BucketExists(CancellationToken cancellation)
    {
        HttpResponseMessage response;
        using (var request = CreateRequest(HttpMethod.Head))
        {
            response = await Send(request, EmptyPayloadHash, cancellation).ConfigureAwait(false);
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                response.Dispose();
                return true;
            case HttpStatusCode.NotFound:
                response.Dispose();
                return false;
            default:
                Errors.UnexpectedResult(response);
                return false;
        }
    }

    public async Task<bool> DeleteBucket(CancellationToken cancellation)
    {
        HttpResponseMessage response;
        using (var request = CreateRequest(HttpMethod.Delete))
        {
            response = await Send(request, EmptyPayloadHash, cancellation).ConfigureAwait(false);
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.NoContent:
                response.Dispose();
                return true;
            case HttpStatusCode.NotFound:
                response.Dispose();
                return false;
            default:
                Errors.UnexpectedResult(response);
                return false;
        }
    }

    /// <summary>
    /// Deletes the file from storage
    /// </summary>
    /// <param name="fileName">Name of the file to be deleted</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <remarks>Storage sends the same response in the following cases: the file was indeed deleted, the file did not exist</remarks>
    /// <exception cref="HttpRequestException">Connection problems or status code isn't <see cref="HttpStatusCode.NoContent"/></exception>
    public async Task DeleteFile(string fileName, CancellationToken cancellation)
    {
        HttpResponseMessage response;
        using (var request = CreateRequest(HttpMethod.Delete, fileName))
        {
            response = await Send(request, EmptyPayloadHash, cancellation).ConfigureAwait(false);
        }

        if (response.StatusCode != HttpStatusCode.NoContent) Errors.UnexpectedResult(response);
        response.Dispose();
    }

    public async Task<bool> FileExists(string fileName, CancellationToken cancellation)
    {
        HttpResponseMessage response;
        using (var request = CreateRequest(HttpMethod.Head, fileName))
        {
            response = await Send(request, EmptyPayloadHash, cancellation).ConfigureAwait(false);
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                response.Dispose();
                return true;
            case HttpStatusCode.NotFound:
                response.Dispose();
                return false;
            default:
                Errors.UnexpectedResult(response);
                return false;
        }
    }

    /// <summary>
    /// Gets file data from the storage
    /// </summary>
    /// <param name="fileName">File name than will be received</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns>Wrapper around <see cref="HttpResponseMessage"/> with the data of the file from storage</returns>
    /// <exception cref="HttpRequestException">Connection problems or status code isn't isn't <see cref="HttpStatusCode.OK"/> or <see cref="HttpStatusCode.NotFound"/></exception>
    public async Task<StorageFile> GetFile(string fileName, CancellationToken cancellation)
    {
        HttpResponseMessage response;
        using (var request = CreateRequest(HttpMethod.Get, fileName))
        {
            response = await Send(request, EmptyPayloadHash, cancellation).ConfigureAwait(false);
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                return new StorageFile(response);
            case HttpStatusCode.NotFound:
                response.Dispose();
                return new StorageFile(response);
            default:
                Errors.UnexpectedResult(response);
                return new StorageFile();
        }
    }

    /// <summary>
    /// Gets file stream from the storage
    /// </summary>
    /// <param name="fileName">File name than will be received</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns>Stream from <see cref="HttpResponseMessage"/> with the data of the file from storage</returns>
    /// <exception cref="HttpRequestException">Connection problems or status code isn't <see cref="HttpStatusCode.OK"/> or <see cref="HttpStatusCode.NotFound"/></exception>
    public async Task<Stream> GetFileStream(string fileName, CancellationToken cancellation)
    {
        HttpResponseMessage response;
        using (var request = CreateRequest(HttpMethod.Get, fileName))
        {
            response = await Send(request, EmptyPayloadHash, cancellation).ConfigureAwait(false);
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                return await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            case HttpStatusCode.NotFound:
                response.Dispose();
                return Stream.Null;
            default:
                Errors.UnexpectedResult(response);
                return Stream.Null;
        }
    }

    /// <summary>
    /// Ensures a file exists in the storage and returns pre-signed URL of the file 
    /// </summary>
    /// <param name="fileName">Name of a file that exists in the storage</param>
    /// <param name="expiration">Time of url expiration</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns>Returns <see cref="string"/> with pre-signed URL of file or <b>null</b> if there isn't a file</returns>
    /// <exception cref="HttpRequestException">Connection problems or other unexpected <see cref="HttpStatusCode"/></exception>
    public async Task<string?> GetFileUrl(string fileName, TimeSpan expiration, CancellationToken cancellation)
    {
        return await FileExists(fileName, cancellation).ConfigureAwait(false)
            ? BuildFileUrl(fileName, expiration)
            : null;
    }

    /// <summary>
    /// Returns a list of files (only 1000 first) by <paramref name="prefix"/>  
    /// </summary>
    /// <param name="prefix">Prefix of file names</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns>Async collection of file names</returns>
    public async IAsyncEnumerable<string> List(
        string? prefix,
        [EnumeratorCancellation] CancellationToken cancellation)
    {
        var url = string.IsNullOrEmpty(prefix)
            ? $"{_bucket}?list-type=2"
            : $"{_bucket}?list-type=2&prefix={HttpHelper.EncodeName(prefix)}";

        HttpResponseMessage response;
        using (var request = new HttpRequestMessage(HttpMethod.Get, url))
        {
            response = await Send(request, EmptyPayloadHash, cancellation).ConfigureAwait(false);
        }

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var responseStream = await response.Content
                .ReadAsStreamAsync(cancellation)
                .ConfigureAwait(false);

            while (responseStream.CanRead)
            {
                var readString = XmlStreamReader.ReadString(responseStream, "Key");
                if (string.IsNullOrEmpty(readString)) break;

                yield return readString;
            }

            await responseStream.DisposeAsync().ConfigureAwait(false);
            response.Dispose();

            yield break;
        }

        Errors.UnexpectedResult(response);
    }

    public async Task<bool> MultipartAbort(string fileName, string uploadId, CancellationToken cancellation)
    {
        HttpResponseMessage? response = null;
        using (var request = new HttpRequestMessage(HttpMethod.Delete, $"{_bucket}/{fileName}?uploadId={uploadId}"))
        {
            try
            {
                response = await Send(request, EmptyPayloadHash, cancellation).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }

        if (response == null) return false;

        var result = response is {IsSuccessStatusCode: true, StatusCode: HttpStatusCode.NoContent};
        response.Dispose();
        return result;
    }

    /// <summary>
    /// Completes multipart upload operation
    /// </summary>
    /// <param name="fileName">Name of file</param>
    /// <param name="uploadId">Identity of upload</param>
    /// <param name="partTags">Tags of parts, sorted by partNumber</param>
    /// <param name="tagsCount">Count of parts in <paramref name="partTags"/></param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns>Returns <b>true</b> if file has been uploaded or <b>false</b> if not</returns>
    public async Task<bool> MultipartComplete(
        string fileName, string uploadId, string[] partTags, int tagsCount,
        CancellationToken cancellation)
    {
        var builder = StringUtils.GetBuilder();

        builder.Append("<CompleteMultipartUpload>");
        for (var i = 0; i < partTags.Length; i++)
        {
            if (i == tagsCount) break;

            builder.Append("<Part>");
            builder.Append("<PartNumber>", i + 1, "</PartNumber>");
            builder.Append("<ETag>", partTags[i], "</ETag>");
            builder.Append("</Part>");
        }

        var data = builder
            .Append("</CompleteMultipartUpload>")
            .Flush();

        var payloadHash = GetPayloadHash(data);

        HttpResponseMessage response;
        using (var request = new HttpRequestMessage(HttpMethod.Post, $"{_bucket}/{fileName}?uploadId={uploadId}"))
        {
            using (var content = new StringContent(data, Encoding.UTF8))
            {
                request.Content = content;
                response = await Send(request, payloadHash, cancellation).ConfigureAwait(false);
            }
        }

        var result = response is {IsSuccessStatusCode: true, StatusCode: HttpStatusCode.OK};

        response.Dispose();
        return result;
    }

    public async Task<string> MultipartStart(string fileName, string fileType, CancellationToken cancellation)
    {
        HttpResponseMessage response;
        using (var request = new HttpRequestMessage(HttpMethod.Post, $"{_bucket}/{fileName}?uploads"))
        {
            using (var content = new ByteArrayContent(Array.Empty<byte>()))
            {
                content.Headers.Add("content-type", fileType);
                request.Content = content;

                response = await Send(request, EmptyPayloadHash, cancellation).ConfigureAwait(false);
            }
        }

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var responseStream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            var result = XmlStreamReader.ReadString(responseStream, "UploadId");

            await responseStream.DisposeAsync().ConfigureAwait(false);
            response.Dispose();

            return result;
        }

        Errors.UnexpectedResult(response);
        return string.Empty;
    }

    public async Task<string?> MultipartUpload(
        string fileName, string uploadId,
        int partNumber, byte[] partData, int partSize,
        CancellationToken cancellation)
    {
        var payloadHash = GetPayloadHash(partData.AsSpan(0, partSize));
        var uri = $"{_bucket}/{fileName}?partNumber={partNumber}&uploadId={uploadId}";

        HttpResponseMessage response;
        using (var request = new HttpRequestMessage(HttpMethod.Put, uri))
        {
            using (var content = new ByteArrayContent(partData, 0, partSize))
            {
                content.Headers.Add("content-length", partSize.ToString());
                request.Content = content;

                response = await Send(request, payloadHash, cancellation).ConfigureAwait(false);
            }
        }

        var result = response is {IsSuccessStatusCode: true, StatusCode: HttpStatusCode.OK}
            ? response.Headers.ETag?.Tag
            : null;

        response.Dispose();
        return result;
    }

    public async Task<bool> PutFile(string fileName, Stream data, string contentType, CancellationToken cancellation)
    {
        var bufferPool = ArrayPool<byte>.Shared;

        var buffer = bufferPool.Rent((int) data.Length);
        var dataSize = await data.ReadAsync(buffer, cancellation).ConfigureAwait(false);
        var payloadHash = GetPayloadHash(buffer.AsSpan(0, dataSize));

        HttpResponseMessage response;
        using (var request = CreateRequest(HttpMethod.Put, fileName))
        {
            using (var content = new ByteArrayContent(buffer, 0, dataSize))
            {
                content.Headers.Add("content-type", contentType);
                request.Content = content;

                try
                {
                    response = await Send(request, payloadHash, cancellation).ConfigureAwait(false);
                }
                finally
                {
                    bufferPool.Return(buffer);
                }
            }
        }

        if (response.StatusCode == HttpStatusCode.OK)
        {
            response.Dispose();
            return true;
        }

        Errors.UnexpectedResult(response);
        return false;
    }

    public Task<bool> PutFile(
        string fileName, byte[] data, string contentType,
        CancellationToken cancellation) => PutFile(fileName, data, 0, data.Length, contentType, cancellation);

    public async Task<bool> PutFile(
        string fileName, byte[] data, int offset, int count, string contentType,
        CancellationToken cancellation)
    {
        var payloadHash = GetPayloadHash(data);

        HttpResponseMessage response;
        using (var request = CreateRequest(HttpMethod.Put, fileName))
        {
            using (var content = new ByteArrayContent(data, offset, count))
            {
                content.Headers.Add("content-type", contentType);
                request.Content = content;

                response = await Send(request, payloadHash, cancellation).ConfigureAwait(false);
            }
        }

        if (response.StatusCode == HttpStatusCode.OK)
        {
            response.Dispose();
            return true;
        }

        Errors.UnexpectedResult(response);
        return false;
    }

    public Task<bool> PutFileMultipart(
        string fileName, Stream data, string contentType,
        CancellationToken cancellation) => PutFileMultipart(fileName, data, contentType, DefaultPartSize, cancellation);

    public async Task<bool> PutFileMultipart(
        string fileName, Stream data, string contentType, int partSize,
        CancellationToken cancellation)
    {
        var dataLength = data.Length;
        fileName = HttpHelper.EncodeName(fileName);

        var uploadId = await MultipartStart(fileName, contentType, cancellation).ConfigureAwait(false);

        var bufferPool = ArrayPool<byte>.Shared;
        var stringPool = ArrayPool<string>.Shared;

        var tags = stringPool.Rent((int) (dataLength / partSize));
        var tagsCount = 0;
        var buffer = bufferPool.Rent(partSize);

        while (data.Position < dataLength)
        {
            tagsCount += 1;
            var chunkSize = await data.ReadAsync(buffer, cancellation).ConfigureAwait(false);

            string? eTag = null;
            try
            {
                var uploadTask = MultipartUpload(fileName, uploadId, tagsCount, buffer, chunkSize, cancellation);
                eTag = await uploadTask.ConfigureAwait(false);
            }
            finally
            {
                if (string.IsNullOrEmpty(eTag))
                {
                    bufferPool.Return(buffer);
                    stringPool.Return(tags);

                    await MultipartAbort(fileName, uploadId, cancellation).ConfigureAwait(false);
                }
            }

            if (string.IsNullOrEmpty(eTag)) return false;
            tags[tagsCount - 1] = eTag;
        }

        bufferPool.Return(buffer);

        var completeResult = false;
        try
        {
            var completeTask = MultipartComplete(fileName, uploadId, tags, tagsCount, cancellation);
            completeResult = await completeTask.ConfigureAwait(false);
        }
        finally
        {
            stringPool.Return(tags);
            if (!completeResult) await MultipartAbort(fileName, uploadId, cancellation).ConfigureAwait(false);
        }

        return completeResult;
    }

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
    public Task<bool> UploadFile(string fileName, Stream data, string contentType, CancellationToken cancellation)
    {
        return data.Length is > 0 and > DefaultPartSize
            ? PutFileMultipart(fileName, data, contentType, DefaultPartSize, cancellation)
            : PutFile(fileName, data, contentType, cancellation);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private HttpRequestMessage CreateRequest(HttpMethod method, string? fileName = null)
    {
        var url = new ValueStringBuilder(stackalloc char[512]);
        url.Append(_bucket);

        // ReSharper disable once InvertIf
        if (!string.IsNullOrEmpty(fileName))
        {
            url.Append('/');
            HttpHelper.AppendEncodedName(ref url, fileName);
        }

        return new HttpRequestMessage(method, new Uri(url.Flush(), UriKind.Absolute));
    }

    private Task<HttpResponseMessage> Send(
        HttpRequestMessage request, string payloadHash,
        CancellationToken cancellation)
    {
        var now = DateTime.UtcNow;

        var headers = request.Headers;
        headers.Add("host", _endpoint);
        headers.Add("x-amz-content-sha256", payloadHash);
        headers.Add("x-amz-date", now.ToString(Signature.Iso8601DateTime, CultureInfo.InvariantCulture));

        if (_useHttp2) request.Version = HttpVersion.Version20;

        var signature = _signature.Calculate(request, payloadHash, S3Headers, now);
        headers.TryAddWithoutValidation("Authorization", _http.BuildHeader(now, signature));

        return _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}