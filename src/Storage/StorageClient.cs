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

[DebuggerDisplay("Client for '{Bucket}'")]
[SuppressMessage("ReSharper", "SwitchStatementHandlesSomeKnownEnumValuesWithDefault")]
public sealed class StorageClient
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
    private bool _disposed;
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

    public void Dispose()
    {
        if (_disposed) return;

        _client.Dispose();

        _disposed = true;
    }

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

    public async Task<string?> GetFileUrl(string fileName, TimeSpan expiration, CancellationToken cancellation)
    {
        return await IsFileExists(fileName, cancellation).ConfigureAwait(false)
            ? BuildFileUrl(fileName, expiration)
            : null;
    }

    public async Task<bool> IsBucketExists(CancellationToken cancellation)
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

    public async Task<bool> IsFileExists(string fileName, CancellationToken cancellation)
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

    public async IAsyncEnumerable<string> List(string? prefix, [EnumeratorCancellation] CancellationToken cancellation)
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

    public Task<bool> UploadFile(string fileName, Stream data, string contentType, CancellationToken cancellation)
    {
        var length = StreamUtils.TryGetLength(data);

        return length is null or > DefaultPartSize
            ? PutFileMultipart(fileName, data, contentType, DefaultPartSize, length, cancellation)
            : PutFile(fileName, data, contentType, cancellation);
    }

    public Task<bool> UploadFile(
        string fileName, byte[] data, string contentType,
        CancellationToken cancellation) => UploadFile(fileName, data, 0, data.Length, contentType, cancellation);

    public async Task<bool> UploadFile(
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

    private async Task<bool> PutFile(string fileName, Stream data, string contentType, CancellationToken cancellation)
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

    private async Task<bool> PutFileMultipart(
        string fileName, Stream data, string contentType, int partSize, long? length,
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

    private Task<HttpResponseMessage> Send(
        HttpRequestMessage request, string payloadHash,
        CancellationToken cancellation)
    {
        if (_disposed) Errors.Disposed(); 
        
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
}