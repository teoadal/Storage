using System.Net;
using FluentAssertions;
using Storage.Tests.Mocks;

namespace Storage.Tests;

public sealed class ObjectShould : IClassFixture<StorageFixture>
{
    private readonly CancellationToken _cancellation;
    private readonly StorageClient _client;
    private readonly StorageFixture _fixture;
    private readonly StorageClient _notExistsBucketClient; // don't dispose it

    public ObjectShould(StorageFixture fixture)
    {
        _cancellation = CancellationToken.None;
        _client = fixture.StorageClient;
        _fixture = fixture;

        var settings = _fixture.Settings;
        _notExistsBucketClient = new StorageClient(new StorageSettings
        {
            AccessKey = settings.AccessKey,
            Bucket = _fixture.Create<string>(),
            EndPoint = settings.EndPoint,
            Port = settings.Port,
            SecretKey = settings.SecretKey,
            UseHttps = settings.UseHttps
        }, _fixture.HttpClient);
    }

    [Fact]
    public async Task AboutMultipartUpload()
    {
        var fileName = _fixture.Create<string>();

        var uploadId = await _client.MultipartStart(fileName, StorageFixture.StreamContentType, _cancellation);


        var part = StorageFixture.GetByteArray();
        await _client
            .Invoking(client => client.MultipartUpload(fileName, uploadId, 1, part, part.Length, _cancellation))
            .Should().NotThrowAsync();

        var abortResult = await _client
            .Invoking(client => client.MultipartAbort(fileName, uploadId, _cancellation))
            .Should().NotThrowAsync();

        abortResult
            .Which
            .Should().BeTrue();
    }

    [Fact]
    public void BuildUrl()
    {
        var fileName = _fixture.Create<string>();

        _client
            .Invoking(client => client.BuildFileUrl(fileName, TimeSpan.FromSeconds(100)))
            .Should().NotThrow()
            .Which.Should().NotBeNull();
    }

    [Fact]
    public async Task BeExists()
    {
        var fileName = await CreateTestFile();
        var fileExistsResult = await _client.FileExists(fileName, _cancellation);

        fileExistsResult
            .Should().BeTrue();

        await DeleteTestFile(fileName);
    }

    [Fact]
    public async Task BeNotExists()
    {
        var fileExistsResult = await _client.FileExists(_fixture.Create<string>(), _cancellation);

        fileExistsResult
            .Should().BeFalse();
    }

