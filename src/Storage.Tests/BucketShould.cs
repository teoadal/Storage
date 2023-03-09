using FluentAssertions;
using Storage.Tests.Mocks;

namespace Storage.Tests;

public sealed class BucketShould : IClassFixture<StorageFixture>
{
    private readonly CancellationToken _cancellation;
    private readonly StorageClient _client;
    private readonly StorageFixture _fixture;

    public BucketShould(StorageFixture fixture)
    {
        _cancellation = CancellationToken.None;
        _client = fixture.StorageClient;
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateBucket()
    {
        var settings = _fixture.Settings;
        
        // don't use using here
        var client = new StorageClient(new StorageSettings
        {
            AccessKey = settings.AccessKey,
            Bucket = _fixture.Create<string>(),
            EndPoint = settings.EndPoint,
            Port = settings.Port,
            SecretKey = settings.SecretKey,
            UseHttps = settings.UseHttps
        }, _fixture.HttpClient);

        var bucketCreateResult = await client.CreateBucket(_cancellation);

        bucketCreateResult
            .Should().BeTrue();

        await DeleteTestBucket(client);
    }

    [Fact]
    public async Task BeExists()
    {
        var bucketExistsResult = await _client.BucketExists(_cancellation);

        bucketExistsResult
            .Should().BeTrue();
    }

    [Fact]
    public async Task BeNotExists()
    {
        var settings = _fixture.Settings;
        
        // don't dispose it
        var client = new StorageClient(new StorageSettings
        {
            AccessKey = settings.AccessKey,
            Bucket = _fixture.Create<string>(),
            EndPoint = settings.EndPoint,
            Port = settings.Port,
            SecretKey = settings.SecretKey,
            UseHttps = settings.UseHttps
        }, _fixture.HttpClient);

        var bucketExistsResult = await client.BucketExists(_cancellation);

        bucketExistsResult
            .Should().BeFalse();
    }

    [Fact]
    public Task NotThrowIfCreateBucketAlreadyExists()
    {
        return _client
            .Invoking(client => client.CreateBucket(_cancellation))
            .Should().NotThrowAsync();
    }
    
    [Fact]
    public Task NotThrowIfDeleteNotExistsBucket()
    {
        var settings = _fixture.Settings;
        
        // don't use using here
        var client = new StorageClient(new StorageSettings
        {
            AccessKey = settings.AccessKey,
            Bucket = _fixture.Create<string>(),
            EndPoint = settings.EndPoint,
            Port = settings.Port,
            SecretKey = settings.SecretKey,
            UseHttps = settings.UseHttps
        }, _fixture.HttpClient);
        
        return client
            .Invoking(c => c.DeleteBucket(_cancellation))
            .Should().NotThrowAsync();
    }

    private async Task DeleteTestBucket(StorageClient client)
    {
        var bucketDeleteResult = await client.DeleteBucket(_cancellation);

        bucketDeleteResult
            .Should().BeTrue();
    }
}