﻿using System.Buffers;
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

        _bucket = ($"{scheme}://{settings.EndPoint}{port}/{bucket}");
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
        using var request = CreateRequest(HttpMethod.Put);
        using var response = await Send(request, EmptyPayloadHash, cancellation);

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
        using var response = await Send(request, EmptyPayloadHash, cancellation);

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
        using var response = await Send(request, EmptyPayloadHash, cancellation);

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

    /// <summary>
    /// Deletes the file from storage
    /// </summary>
    /// <param name="fileName">Name of the file to be deleted</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <remarks>Storage sends the same response in the following cases: the file was indeed deleted, the file did not exist</remarks>
    /// <exception cref="HttpRequestException">Connection problems or status code isn't <see cref="HttpStatusCode.NoContent"/></exception>
    public async Task DeleteFile(string fileName, CancellationToken cancellation)
    {
        using var request = CreateRequest(HttpMethod.Delete, fileName);
        using var response = await Send(request, EmptyPayloadHash, cancellation);

        if (response.StatusCode != HttpStatusCode.NoContent) Errors.UnexpectedResult(response);
    }

    public async Task<bool> FileExists(string fileName, CancellationToken cancellation)
    {
        using var request = CreateRequest(HttpMethod.Head, fileName);
        using var response = await Send(request, EmptyPayloadHash, cancellation);

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

    /// <summary>
    /// Gets file data from the storage
    /// </summary>
    /// <param name="fileName">File name than will be received</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns>Wrapper around <see cref="HttpResponseMessage"/> with the data of the file from storage</returns>
    /// <exception cref="HttpRequestException">Connection problems or status code isn't <see cref="HttpStatusCode.NoContent"/></exception>
    public async Task<StorageFile> GetFile(string fileName, CancellationToken cancellation)
    {
        using var request = CreateRequest(HttpMethod.Get, fileName);
        var response = await Send(request, EmptyPayloadHash, cancellation);

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                return new StorageFile(response, await response.Content.ReadAsStreamAsync(cancellation));
            case HttpStatusCode.NotFound:
                response.Dispose();
                return new StorageFile(response, Stream.Null);
            default:
                Errors.UnexpectedResult(response);
                return new StorageFile();
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
        return await FileExists(fileName, cancellation)
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

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        using var response = await Send(request, EmptyPayloadHash, cancellation);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellation);
            while (responseStream.CanRead)
            {
                var readString = XmlStreamReader.ReadString(responseStream, "Key");
                if (string.IsNullOrEmpty(readString)) break;

                yield return readString;
            }

            yield break;
        }

        Errors.UnexpectedResult(response);
    }

    public async Task<bool> MultipartAbort(string fileName, string uploadId, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{_bucket}/{fileName}?uploadId={uploadId}");
        using var response = await Send(request, EmptyPayloadHash, cancellation);

        return response is {IsSuccessStatusCode: true, StatusCode: HttpStatusCode.NoContent};
    }

    /// <summary>
    /// Completes multipart upload operation
    /// </summary>
    /// <param name="fileName">Name of file</param>
    /// <param name="uploadId">Identity of upload</param>
    /// <param name="partTags">Tags of parts, sorted by partNumber</param>
    /// <param name="cancellation">Cancellation token</param>
    /// <returns>Returns <b>true</b> if file has been uploaded or <b>false</b> if not</returns>
    public async Task<bool> MultipartComplete(
        string fileName, string uploadId, ArraySegment<string> partTags,
        CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_bucket}/{fileName}?uploadId={uploadId}");
        var builder = StringUtils.GetBuilder();

        builder.Append("<CompleteMultipartUpload>");
        for (var i = 0; i < partTags.Count; i++)
        {
            builder.Append("<Part>");
            builder.Append("<PartNumber>", i + 1, "</PartNumber>");
            builder.Append("<ETag>", partTags[i], "</ETag>");
            builder.Append("</Part>");
        }

        var data = builder
            .Append("</CompleteMultipartUpload>")
            .Flush();

        using var content = new StringContent(data, Encoding.UTF8);
        request.Content = content;

        using var response = await Send(request, GetPayloadHash(data), cancellation);
        return response is {IsSuccessStatusCode: true, StatusCode: HttpStatusCode.OK};
    }

    public async Task<string> MultipartStart(string fileName, string fileType, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_bucket}/{fileName}?uploads");

        using var content = new ByteArrayContent(Array.Empty<byte>());
        content.Headers.Add("content-type", fileType);
        request.Content = content;

        using var response = await Send(request, EmptyPayloadHash, cancellation);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellation);
            return XmlStreamReader.ReadString(responseStream, "UploadId");
        }

        Errors.UnexpectedResult(response);
        return string.Empty;
    }

    public async Task<string?> MultipartUpload(
        string fileName, string uploadId,
        int partNumber, byte[] partData, int partSize,
        CancellationToken cancellation)
    {
        var uri = $"{_bucket}/{fileName}?partNumber={partNumber}&uploadId={uploadId}";
        using var request = new HttpRequestMessage(HttpMethod.Put, uri);

        using var content = new ByteArrayContent(partData, 0, partSize);
        content.Headers.Add("content-length", partSize.ToString());
        request.Content = content;

        var payloadHash = GetPayloadHash(partData.AsSpan(0, partSize));
        using var response = await Send(request, payloadHash, cancellation);

        return response is {IsSuccessStatusCode: true, StatusCode: HttpStatusCode.OK}
            ? response.Headers.ETag?.Tag
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

        var payloadHash = GetPayloadHash(buffer.AsSpan(0, dataSize));
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

        using var response = await Send(request, GetPayloadHash(data), cancellation);

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

        var uploadId = await MultipartStart(fileName, contentType, cancellation);

        var bufferPool = ArrayPool<byte>.Shared;
        var stringPool = ArrayPool<string>.Shared;

        var chunkTags = stringPool.Rent((int) (dataLength / partSize));
        var chunkNumber = 0;
        var buffer = bufferPool.Rent(partSize);

        while (data.Position < dataLength)
        {
            chunkNumber += 1;
            var chunkSize = await data.ReadAsync(buffer, cancellation);

            var eTag = await MultipartUpload(fileName, uploadId, chunkNumber, buffer, chunkSize, cancellation);
            if (string.IsNullOrEmpty(eTag))
            {
                bufferPool.Return(buffer);
                stringPool.Return(chunkTags);

                await MultipartAbort(fileName, uploadId, cancellation);
                return false;
            }

            chunkTags[chunkNumber - 1] = eTag;
        }

        bufferPool.Return(buffer);

        var tags = new ArraySegment<string>(chunkTags, 0, chunkNumber);
        var uploadEndResult = await MultipartComplete(fileName, uploadId, tags, cancellation);
        if (!uploadEndResult)
        {
            await MultipartAbort(fileName, uploadId, cancellation);
            return false;
        }

        stringPool.Return(chunkTags);

        return true;
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