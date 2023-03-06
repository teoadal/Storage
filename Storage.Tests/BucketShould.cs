using FluentAssertions;
using Storage.Tests.Mocks;

namespace Storage.Tests;

public sealed class BucketShould : IClassFixture<StorageFixture>
{
    private readonly StorageClient _client;
    private readonly StorageFixture _fixture;

    public BucketShould(StorageFixture fixture)
    {
        _client = fixture.Client;
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateBucket()
    {
        var settings = _fixture.Settings;
        using var client = new StorageClient(new StorageSettings
        {
            AccessKey = settings.AccessKey,
            Bucket = _fixture.Create<string>(),
            EndPoint = settings.EndPoint,
            Port = settings.Port,
            SecretKey = settings.SecretKey,
            UseHttps = settings.UseHttps
        });

        var bucketCreateResult = await client.CreateBucket(CancellationToken.None);
        
        bucketCreateResult
            .Should().BeTrue();

        await DeleteTestBucket(client);
    }
    
    [Fact]
    public async Task BeExists()
    {
        var bucketExistsResult = await _client.BucketExists(CancellationToken.None);

        bucketExistsResult
            .Should().BeTrue();
    }

    [Fact]
    public async Task BeNotExists()
    {
        var settings = _fixture.Settings;
        using var client = new StorageClient(new StorageSettings
        {
            AccessKey = settings.AccessKey,
            Bucket = _fixture.Create<string>(),
            EndPoint = settings.EndPoint,
            Port = settings.Port,
            SecretKey = settings.SecretKey,
            UseHttps = settings.UseHttps
        });

        var bucketExistsResult = await client.BucketExists(CancellationToken.None);

        bucketExistsResult
            .Should().BeFalse();
    }
    
    private static async Task DeleteTestBucket(StorageClient client)
    {
        var bucketDeleteResult = await client.DeleteBucket(CancellationToken.None);
        
        bucketDeleteResult
            .Should().BeTrue();
    }
}