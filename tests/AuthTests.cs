using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DotnetWeb3Api.Tests;

public class AuthTests : IClassFixture<WebApplicationFactory<Program>>
{
    // minimal API responses are camelCase; ReadFromJsonAsync defaults to case-sensitive matching.
    internal static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public AuthTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_Then_Login_ReturnsToken()
    {
        var username = $"user-{Guid.NewGuid():N}";

        var registerResp = await _client.PostAsJsonAsync("/auth/register", new { Username = username, Password = "correct-horse-battery" });
        Assert.Equal(HttpStatusCode.Created, registerResp.StatusCode);

        var loginResp = await _client.PostAsJsonAsync("/auth/login", new { Username = username, Password = "correct-horse-battery" });
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);

        var body = await loginResp.Content.ReadFromJsonAsync<TokenResponse>(JsonOpts);
        Assert.False(string.IsNullOrWhiteSpace(body?.Token));
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var username = $"user-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/auth/register", new { Username = username, Password = "correct-horse-battery" });

        var loginResp = await _client.PostAsJsonAsync("/auth/login", new { Username = username, Password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, loginResp.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        var resp = await _client.GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsSubject()
    {
        var username = $"user-{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/auth/register", new { Username = username, Password = "correct-horse-battery" });
        var loginResp = await _client.PostAsJsonAsync("/auth/login", new { Username = username, Password = "correct-horse-battery" });
        var token = (await loginResp.Content.ReadFromJsonAsync<TokenResponse>(JsonOpts))!.Token;

        var request = new HttpRequestMessage(HttpMethod.Get, "/me");
        request.Headers.Add("Authorization", $"Bearer {token}");
        var resp = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<MeResponse>(JsonOpts);
        Assert.Equal(username, body?.Subject);
    }

    private record TokenResponse(string Token);
    private record MeResponse(string? Subject, string? Wallet);
}
