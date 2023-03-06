using System.Net;
using System.Runtime.CompilerServices;

namespace Storage;

public readonly struct StorageFile : IAsyncDisposable
{
    public string? ContentType
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _response.Content.Headers.ContentType?.MediaType;
    }

    public bool IsSuccess
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _response.IsSuccessStatusCode;
    }

    public long? Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _response.Content.Headers.ContentLength;
    }

    public HttpStatusCode Status
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

    public ValueTask DisposeAsync()
    {
        _response.Dispose();
        return _stream.DisposeAsync();
    }
}