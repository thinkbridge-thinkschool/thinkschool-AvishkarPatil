# Day 5 · Piece 2 — Container image from `dotnet publish` (no Dockerfile)

.NET 10 ships built-in container image generation. No Dockerfile, no `FROM mcr.microsoft.com/dotnet/aspnet`, no multi-stage build for the common case.

---

## csproj container properties

[QuotesApi.csproj](QuotesApi.csproj#L9-L13):

```xml
<PropertyGroup>
  <ContainerRepository>quotes-api</ContainerRepository>
  <ContainerImageTag>0.1.0</ContainerImageTag>
  <ContainerBaseImage>mcr.microsoft.com/dotnet/aspnet:10.0</ContainerBaseImage>
</PropertyGroup>
```

> The instructions show `<ContainerImageName>` and `aspnet:10.0-alpine`. The SDK now warns that `ContainerImageName` is obsolete (use `ContainerRepository`), and the alpine base crashes at startup because `Microsoft.EntityFrameworkCore.Sqlite`'s native `e_sqlite3.so` is built for glibc, not musl (`Error relocating /app/libe_sqlite3.so: fcntl64: symbol not found`). Staying on the Debian base — it works without further changes.

Health endpoint in [Program.cs](Program.cs#L54-L55):

```csharp
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .AllowAnonymous();
```

---

## Build the image

```pwsh
dotnet publish QuotesApi.csproj --os linux --arch x64 -t:PublishContainer -c Release
```

Tail of build output:

```
QuotesApi -> .../bin/Release/net10.0/linux-x64/publish/
Building image 'quotes-api' with tags '0.1.0' on top of base image 'mcr.microsoft.com/dotnet/aspnet:10.0'.
Pushed image 'quotes-api:0.1.0' to local registry via 'docker'.
```

```
$ docker images quotes-api
IMAGE              ID             DISK USAGE   CONTENT SIZE
quotes-api:0.1.0   5a22e101e79d        366MB          103MB
```

---

## `docker run` output

```pwsh
docker run -d --name quotes-api -p 8080:8080 `
  -e KeyVault__Uri= `
  -e ConnectionStrings__Default="Data Source=/tmp/quotes.db" `
  quotes-api:0.1.0
```

Output:

```
$ docker run -d --name quotes-api -p 8080:8080 ...
cef938414d5bc3ca37a9382ac10704f43d5fb02bddbbda6d1f77d3298458a2ec

$ docker ps --filter name=quotes-api --format "table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}"
NAMES        IMAGE              STATUS          PORTS
quotes-api   quotes-api:0.1.0   Up 5 seconds    0.0.0.0:8080->8080/tcp, [::]:8080->8080/tcp
```

Startup logs (head):

```
[INF] Now listening on: http://[::]:8080
[INF] Application started. Press Ctrl+C to shut down.
[INF] Hosting environment: Production
[INF] Content root path: /app
```

Why the two `-e` overrides were needed:

- `KeyVault__Uri=` — `appsettings.json` points at a real Azure Key Vault; `DefaultAzureCredential` from inside a container has nothing to authenticate with and takes ~30s to time out the whole chain. Empty value short-circuits the `if (!string.IsNullOrWhiteSpace(keyVaultUri))` block in `Program.cs`.
- `ConnectionStrings__Default="Data Source=/tmp/quotes.db"` — the SDK runs the container as non-root user `app` with `/app` read-only, so the default `Data Source=quotes.db` fails with `SQLite Error 14: 'unable to open database file'`. `/tmp` is world-writable.

---

## `curl` to the health endpoint

```
$ curl -sS -i http://localhost:8080/health
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Date: Mon, 25 May 2026 13:05:14 GMT
Server: Kestrel
Transfer-Encoding: chunked

{"status":"ok"}
```

Matching log line from inside the container:

```
[INF] HTTP GET /health responded 200 in 32.85 ms
```

---

## GitHub link

- **Repository:** [https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil)
- **Folder:** [Day-5/piece-2](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil/tree/main/Day-5/piece-2)

---

## Q1 — What did you learn this session?

The thing that clicked is **how much of "writing a Dockerfile" is just boilerplate that the SDK already knows for ASP.NET Core apps**. With three MSBuild properties (`ContainerRepository`, `ContainerImageTag`, `ContainerBaseImage`) and one `dotnet publish` invocation, I went from source code to a tagged image in my local Docker daemon. There's no `FROM`, no `COPY bin/Release/...`, no `WORKDIR`, no `ENTRYPOINT`, no `RUN adduser` to create a non-root user — the SDK picks the right base image variant for my runtime ID, copies the published output to `/app`, sets the entrypoint, exposes port 8080, and runs as a non-root user named `app`. That last part is the idea I'll keep: the SDK is opinionated about secure defaults in a way a hand-written Dockerfile usually isn't, because the author of a Dockerfile is opinionated only insofar as they thought to be.

The other half of the lesson was that **"no Dockerfile" doesn't mean "no container thinking."** I still had to reason about which base image works with my native dependencies (Alpine/musl vs Debian/glibc for SQLite), where the process can write files (non-root `app` user can't write to `/app`, hence routing the SQLite file to `/tmp`), and which config is environment-specific (the Key Vault URI baked into `appsettings.json` is a leak waiting to happen — the SDK as the layer above doesn't change the fact that the string is in layer 1 of the image). The SDK removes the boilerplate. It does not remove the engineering.

## Q2 — What would break this?

The biggest failure mode is exactly what bit me: **a base image whose libc doesn't match the native libraries my dependencies ship.** SQLite was the giveaway — `aspnet:10.0-alpine` looks like an obvious win for a smaller image, but `e_sqlite3.so` is built against glibc, the container exits 139 at startup with the cryptic `Error relocating /app/libe_sqlite3.so: fcntl64: symbol not found`, and exit code 139 is the only hint that this is a native-loader problem and not application code. The same trap exists for any NuGet package with native code (`System.Drawing.Common`, `Grpc.Core`, anything bundling libicu or skia). The fix is either `--os linux-musl --arch x64` plus a musl-compatible package variant, or just staying on the Debian base — but you don't know until the container exits 139 in CI.

Other things I didn't handle:

- **Stateful SQLite in an ephemeral container.** I wrote `/tmp/quotes.db`, which means every restart loses every quote, user, and refresh token. The image doesn't even ship the migration-history table — the first request after every restart triggers `db.Database.Migrate()` to recreate the schema from empty. SQLite is the wrong choice for a containerized API the moment you scale past one replica.
- **DataProtection keys are ephemeral.** Startup logs warned that keys go to `/home/app/.aspnet/DataProtection-Keys` inside the container; on restart every issued cookie/anti-forgery token becomes invalid. Fix needs persistent storage or Key Vault for the keyring.
- **Secrets baked into the image.** `appsettings.json` (containing the Key Vault URI and the dev JWT signing key) is in the published `/app` directory and ships inside layer 1 of the image. The JWT key is labelled "dev-only" but it's right there in plain text — anyone with `docker pull` access has it.
- **No `HEALTHCHECK` instruction.** `/health` works at the HTTP layer, but the image has no `HEALTHCHECK` line, so Docker can't tell a hung process from a healthy one — `docker ps` shows `Up` indefinitely.
- **Port hardcoding.** `-p 8080:8080` assumes the SDK keeps its default (`ASPNETCORE_HTTP_PORTS=8080`). If a future SDK changes the default, or if I set `ASPNETCORE_URLS` to something else, the `-p` mapping silently does nothing useful.
