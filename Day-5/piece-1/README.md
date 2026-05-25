# Day 4 · Piece 7 — Configuration done right

Up to piece 6 the JWT key, issuer, audience, and lifetime were all read by `configuration["Jwt:Key"]` etc., sprinkled across `TokenService` and `InfrastructureExtensions`. That works, but every read is a stringly-typed dictionary lookup with no compile-time guarantee anything is actually configured. This piece introduces the **typed `IOptions<T>` pattern** for `Jwt` (and `EntraId` while I was in there), validates the binding at startup, and reads it everywhere a string key used to be.

---

## Where config comes from (precedence, low → high)

1. **`appsettings.json`** — defaults, committed to the repo. No secrets.
2. **`appsettings.{Environment}.json`** — `appsettings.Development.json`, `appsettings.Testing.json`. Same shape, env-specific overrides.
3. **User secrets** (`dotnet user-secrets set Jwt:Key ...`) — local dev only, stored in `%APPDATA%\Microsoft\UserSecrets\`. Never on disk inside the repo.
4. **Environment variables** — `Jwt__Key=...` (the `__` becomes `:`). What container runtimes and CI inject.
5. **Azure Key Vault** — added in [Program.cs](Program.cs#L11-L18) via the `Azure.Extensions.AspNetCore.Configuration.Secrets` provider. Wired in piece 6 for `AppInsights:ConnectionString`; same provider can hand off `Jwt--Key` in prod.

Each layer **overrides** the previous one — env vars beat appsettings, Key Vault beats env vars (since it's added last in `Program.cs`). All five land in the same `IConfiguration` tree, so the calling code never has to know which layer answered.

> **Secrets never go in `appsettings.json`.** The current `Jwt:Key` in `appsettings.json` is the literal string `dev-only-key-replace-via-env-or-secrets-in-production-32b` — long enough to satisfy `MinLength(32)`, useless to an attacker, and overridden by user secrets locally / Key Vault in prod.

---

## The exercise — `JwtOptions` end-to-end

### 1. Typed class — [Configuration/JwtOptions.cs](Configuration/JwtOptions.cs)

```csharp
using System.ComponentModel.DataAnnotations;

namespace QuotesApi.Configuration;

public sealed record JwtOptions
{
    public const string SectionName = "Jwt";

    [Required, MinLength(32)]
    public string Key { get; init; } = default!;

    [Required]
    public string Issuer { get; init; } = default!;

    [Required]
    public string Audience { get; init; } = default!;

    [Range(1, 1440)]
    public int AccessTokenExpiresInMinutes { get; init; } = 15;

    [Range(1, 365)]
    public int RefreshTokenExpiresInDays { get; init; } = 7;

