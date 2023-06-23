using System.Diagnostics.CodeAnalysis;

namespace Storage;

internal sealed class S3Stream : Stream
{
    public override long Length
    {
        get
        {
            _length ??= _response.Content.Headers.ContentLength ?? _stream.Length;
            return _length.Value;
        }
    }

    private long? _length;
    private readonly Stream _stream;
    private readonly HttpResponseMessage _response;

    public S3Stream(HttpResponseMessage response, Stream stream)
    {
        _response = response;
        _stream = stream;
    }

    protected override void Dispose(bool disposing)
    {
        _stream.Dispose();
        _response.Dispose();
    }

    #region Contract

    public override bool CanRead => _stream.CanRead;

    public override bool CanSeek => _stream.CanSeek;

    public override bool CanWrite => _stream.CanWrite;

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return _stream.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
    {
        return _stream.ReadAsync(buffer, cancellationToken);
    }

    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        return _stream.CopyToAsync(destination, bufferSize, cancellationToken);
    }

    [ExcludeFromCodeCoverage]
    public override void Flush() => _stream.Flush();

    [ExcludeFromCodeCoverage]
    public override long Position
    {
        get => _stream.Position;
        set => _stream.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);

    [ExcludeFromCodeCoverage]
    public override long Seek(long offset, SeekOrigin origin) => _stream.Seek(offset, origin);

    [ExcludeFromCodeCoverage]
    public override void SetLength(long value) => _stream.SetLength(value);

    [ExcludeFromCodeCoverage]
    public override void Write(byte[] buffer, int offset, int count) => _stream.Write(buffer, offset, count);

    #endregion
}