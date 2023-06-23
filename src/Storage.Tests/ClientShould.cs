using FluentAssertions;
using Storage.Tests.Mocks;

namespace Storage.Tests;

public sealed class ClientShould : IClassFixture<StorageFixture>
{
    private readonly S3Client _client;
    private readonly StorageFixture _fixture;

    public ClientShould(StorageFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.S3Client;
    }

    [Fact]
    public void DeserializeSettingsJson()
    {
        var expected = _fixture.Settings;
        
        var json = System.Text.Json.JsonSerializer.Serialize(expected);
        var actual = System.Text.Json.JsonSerializer.Deserialize<S3Settings>(json);

        actual.Should().BeEquivalentTo(expected);
    }
    
    [Fact]
    public void HasValidInfo()
    {
        _client
            .Bucket
            .Should().Be(_fixture.Settings.Bucket);
    }
}