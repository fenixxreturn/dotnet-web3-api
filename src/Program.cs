using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Numerics;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Signer;
using Nethereum.Web3;

var builder = WebApplication.CreateBuilder(args);

var jwtIssuer = builder.Configuration["Jwt:Issuer"]!;
var jwtAudience = builder.Configuration["Jwt:Audience"]!;
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured (set Jwt__Secret env var or appsettings).");
var rpcUrl = builder.Configuration["Rpc:Url"]!;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep the raw "sub" claim instead of remapping it to ClaimTypes.NameIdentifier,
        // so /me can read JwtRegisteredClaimNames.Sub directly.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// ponytail: in-memory user store, this is a portfolio demo not a production db.
// key = lowercased username or lowercased wallet address.
var users = new ConcurrentDictionary<string, UserRecord>();

string IssueToken(string subject, IEnumerable<Claim>? extraClaims = null)
{
    var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, subject) };
    if (extraClaims is not null) claims.AddRange(extraClaims);

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(4),
        signingCredentials: creds);
    return new JwtSecurityTokenHandler().WriteToken(token);
}

app.MapPost("/auth/register", (RegisterRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { error = "username and password are required" });

    var key = req.Username.Trim().ToLowerInvariant();
    var record = new UserRecord(req.Username, BCrypt.Net.BCrypt.HashPassword(req.Password), WalletAddress: null);
    if (!users.TryAdd(key, record))
        return Results.Conflict(new { error = "username already exists" });

    return Results.Created($"/auth/{req.Username}", new { username = req.Username });
});

app.MapPost("/auth/login", (LoginRequest req) =>
{
    var key = req.Username?.Trim().ToLowerInvariant() ?? "";
    if (!users.TryGetValue(key, out var record) || record.PasswordHash is null ||
        !BCrypt.Net.BCrypt.Verify(req.Password, record.PasswordHash))
        return Results.Unauthorized();

    return Results.Ok(new { token = IssueToken(record.Username) });
});

app.MapGet("/me", (ClaimsPrincipal user) =>
{
    var subject = user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.Identity?.Name;
    var wallet = user.FindFirstValue("wallet");
    return Results.Ok(new { subject, wallet });
}).RequireAuthorization();

// SIWE-style wallet auth: recover the EIP-191 personal_sign signer and compare to the claimed address.
app.MapPost("/auth/wallet", (WalletAuthRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Address) || string.IsNullOrWhiteSpace(req.Message) || string.IsNullOrWhiteSpace(req.Signature))
        return Results.BadRequest(new { error = "address, message and signature are required" });

    string recovered;
    try
    {
        recovered = new EthereumMessageSigner().EncodeUTF8AndEcRecover(req.Message, req.Signature);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"could not recover signer: {ex.Message}" });
    }

    if (!string.Equals(recovered, req.Address, StringComparison.OrdinalIgnoreCase))
        return Results.Unauthorized();

    var key = req.Address.Trim().ToLowerInvariant();
    users.AddOrUpdate(key,
        _ => new UserRecord(req.Address, PasswordHash: null, WalletAddress: req.Address),
        (_, existing) => existing with { WalletAddress = req.Address });

    var claims = new[] { new Claim("wallet", req.Address) };
    return Results.Ok(new { token = IssueToken(req.Address, claims) });
});

// On-chain read: ERC-20 balanceOf via Nethereum against a configurable RPC.
app.MapGet("/chain/erc20/{token}/balance/{address}", async (string token, string address) =>
{
    try
    {
        var web3 = new Web3(rpcUrl);
        var balanceHandler = web3.Eth.GetContractQueryHandler<BalanceOfFunction>();
        var rawBalance = await balanceHandler.QueryAsync<BigInteger>(token, new BalanceOfFunction { Owner = address });

        var decimalsHandler = web3.Eth.GetContractQueryHandler<DecimalsFunction>();
        var decimals = await decimalsHandler.QueryAsync<byte>(token, new DecimalsFunction());

        var human = (decimal)rawBalance / (decimal)BigInteger.Pow(10, decimals);

        return Results.Ok(new
        {
            token,
            address,
            rawBalance = rawBalance.ToString(),
            decimals,
            balance = human,
            rpc = rpcUrl,
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"RPC call failed: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

record RegisterRequest(string Username, string Password);
record LoginRequest(string Username, string Password);
record WalletAuthRequest(string Address, string Message, string Signature);
record UserRecord(string Username, string? PasswordHash, string? WalletAddress);

[Function("balanceOf", "uint256")]
public class BalanceOfFunction : FunctionMessage
{
    [Parameter("address", "owner", 1)]
    public string Owner { get; set; } = "";
}

[Function("decimals", "uint8")]
public class DecimalsFunction : FunctionMessage
{
}

// exposed for WebApplicationFactory<Program> in the test project
public partial class Program { }
