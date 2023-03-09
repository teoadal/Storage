using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;

namespace Storage;

/// <summary>
/// Wrapper around <see cref="HttpResponseMessage"/> with a data of a file from storage
/// </summary>
[DebuggerDisplay("{ToString()}")]
public readonly struct StorageFile : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Type of file content in MIME
    /// </summary>
    /// <remarks>It will be take from header "Content-Type" of <see cref="HttpResponseMessage"/></remarks>
    public string? ContentType
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _response.Content.Headers.ContentType?.MediaType;
    }

    /// <summary>
    /// Is the file data received successfully?
    /// </summary>
    public bool Exists
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Length of file data
    /// </summary>
    /// <remarks>It will be take from header "Content-Length" of <see cref="HttpResponseMessage"/></remarks>
    public long? Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _response.Content.Headers.ContentLength;
    }

    /// <summary>
    /// The code of storage response 
    /// </summary>
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

    /// <summary>
    /// Gets stream of the file data from <see cref="HttpResponseMessage"/>
    /// </summary>
    /// <returns>Stream of data</returns>
    /// <remarks>When stream will be closed the <see cref="HttpResponseMessage"/> will be disposed</remarks>
    public Stream GetStream() => _response.IsSuccessStatusCode
        ? new StorageStream(_response, _stream)
        : _stream;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator bool(StorageFile file) => file._response.IsSuccessStatusCode;

    [ExcludeFromCodeCoverage]
    public override string ToString()
    {
        if (_response.IsSuccessStatusCode) return $"OK (Length = {Length})";

        var reasonPhrase = _response.ReasonPhrase;
        var statusCode = _response.StatusCode;
        return string.IsNullOrEmpty(reasonPhrase)
            ? $"{statusCode} ({(int) statusCode})"
            : $"{statusCode} ({(int) statusCode}, '{reasonPhrase}')";
    }

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
}