using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Storage.Utils;

namespace Storage;

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

    public StorageClient(StorageSettings settings, HttpClient? client = null)
    {
        Bucket = settings.Bucket;

        var scheme = settings.UseHttps ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
        _bucket = ($"{scheme}://{settings.EndPoint}:{settings.Port}/{settings.Bucket}");
        _client = client ?? new HttpClient();
        _endpoint = $"{settings.EndPoint}:{settings.Port}";
        _http = new HttpHelper(settings.AccessKey, settings.Region, settings.Service, S3Headers);
        _signature = new Signature(settings.SecretKey, settings.Region, settings.Service);
    }

    public string BuildFileUrl(string fileName, TimeSpan expiration)
    {
        var expires = expiration.TotalSeconds.ToString(CultureInfo.InvariantCulture);
        var now = DateTime.UtcNow;

        var url = _http.BuildUrl(_bucket, fileName, now, expires);
        var signature = _signature.CalculateUrlSignature(url, now);

        return $"{url}&X-Amz-Signature={signature}";
    }

    public async Task<bool> CreateBucket(CancellationToken cancellation)
    {
        using var request = CreateRequest(HttpMethod.Put);
        using var response = await Send(request, Signature.EmptyPayloadHash, cancellation);

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                return true;
            case HttpStatusCode.Conflict: // already exists
                return false;
            default:
                Errors.UnexpectedResult(response);
                return false;
        }
    }

    public async Task<bool> BucketExists(CancellationToken cancellation)
    {
        using var request = CreateRequest(HttpMethod.Head);
        using var response = await Send(request, Signature.EmptyPayloadHash, cancellation);

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                return true;
            case HttpStatusCode.NotFound:
                return false;
            default:
                Errors.UnexpectedResult(response);
                return false;
        }
    }

    public async Task<bool> DeleteBucket(CancellationToken cancellation)
    {
        using var request = CreateRequest(HttpMethod.Delete);
        using var response = await Send(request, Signature.EmptyPayloadHash, cancellation);

        switch (response.StatusCode)
        {
            case HttpStatusCode.NoContent:
                return true;
            case HttpStatusCode.NotFound:
                return false;
            default:
                Errors.UnexpectedResult(response);
                return false;
        }
    }

    public async Task DeleteFile(string fileName, CancellationToken cancellation)
    {
        using var request = CreateRequest(HttpMethod.Delete, fileName);
        using var response = await Send(request, Signature.EmptyPayloadHash, cancellation);

        if (response.StatusCode != HttpStatusCode.NoContent) Errors.UnexpectedResult(response);
    }

    public async Task<bool> FileExists(string fileName, CancellationToken cancellation)
    {
        using var request = CreateRequest(HttpMethod.Head, fileName);
        using var response = await Send(request, Signature.EmptyPayloadHash, cancellation);

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                return true;
            case HttpStatusCode.NotFound:
                return false;
            default:
                Errors.UnexpectedResult(response);
                return false;
        }
    }

    public async Task<StorageFile> GetFile(string fileName, CancellationToken cancellation)
    {
        using var request = CreateRequest(HttpMethod.Get, fileName);
        var response = await Send(request, Signature.EmptyPayloadHash, cancellation);

        if (response is {IsSuccessStatusCode: true, StatusCode: HttpStatusCode.OK})
        {
            return new StorageFile(response, await response.Content.ReadAsStreamAsync(cancellation));
        }

        response.Dispose();
        return new StorageFile(response, Stream.Null);
    }

    public async Task<string?> GetFileUrl(string fileName, TimeSpan expiration, CancellationToken cancellation)
    {
        return await FileExists(fileName, cancellation)
            ? BuildFileUrl(fileName, expiration)
            : null;
    }

    public async Task<bool> PutFile(string fileName, Stream data, string contentType, CancellationToken cancellation)
    {
        using var request = CreateRequest(HttpMethod.Put, fileName);

        var bufferPool = ArrayPool<byte>.Shared;
        var buffer = bufferPool.Rent((int) data.Length);
        var dataSize = await data.ReadAsync(buffer, cancellation);

        using var content = new ByteArrayContent(buffer, 0, dataSize);
        content.Headers.Add("content-type", contentType);
        request.Content = content;

        var payloadHash = Signature.GetPayloadHash(buffer.AsSpan(0, dataSize));
        bufferPool.Return(buffer);

        using var response = await Send(request, payloadHash, cancellation);

        if (response.StatusCode == HttpStatusCode.OK) return true;
        Errors.UnexpectedResult(response);
        return false;
    }

    public async Task<bool> PutFile(
        string fileName, byte[] data, string contentType,
        CancellationToken cancellation)
    {
        using var request = CreateRequest(HttpMethod.Put, fileName);

        using var content = new ByteArrayContent(data);
        content.Headers.Add("content-type", contentType);
        request.Content = content;

        using var response = await Send(request, Signature.GetPayloadHash(data), cancellation);

        if (response.StatusCode == HttpStatusCode.OK) return true;
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

        var uploadId = await UploadChunksStart(fileName, contentType, cancellation);
        if (string.IsNullOrEmpty(uploadId)) return false;

        var bufferPool = ArrayPool<byte>.Shared;
        var stringPool = ArrayPool<string>.Shared;

        var chunkTags = stringPool.Rent((int) (dataLength / partSize));
        var chunkNumber = 0;
        var buffer = bufferPool.Rent(partSize);

        while (data.Position != dataLength)
        {
            chunkNumber += 1;
            var chunkSize = await data.ReadAsync(buffer, cancellation);

            var eTag = await UploadChunk(fileName, uploadId, chunkNumber, buffer, chunkSize, cancellation);
            if (string.IsNullOrEmpty(eTag))
            {
                bufferPool.Return(buffer);
                stringPool.Return(chunkTags);

                await UploadChunkAbort(fileName, uploadId, cancellation);
                return false;
            }

            chunkTags[chunkNumber - 1] = eTag;
        }

        bufferPool.Return(buffer);

        var tags = new ArraySegment<string>(chunkTags, 0, chunkNumber);
        var uploadEndResult = await UploadChunksEnd(fileName, uploadId, tags, cancellation);
        if (!uploadEndResult)
        {
            await UploadChunkAbort(fileName, uploadId, cancellation);
            return false;
        }

        stringPool.Return(chunkTags);

        return true;
    }

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

        var signature = _signature.CalculateRequestSignature(request, payloadHash, S3Headers, now);
        headers.TryAddWithoutValidation("Authorization", _http.BuildHeader(now, signature));

        return _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
    }

    private async Task<string?> UploadChunk(
        string fileName, string uploadId,
        int chunkNumber, byte[] chunkData, int chunkSize,
        CancellationToken cancellation)
    {
        var uri = $"{_bucket}/{fileName}?partNumber={chunkNumber}&uploadId={uploadId}";
        using var request = new HttpRequestMessage(HttpMethod.Put, uri);

        using var content = new ByteArrayContent(chunkData, 0, chunkSize);
        content.Headers.Add("content-length", chunkSize.ToString());
        request.Content = content;

        var payloadHash = Signature.GetPayloadHash(chunkData.AsSpan(0, chunkSize));
        using var response = await Send(request, payloadHash, cancellation);

        return response is {IsSuccessStatusCode: true, StatusCode: HttpStatusCode.OK}
            ? response.Headers.ETag?.Tag
            : null;
    }

    private async Task<bool> UploadChunkAbort(string fileName, string uploadId, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{_bucket}/{fileName}?uploadId={uploadId}");
        using var response = await Send(request, Signature.EmptyPayloadHash, cancellation);

        return response is {IsSuccessStatusCode: true, StatusCode: HttpStatusCode.OK};
    }

    private async Task<bool> UploadChunksEnd(
        string fileName, string uploadId, ArraySegment<string> chunkTags,
        CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_bucket}/{fileName}?uploadId={uploadId}");
        var builder = StringUtils.GetBuilder();

        builder.Append("<CompleteMultipartUpload>");
        for (var i = 0; i < chunkTags.Count; i++)
        {
            builder.Append("<Part>");
            builder.Append("<PartNumber>", i + 1, "</PartNumber>");
            builder.Append("<ETag>", chunkTags[i], "</ETag>");
            builder.Append("</Part>");
        }

        var data = builder
            .Append("</CompleteMultipartUpload>")
            .Flush();

        using var content = new StringContent(data, Encoding.UTF8);
        request.Content = content;

        using var response = await Send(request, Signature.GetPayloadHash(data), cancellation);
        return response is {IsSuccessStatusCode: true, StatusCode: HttpStatusCode.OK};
    }

    private async Task<string?> UploadChunksStart(string fileName, string fileType, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_bucket}/{fileName}?uploads");

        using var content = new ByteArrayContent(Array.Empty<byte>());
        content.Headers.Add("content-type", fileType);
        request.Content = content;

        using var response = await Send(request, Signature.EmptyPayloadHash, cancellation);
        return response is {IsSuccessStatusCode: true, StatusCode: HttpStatusCode.OK}
            ? MultipartUploadResult.GetUploadId(await response.Content.ReadAsStreamAsync(cancellation))
            : null;
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}