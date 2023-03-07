using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;

namespace Storage;

[DebuggerDisplay("{ToString()}")]
public readonly struct StorageFile : IAsyncDisposable, IDisposable
{
    public string? ContentType
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _response.Content.Headers.ContentType?.MediaType;
    }

    public bool Exists
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _response.IsSuccessStatusCode;
    }

    public long? Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _response.Content.Headers.ContentLength;
    }

    public HttpStatusCode StatusCode
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _response.StatusCode;
    }

    private readonly HttpResponseMessage _response;
    private readonly Stream _stream;

    internal StorageFile(HttpResponseMessage response, Stream stream)
    {
        _response = response;
        _stream = stream;
    }

    public Stream GetStream() => _response.IsSuccessStatusCode
        ? new StorageStream(_response, _stream)
        : _stream;

    public void Dispose()
    {
        _response.Dispose();
        _stream.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        _response.Dispose();
        return _stream.DisposeAsync();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator bool(StorageFile file) => file._response.IsSuccessStatusCode;

    public override string ToString()
    {
        if (_response.IsSuccessStatusCode) return $"OK (Length = {Length})";

        var reasonPhrase = _response.ReasonPhrase;
        var statusCode = _response.StatusCode;
        return string.IsNullOrEmpty(reasonPhrase)
            ? $"{statusCode} ({(int) statusCode})"
            : $"{statusCode} ({(int) statusCode}, '{reasonPhrase}')";
    }
}