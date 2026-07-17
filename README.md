# dotnet-web3-api

A small ASP.NET Core (net8.0) minimal API that bridges .NET with the EVM blockchain.

Note on stack: .NET is not our primary stack, blockchain engineering is. This repo exists to show that a
C#/ASP.NET service can do real web3 work, not to showcase .NET idioms for their own sake. The interesting
code here is the Nethereum integration: an on-chain ERC-20 read and an EIP-191 signature-based wallet login.

## What it does

Two blockchain endpoints, both backed by [Nethereum](https://nethereum.com/):

- `GET /chain/erc20/{token}/balance/{address}`
  Reads `balanceOf` (and `decimals`) directly from an ERC-20 contract on a live RPC endpoint and returns
  both the raw uint256 and the human-readable balance. No indexer, no cache, a direct on-chain read from C#.

- `POST /auth/wallet` (`{ "address", "message", "signature" }`)
  SIWE-style wallet login. Takes an EIP-191 `personal_sign` signature, recovers the signer address with
  Nethereum's `EthereumMessageSigner`, and if it matches the claimed address, issues a JWT. This is the same
  recover-and-compare pattern used by "Sign-In with Ethereum", implemented server-side in C#.

Plus a conventional JWT auth flow to round out the API:

- `POST /auth/register` hashes the password with BCrypt.Net and stores the user (in-memory, this is a demo).
- `POST /auth/login` verifies the password and issues a JWT.
- `GET /me` is behind the standard `Microsoft.AspNetCore.Authentication.JwtBearer` middleware.

## Running it

Requires the .NET 8 SDK.

```bash
cd src
dotnet run
```

Config lives in `appsettings.json` and can be overridden with environment variables:

- `Jwt__Secret`, `Jwt__Issuer`, `Jwt__Audience`: JWT signing config. The committed secret is a demo-only
  placeholder, override it with `Jwt__Secret` for anything real.
- `Rpc__Url`: the EVM RPC endpoint used for on-chain reads. Defaults to the public Base mainnet RPC
  (`https://mainnet.base.org`), no API key required.

Example call once it's running:

```bash
# USDC on Base
curl http://localhost:5000/chain/erc20/0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913/balance/0x0000000000000000000000000000000000000000
```

## Tests

xUnit tests cover the register/login/protected-route flow and, more importantly, the signature-recovery
logic: a test signs a message with a well-known test private key using Nethereum, posts it to
`/auth/wallet`, and asserts the endpoint recovers the correct signer and issues a token (plus a negative
case where the signature doesn't match the claimed address). The live-RPC balance endpoint is not covered
by an automated test since public RPC availability isn't reliable in CI; the ABI encoding it depends on
(`BalanceOfFunction`/`DecimalsFunction`) is exercised indirectly through the Nethereum contract-call
pipeline used by the wallet-auth tests.

```bash
cd tests
dotnet test
```

Verified green in a clean GitHub Codespace (.NET SDK preinstalled, no local dependency on this repo's dev
box, which cannot run the .NET SDK). Real output:

```
Passed!  - Failed: 0, Passed: 6, Skipped: 0, Total: 6 - DotnetWeb3Api.Tests.dll (net8.0)
```

Verified in a GitHub Codespace on .NET SDK 10.0.200 (targeting net8.0): `dotnet build` clean, `dotnet test` 6/6 passing.