    [Fact]
    public async Task DeleteFile()
    {
        var fileName = await CreateTestFile();

        await _client
            .Invoking(client => client.DeleteFile(fileName, _cancellation))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeFileStream()
    {
        var fileName = await CreateTestFile();
        await using var fileGetResult = await _client.GetFile(fileName, _cancellation);

        var fileStream = fileGetResult.GetStream();
        await fileStream.DisposeAsync();

        await DeleteTestFile(fileName);
    }

    [Fact]
    public async Task DisposeStorageFile()
    {
        var fileName = await CreateTestFile();
        await using var fileGetResult = await _client.GetFile(fileName, _cancellation);

        // ReSharper disable once MethodHasAsyncOverload
        fileGetResult.Dispose();

        await DeleteTestFile(fileName);
    }

    [Fact]
    public async Task GetFileUrl()
    {
        var fileName = await CreateTestFile();

        var url = await _client.GetFileUrl(fileName, TimeSpan.FromSeconds(600), _cancellation);

        url.Should().NotBeNull();

        using var response = await _fixture.HttpClient.GetAsync(url, _cancellation);

        await DeleteTestFile(fileName);

        response
            .IsSuccessStatusCode
            .Should().BeTrue(response.ReasonPhrase);
    }

    [Fact]
    public async Task GetFileUrlWithCyrillicName()
    {
        var fileName = await CreateTestFile($"при(ве)+т_как23дела{Guid.NewGuid()}.pdf");

        var url = await _client.GetFileUrl(fileName, TimeSpan.FromSeconds(600), _cancellation);

        url.Should().NotBeNull();

        using var response = await _fixture.HttpClient.GetAsync(url, _cancellation);

        await DeleteTestFile(fileName);

        response
            .IsSuccessStatusCode
            .Should().BeTrue(response.ReasonPhrase);
    }

    [Fact]
    public async Task HasValidInformation()
    {
        const int length = 1 * 1024 * 1024;
        const string contentType = "video/mp4";

        var fileName = await CreateTestFile(contentType: contentType);
        await using var fileGetResult = await _client.GetFile(fileName, _cancellation);

        fileGetResult
            .ContentType
            .Should().Be(contentType);

        fileGetResult
            .Exists
            .Should().BeTrue();

        fileGetResult
            .Length
            .Should().Be(length);

        fileGetResult
            .StatusCode
            .Should().Be(HttpStatusCode.OK);

        await DeleteTestFile(fileName);
    }

    [Fact]
    public async Task HasValidStreamInformation()
    {
        const int length = 1 * 1024 * 1024;
        var fileName = await CreateTestFile(length: length);
        await using var fileGetResult = await _client.GetFile(fileName, _cancellation);

        var fileStream = fileGetResult.GetStream();

        fileStream.CanRead.Should().BeTrue();
        fileStream.CanSeek.Should().BeFalse();
        fileStream.CanWrite.Should().BeFalse();
        fileStream.Length.Should().Be(length);

        await fileStream.DisposeAsync();
        await fileStream.DisposeAsync();

        await DeleteTestFile(fileName);
    }

    [Fact]
    public async Task PutByteArray()
    {
        var fileName = _fixture.Create<string>();
        var data = StorageFixture.GetByteArray(15000);
        var filePutResult = await _client.PutFile(fileName, data, StorageFixture.StreamContentType, _cancellation);

        filePutResult
            .Should().BeTrue();

        await EnsureFileSame(fileName, data);
        await DeleteTestFile(fileName);
    }

    [Fact]
    public async Task PutStream()
    {
        var fileName = _fixture.Create<string>();
        var data = StorageFixture.GetByteStream(15000);
        var filePutResult = await _client.PutFile(fileName, data, StorageFixture.StreamContentType, _cancellation);

        filePutResult
            .Should().BeTrue();

        await EnsureFileSame(fileName, data);
        await DeleteTestFile(fileName);
    }

    [Fact]
    public async Task PutMultipart()
    {
        var fileName = _fixture.Create<string>();
        using var data = StorageFixture.GetByteStream(12 * 1024 * 1024); // 12 Mb
        var filePutResult =
            await _client.PutFileMultipart(fileName, data, StorageFixture.StreamContentType, _cancellation);

        filePutResult
            .Should().BeTrue();

        await EnsureFileSame(fileName, data.ToArray());
        await DeleteTestFile(fileName);
    }

    [Fact]
    public async Task PutMultipartWithPartSize()
    {
        var fileName = _fixture.Create<string>();
        const int partSize = 5 * 1024 * 1024; // 5 Mb 
        using var data = StorageFixture.GetByteStream(12 * 1024 * 1024); // 12 Mb
        var filePutResult =
            await _client.PutFileMultipart(fileName, data, StorageFixture.StreamContentType, partSize, _cancellation);

        filePutResult
            .Should().BeTrue();

        await EnsureFileSame(fileName, data.ToArray());
        await DeleteTestFile(fileName);
    }


    [Fact]
    public async Task Upload()
    {
        var fileName = _fixture.Create<string>();
        using var data = StorageFixture.GetByteStream(12 * 1024 * 1024); // 12 Mb
        var filePutResult = await _client.UploadFile(fileName, data, StorageFixture.StreamContentType, _cancellation);

        filePutResult
            .Should().BeTrue();

        await EnsureFileSame(fileName, data.ToArray());
        await DeleteTestFile(fileName);
    }

    [Fact]
    public async Task UploadCyrillicName()
    {
        var fileName = $"при(ве)+т_как23дела{Guid.NewGuid()}.pdf";
        using var data = StorageFixture.GetByteStream();
        var uploadResult = await _client.UploadFile(fileName, data, StorageFixture.StreamContentType, _cancellation);

        await DeleteTestFile(fileName);

        uploadResult
            .Should().BeTrue();
    }

    [Fact]
    public async Task NotThrowIfFileAlreadyExists()
    {
        var fileName = await CreateTestFile();
        await _client
            .Invoking(client => client.UploadFile(
                fileName, StorageFixture.GetByteStream(),
                StorageFixture.StreamContentType,
                _cancellation))
            .Should().NotThrowAsync();

        await DeleteTestFile(fileName);
    }

    [Fact]
    public Task NotThrowIfFileExistsWithNotExistsBucket()
    {
        return _notExistsBucketClient
            .Invoking(client => client.FileExists(_fixture.Create<string>(), _cancellation))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotThrowIfGetFileUrlWithNotExistsBucket()
    {
        var result = await _notExistsBucketClient
            .Invoking(client => client.GetFileUrl(_fixture.Create<string>(), TimeSpan.FromSeconds(100), _cancellation))
            .Should().NotThrowAsync();

        result
            .Which
            .Should().BeNull();
    }

    [Fact]
    public async Task NotThrowIfFileGetWithNotExistsBucket()
    {
        var result = await _notExistsBucketClient
            .Invoking(client => client.GetFile(_fixture.Create<string>(), _cancellation))
            .Should().NotThrowAsync();

        result
            .Which.Exists
            .Should().BeFalse();
    }

    [Fact]
    public async Task NotThrowIfGetNotExistsFile()
    {
        var fileName = _fixture.Create<string>();
        await _client
            .Invoking(client => client.GetFile(fileName, _cancellation))
            .Should().NotThrowAsync();
    }

    [Fact]
    public Task NotThrowIfDeleteFileNotExists()
    {
        return _client
            .Invoking(client => client.DeleteFile(_fixture.Create<string>(), _cancellation))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task ThrowIfBucketNotExists()
    {
        var fileArray = StorageFixture.GetByteArray();
        var fileName = _fixture.Create<string>();
        var fileStream = StorageFixture.GetByteStream();

        await _notExistsBucketClient
            .Invoking(client => client.DeleteFile(fileName, _cancellation))
            .Should().ThrowAsync<HttpRequestException>();

        await _notExistsBucketClient
            .Invoking(client => client.PutFile(fileName, fileStream, StorageFixture.StreamContentType, _cancellation))
            .Should().ThrowAsync<HttpRequestException>();

        await _notExistsBucketClient
            .Invoking(client => client.PutFile(fileName, fileArray, StorageFixture.StreamContentType, _cancellation))
            .Should().ThrowAsync<HttpRequestException>();

        await _notExistsBucketClient
            .Invoking(client =>
                client.PutFileMultipart(fileName, fileStream, StorageFixture.StreamContentType, _cancellation))
            .Should().ThrowAsync<HttpRequestException>();
    }


    private async Task<string> CreateTestFile(
        string? fileName = null,
        string contentType = StorageFixture.StreamContentType,
        int? length = null)
    {
        fileName ??= _fixture.Create<string>();
        using var data = StorageFixture.GetByteStream(length ?? 1 * 1024 * 1024); // 1 Mb

        var uploadResult = await _client.UploadFile(fileName, data, contentType, _cancellation);

        uploadResult
            .Should().BeTrue();

        return fileName;
    }

    private async Task EnsureFileSame(string fileName, byte[] expectedBytes)
    {
        await using var getFileResult = await _client.GetFile(fileName, _cancellation);

        using var memoryStream = StorageFixture.GetEmptyByteStream(getFileResult.Length);
        await getFileResult.GetStream().CopyToAsync(memoryStream, _cancellation);

        memoryStream
            .ToArray().SequenceEqual(expectedBytes)
            .Should().BeTrue();
    }

    private async Task EnsureFileSame(string fileName, MemoryStream expectedBytes)
    {
        expectedBytes.Seek(0, SeekOrigin.Begin);

        await using var getFileResult = await _client.GetFile(fileName, _cancellation);

        using var memoryStream = StorageFixture.GetEmptyByteStream(getFileResult.Length);
        await getFileResult.GetStream().CopyToAsync(memoryStream, _cancellation);

        memoryStream
            .ToArray().SequenceEqual(expectedBytes.ToArray())
            .Should().BeTrue();
    }

    private Task DeleteTestFile(string fileName) => _client.DeleteFile(fileName, _cancellation);
}