using JobPilot.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace JobPilot.Api.Tests;

public sealed class AuthTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Theory]
    [InlineData(null, true)]
    [InlineData("http://127.0.0.1:5080", true)]
    [InlineData("http://localhost:5000;https://localhost:5001", true)]
    [InlineData("http://[::1]:5080", true)]
    [InlineData("http://0.0.0.0:10000", false)]
    [InlineData("http://*:8080", false)]
    [InlineData("http://+:8080", false)]
    [InlineData("http://127.0.0.1:5080;http://0.0.0.0:5081", false)]
    public void DetectsLoopbackOnlyUrls(string? urls, bool expected) =>
        Assert.Equal(expected, AuthSettings.IsLoopbackOnly(urls));

    [Fact]
    public void LocalhostWithoutConfigurationRunsWithoutSignIn()
    {
        var settings = AuthSettings.Resolve(Config([]), "http://127.0.0.1:5080");

        Assert.False(settings.Enabled);
    }

    [Fact]
    public void PublicAddressWithoutConfigurationRefusesToStart()
    {
        Assert.Throws<InvalidOperationException>(() => AuthSettings.Resolve(Config([]), "http://0.0.0.0:10000"));
    }

    [Fact]
    public void ShortSigningKeyIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => AuthSettings.Resolve(Config(new()
        {
            ["Auth:GoogleClientId"] = "client.apps.googleusercontent.com",
            ["Auth:AllowedEmails"] = "me@example.com",
            ["Auth:SigningKey"] = "too-short"
        }), "http://0.0.0.0:10000"));
    }

    [Fact]
    public async Task SessionTokenValidatesOnlyWithTheSameKey()
    {
        var settings = AuthSettings.Resolve(Config(new()
        {
            ["Auth:GoogleClientId"] = "client.apps.googleusercontent.com",
            ["Auth:AllowedEmails"] = "Me@Example.com, other@example.com",
            ["Auth:SigningKey"] = new string('k', 48)
        }), "http://0.0.0.0:10000");
        var otherSettings = AuthSettings.Resolve(Config(new()
        {
            ["Auth:GoogleClientId"] = "client.apps.googleusercontent.com",
            ["Auth:AllowedEmails"] = "me@example.com",
            ["Auth:SigningKey"] = new string('x', 48)
        }), "http://0.0.0.0:10000");

        Assert.True(settings.Enabled);
        Assert.Contains("me@example.com", settings.AllowedEmails);

        var session = new AuthService(settings).CreateSession("me@example.com");
        var handler = new JsonWebTokenHandler();

        var valid = await handler.ValidateTokenAsync(session.Token, new AuthService(settings).ValidationParameters());
        var forged = await handler.ValidateTokenAsync(session.Token, new AuthService(otherSettings).ValidationParameters());

        Assert.True(valid.IsValid);
        Assert.Equal("me@example.com", valid.Claims["email"]);
        Assert.False(forged.IsValid);
    }
}
