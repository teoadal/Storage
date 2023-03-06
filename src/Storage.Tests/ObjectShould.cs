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
        _client = fixture.Client;
        _fixture = fixture;
    }

    [Fact]
    public async Task BeExists()
    {
        var fileName = _fixture.Create<string>();
        using var data = StorageFixture.GetByteStream(1 * 1024 * 1024); // 1 Mb
        await _client.PutFile(fileName, data, StorageFixture.StreamContentType, _cancellation);

        var fileExistsResult = await _client.FileExists(fileName, _cancellation);

        fileExistsResult
            .Should().BeTrue();
    }

    [Fact]
    public async Task BeNotExists()
    {
        var fileExistsResult = await _client.FileExists(_fixture.Create<string>(), _cancellation);

        fileExistsResult
            .Should().BeFalse();
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

    private async Task EnsureFileSame(string fileName, byte[] expectedBytes)
    {
        await using var getFileResult = await _client.GetFile(fileName, _cancellation);

        getFileResult
            .IsSuccess
            .Should().BeTrue();

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