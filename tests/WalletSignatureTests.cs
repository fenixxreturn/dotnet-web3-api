using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Nethereum.Signer;
using Xunit;

namespace DotnetWeb3Api.Tests;

public class WalletSignatureTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public WalletSignatureTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task WalletAuth_ValidSignature_RecoversAddressAndIssuesToken()
    {
        // sign a message with a known test key using Nethereum, exactly what a wallet's
        // personal_sign / SIWE flow produces, then check /auth/wallet recovers the same address.
        // well-known Hardhat/Anvil default test account #0 private key, never used on mainnet
        var key = new EthECKey("0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80");
        var address = key.GetPublicAddress();
        var message = $"Sign in to dotnet-web3-api: {Guid.NewGuid()}";
        var signature = new EthereumMessageSigner().EncodeUTF8AndSign(message, key);

        var resp = await _client.PostAsJsonAsync("/auth/wallet", new { Address = address, Message = message, Signature = signature });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<TokenResponse>(AuthTests.JsonOpts);
        Assert.False(string.IsNullOrWhiteSpace(body?.Token));
    }

    [Fact]
    public async Task WalletAuth_SignatureFromDifferentKey_ReturnsUnauthorized()
    {
        var signingKey = EthECKey.GenerateKey();
        var claimedAddress = EthECKey.GenerateKey().GetPublicAddress(); // not the signer's address
        var message = $"Sign in to dotnet-web3-api: {Guid.NewGuid()}";
        var signature = new EthereumMessageSigner().EncodeUTF8AndSign(message, signingKey);

        var resp = await _client.PostAsJsonAsync("/auth/wallet", new { Address = claimedAddress, Message = message, Signature = signature });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    private record TokenResponse(string Token);
}