    public TimeSpan AccessTokenLifetime  => TimeSpan.FromMinutes(AccessTokenExpiresInMinutes);
    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(RefreshTokenExpiresInDays);
}
```

A `record` with `init` setters — the values come from config at startup and never change for the lifetime of the instance. `SectionName` is a `const` so the DI registration and any future code can refer to `JwtOptions.SectionName` instead of the magic string `"Jwt"`. `AccessTokenLifetime` / `RefreshTokenLifetime` are computed properties so callers like `TokenService` can ask for a `TimeSpan` directly instead of doing the `.AddMinutes(int)` dance themselves.

The `[Required] / [MinLength] / [Range]` attributes are picked up by `.ValidateDataAnnotations()` below — that's what turns a missing or too-short key into a **startup failure** instead of a 500 on the first login.

### 2. `appsettings.json` section — [appsettings.json](appsettings.json#L14-L20)

```json
"Jwt": {
  "Key": "dev-only-key-replace-via-env-or-secrets-in-production-32b",
  "Issuer": "QuotesApi",
  "Audience": "QuotesApiClients",
  "AccessTokenExpiresInMinutes": 15,
  "RefreshTokenExpiresInDays": 7
}
```

Property names match the `JwtOptions` property names *exactly* — the binder is case-insensitive but pedantically matching avoids any "is it `expiresInMinutes` or `ExpiresInMinutes`?" doubt when overriding from env vars (`Jwt__AccessTokenExpiresInMinutes=30`).

### 3. DI registration — [Extensions/InfrastructureExtensions.cs](Extensions/InfrastructureExtensions.cs#L37-L41)

```csharp
services
    .AddOptions<JwtOptions>()
    .Bind(configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

Four lines:

| Call | What it does |
| --- | --- |
| `AddOptions<JwtOptions>()` | Registers `IOptions<JwtOptions>`, `IOptionsSnapshot<JwtOptions>`, and `IOptionsMonitor<JwtOptions>` in DI. |
| `.Bind(...)` | Tells the binder to populate the record from the `"Jwt"` section of `IConfiguration`. |
| `.ValidateDataAnnotations()` | Runs the `[Required] / [MinLength] / [Range]` checks when the options are first resolved. |
| `.ValidateOnStart()` | Forces that first resolution to happen during host startup, not on the first request — so a misconfigured key crashes the app immediately, not at 3am when the first user logs in. |

The reason this is a clear win over the old `configuration["Jwt:Key"]` reads: with the four-line wire-up above, the day someone deploys with a missing `Jwt:Key`, the deployment *never enters service*. With the old code, the deploy succeeds, the app starts, healthchecks pass, and the first user to hit `/auth/login` gets a 500 and we find out from a customer.

The piece also adds an `EntraIdOptions` record [Configuration/EntraIdOptions.cs](Configuration/EntraIdOptions.cs) with the same treatment — same pattern, smaller surface.

### 4. Consuming it in a service — [Services/TokenService.cs](Services/TokenService.cs)

```csharp
public sealed class TokenService(IOptionsSnapshot<JwtOptions> jwtOptions, IClock clock) : ITokenService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public string CreateAccessToken(User user)
    {
        var signingKey  = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        // ... build claims ...

        var token = new JwtSecurityToken(
            issuer:             _jwt.Issuer,
            audience:           _jwt.Audience,
            claims:             claimList,
            expires:            clock.UtcNow.Add(_jwt.AccessTokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    // ...
}
```

`TokenService` no longer takes `IConfiguration` — it asks for exactly what it needs. The hand-rolled "is the key configured / is it long enough" defensive checks are gone too, because `ValidateDataAnnotations` already covered them at startup. If `_jwt` exists, it's valid.

### Why `IOptionsSnapshot<T>` here?

`TokenService` is scoped (one per request). `IOptionsSnapshot<T>` is **scoped too**, so its `.Value` is re-bound on the first access *per request*. If someone live-edits `appsettings.json` in a running app, the next request picks up the new value without a restart. For a singleton service that wants change notifications, you'd use `IOptionsMonitor<T>` instead — but `TokenService` doesn't need callbacks, just a fresh-per-request read.

The straight `IOptions<T>` is a **singleton** that captures the config at first resolution and never refreshes. That's the right choice for things you genuinely want frozen at startup; not for things you might want to hot-rotate (a JWT signing key, for example, where a forced restart on rotation is operationally annoying).

### Bonus — the JWT *validator* now reads from the same record

The piece's secondary win is that the `AddJwtBearer(...)` configuration also goes through `JwtOptions`, not a second pass of `configuration["Jwt:*"]` reads:

```csharp
services
    .AddOptions<JwtBearerOptions>(InternalScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
    {
        var j = jwt.Value;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer      = j.Issuer,
            ValidAudience    = j.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(j.Key)),
            // ...
        };
    });
```

Before piece 7, the signer (`TokenService`) and the validator (`AddJwtBearer`) both read `Jwt:Key` independently — drift was possible if a refactor touched only one. Now they read from the same `IOptions<JwtOptions>` instance — **one source of truth**.

---

## What I checked still works

- `dotnet build QuotesApi.csproj` — 0 errors.
- `dotnet test Quotes.Tests.Unit` — **64 passed**, including 3 new tests in [JwtOptionsValidationTests.cs](Quotes.Tests.Unit/JwtOptionsValidationTests.cs) that prove `OptionsValidationException` fires on missing / too-short keys, plus the rewritten [TokenServiceTests.cs](Quotes.Tests.Unit/TokenServiceTests.cs) that now constructs the SUT with an NSubstitute `IOptionsSnapshot<JwtOptions>`.
- `dotnet test Quotes.Tests.Integration` — **33 passed**. The Testcontainers SQL Server still spins up, auth still works, the policy scheme still routes Internal vs. Entra tokens correctly. Nothing in the request path changed observably; the refactor was internal.

---

## Exercise reflection

### Q1 — What did you learn this session?

The thing that clicked is that **`IOptions<T>` isn't about reducing keystrokes — it's about moving the "is this configured correctly?" question from request time to startup time**. The old `configuration["Jwt:Key"] ?? throw ...` pattern is fine code; it just answers the question too late. `.ValidateDataAnnotations().ValidateOnStart()` answers it before the app even starts accepting traffic, which means a misconfigured deploy fails the rollout instead of failing the first user. That alignment between "config is wrong" and "deployment can't proceed" is the whole point. The other idea I'll keep is the **scope distinction between the three `IOptions*` flavors**. `IOptions<T>` is a singleton — frozen at first read; great when you genuinely want startup-time config. `IOptionsSnapshot<T>` is scoped — re-resolves per request; great when you want hot-reload of `appsettings.json` to actually do something. `IOptionsMonitor<T>` is a singleton with callbacks — great for long-lived background services that want to react when config changes. They're not interchangeable, and picking the right one tells the next reader something about how that piece of config is expected to behave at runtime. Tied to that is the realization that **`JwtOptions.AccessTokenLifetime`** (a computed `TimeSpan` property) is more honest than handing callers an `int` minutes value: the type now expresses the unit. `TimeSpan` doesn't have an "is this in minutes or seconds?" ambiguity.

### Q2 — What would break this?

The first failure mode is **the binder silently filling in default values for properties without `[Required]`**. If I add a new `JwtOptions.Issuer` requirement tomorrow but forget the `[Required]` attribute, and the config section happens to be missing `Issuer`, the binder produces `Issuer = null!` (because the `default!` initializer suppresses the nullable warning) and the app boots happily — then tokens get issued with `iss: null`, which JWT validation will silently accept on some libraries and reject on others. The fix is discipline: every property that must be present needs `[Required]`. There's no compiler enforcement, just code review. The second is **`ValidateOnStart` and Key Vault `DefaultAzureCredential`'s slow-fail chain**. If Key Vault is reachable but my credential is wrong, the provider takes ~30s to walk its chain and time out — and only *then* does `ValidateOnStart` get to look at what landed in `IConfiguration`. If the eventual value is empty, the app crashes at startup with a `OptionsValidationException` *for the JWT key*, even though the *real* root cause was the auth-to-Key-Vault failure. The error message points at the wrong layer. The mitigation is to give the Key Vault load its own diagnostic logging in `Program.cs` so the silent timeout becomes visible. The third is **`IOptionsSnapshot<T>` with config that legitimately should be frozen**. `TokenService` consumes the *signing key* via snapshot, so if someone edits `appsettings.json` in a running container, the next request signs with the new key — but every token already issued was signed with the *old* key, and now they fail validation because `AddJwtBearer` also reads the snapshot and only sees the new key. There's no overlap window, no key-ID-aware validation. For something as load-bearing as a signing key, the right fix is to maintain a list of *previous* keys for some grace period, or to roll keys via a process (drain → restart) rather than a hot-edit — i.e., the snapshot's "easy to change" property is actually a footgun here. For *expiry minutes* the snapshot is fine; for *the signing key* a frozen `IOptions<T>` would be safer, with rotation explicitly modelled rather than implicit.

---

## Links

- **Repository:** [https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil)
- **Folder:** [Day-4/piece-7](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil/tree/main/Day-4/piece-7)
