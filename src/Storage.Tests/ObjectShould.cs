using System.Net;
using FluentAssertions;
using Storage.Tests.Mocks;

namespace Storage.Tests;

public sealed class ObjectShould : IClassFixture<StorageFixture>
{
    private readonly CancellationToken _cancellation;
    private readonly StorageClient _client;
    private readonly StorageFixture _fixture;

    public ObjectShould(StorageFixture fixture)
    {
        _cancellation = CancellationToken.None;
        _client = fixture.StorageClient;
        _fixture = fixture;
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
    public async Task DisposeFileStream()
    {
        var fileName = await CreateTestFile();
        await using var fileGetResult = await _client.GetFile(fileName, _cancellation);

        var fileStream = fileGetResult.GetStream();
        await fileStream.DisposeAsync();

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
    public async Task HasValidInformation()
    {
        const int length = 1 * 1024 * 1024;
        const string contentType = "video/mp4";

        var fileName = await CreateTestFile(contentType);
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
            .Status
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
    public async Task PutAsByteArray()
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
    public async Task PutMultipart()
    {
        var fileName = _fixture.Create<string>();
        using var data = StorageFixture.GetByteStream(12 * 1024 * 1024); // 12 Mb
        var filePutResult = await _client.UploadFile(fileName, data, StorageFixture.StreamContentType, _cancellation);

        filePutResult
            .Should().BeTrue();

        await EnsureFileSame(fileName, data.ToArray());
        await DeleteTestFile(fileName);
    }

    private async Task<string> CreateTestFile(string contentType = StorageFixture.StreamContentType, int? length = null)
    {
        var fileName = _fixture.Create<string>();
        using var data = StorageFixture.GetByteStream(length ?? 1 * 1024 * 1024); // 1 Mb
        await _client.PutFile(fileName, data, contentType, _cancellation);
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

    private async Task DeleteTestFile(string fileName)
    {
        var deleteFileResult = await _client.DeleteFile(fileName, _cancellation);

        deleteFileResult
            .Should().BeTrue();
    }
}