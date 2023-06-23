using System.Buffers;
using System.Runtime.CompilerServices;
using Storage.Utils;

namespace Storage;

public sealed class StorageUpload : IDisposable
{
    public readonly string FileName;
    public readonly string UploadId;

    private byte[]? _byteBuffer;
    private readonly StorageClient _client;
    private readonly string _encodedFileName;
    private bool _disposed;
    private string[] _parts;
    private int _partCount;

    internal StorageUpload(StorageClient client, string fileName, string encodedFileName, string uploadId)
    {
        FileName = fileName;
        UploadId = uploadId;

        _client = client;
        _encodedFileName = encodedFileName;
        _parts = ArrayPool<string>.Shared.Rent(16);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task Abort(CancellationToken cancellation)
    {
        return _client.MultipartAbort(_encodedFileName, UploadId, cancellation);
    }

    public Task<bool> Complete(CancellationToken cancellation) => _partCount == 0
        ? Task.FromResult(false)
        : _client.MultipartComplete(_encodedFileName, UploadId, _parts, _partCount, cancellation);

    public Task<bool> Upload(Stream data, CancellationToken cancellation)
    {
        _byteBuffer ??= ArrayPool<byte>.Shared.Rent(StorageClient.DefaultPartSize);
        return Upload(data, _byteBuffer, cancellation);
    }

    public async Task<bool> Upload(Stream data, byte[] buffer, CancellationToken cancellation)
    {
        while (true)
        {
            var written = await data.ReadTo(buffer, cancellation);
            if (written == 0) break;
            if (!await Upload(buffer, written, cancellation)) return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<bool> Upload(byte[] data, CancellationToken cancellation) => Upload(data, data.Length, cancellation);

    public async Task<bool> Upload(byte[] data, int length, CancellationToken cancellation)
    {
        var partId = await _client.MultipartUpload(
            _encodedFileName, UploadId, _partCount + 1, data, length,
            cancellation);

        if (string.IsNullOrEmpty(partId)) return false;

        if (_parts.Length == _partCount) CollectionUtils.Resize(ref _parts, ArrayPool<string>.Shared, _partCount * 2);
        _parts[_partCount++] = partId;

        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;

        Array.Clear(_parts, 0, _partCount);
        ArrayPool<string>.Shared.Return(_parts);
        _parts = null!;

        if (_byteBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(_byteBuffer);
            _byteBuffer = null;
        }

        _disposed = true;
    }
}