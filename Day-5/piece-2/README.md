# Day 5 · Piece 2 — Container image from `dotnet publish` (no Dockerfile)

Up to this point QuotesApi was always run with `dotnet run`. To ship it anywhere that isn't my laptop I need an OCI image — and the path most .NET tutorials still show is "write a multi-stage Dockerfile, copy bin/Release, set ENTRYPOINT." .NET 10 makes that path optional. The SDK can build and publish a container image **directly from `dotnet publish`**, with no Dockerfile in the repository, no `FROM mcr.microsoft.com/...` lines to keep in sync with the runtime version, and no hand-rolled non-root user. This piece wires that in for QuotesApi.

---

## How SDK containers work (one paragraph)

`dotnet publish -t:PublishContainer` runs the normal publish step (restore → compile → publish/Release for the chosen RID), then hands the published output to the **`Microsoft.NET.Build.Containers`** SDK target. That target reads a base image from MCR, layers your published `/app` directory on top, sets `ENTRYPOINT ["dotnet", "QuotesApi.dll"]`, picks the matching ASP.NET Core port, configures a non-root `app` user, and pushes the resulting image straight to your local Docker daemon (or to a registry if you give it `ContainerRegistry`). The whole image build is one MSBuild invocation — no `docker build`, no Dockerfile to lint, no stage to forget.

---

## The exercise — three csproj properties

Added to [QuotesApi.csproj](QuotesApi.csproj#L9-L13):

```xml
<PropertyGroup>
  <ContainerRepository>quotes-api</ContainerRepository>
  <ContainerImageTag>0.1.0</ContainerImageTag>
  <ContainerBaseImage>mcr.microsoft.com/dotnet/aspnet:10.0</ContainerBaseImage>
</PropertyGroup>
```

That's the whole "Dockerfile" — three lines of MSBuild metadata.

| Property | What it does |
| --- | --- |
| `ContainerRepository` | The repository part of the image tag (`quotes-api` in `quotes-api:0.1.0`). The piece instructions show `<ContainerImageName>`, which still works but the SDK now warns it's obsolete in favour of `ContainerRepository`. |
| `ContainerImageTag` | The version tag. Single string here; `ContainerImageTags` (plural) accepts a semicolon-separated list if you want to push `0.1.0` and `latest` from the same build. |
| `ContainerBaseImage` | The base image MCR pulls from. The default for a Web SDK project is already `mcr.microsoft.com/dotnet/aspnet:<runtime>`, so this line is technically optional — but setting it explicitly means the version is pinned in the csproj rather than implicit in whichever SDK happens to be on the machine. |

There are more knobs (`ContainerImageTags`, `ContainerRegistry`, `ContainerLabel`, `ContainerWorkingDirectory`, `ContainerUser`, `ContainerPort`); the three above are enough for this exercise.

---

## A `/health` endpoint to prove it

Added in [Program.cs](Program.cs#L54-L55):

```csharp
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .AllowAnonymous();
```

A real app would use `Microsoft.Extensions.Diagnostics.HealthChecks` with probes for the DB and downstream services, but for "did the container come up?" a single-route 200 is enough. `.AllowAnonymous()` is technically a no-op here (there's no fallback `[Authorize]` policy on the app) — I left it in to be explicit, so a future reader adding `RequireAuthenticatedUser()` as the fallback doesn't accidentally lock out the healthcheck.

---

## Build

```pwsh
dotnet publish QuotesApi.csproj --os linux --arch x64 -t:PublishContainer -c Release
```

Two notes on the flags:

- `--os linux --arch x64` selects the runtime identifier (RID). The SDK then asks MCR for the matching variant of `ContainerBaseImage`. On a Windows host without these, `dotnet publish` would default to `win-x64` and pick a Windows base image — not what you want.
- `-t:PublishContainer` is the MSBuild target. The piece instructions show `/t:PublishContainer`, which works in PowerShell but bash interprets the leading `/` as a path. `-t:` is portable.

Tail of build output:

```
QuotesApi -> .../bin/Release/net10.0/linux-x64/publish/
Building image 'quotes-api' with tags '0.1.0' on top of base image 'mcr.microsoft.com/dotnet/aspnet:10.0'.
Pushed image 'quotes-api:0.1.0' to local registry via 'docker'.
```

The "local registry via 'docker'" line is the SDK shelling out to `docker` to load the image; if Docker Desktop weren't running, the SDK would still build the image as a tarball and tell you where to find it.

---

## Run

```pwsh
docker run -d --name quotes-api -p 8080:8080 `
  -e KeyVault__Uri= `
  -e ConnectionStrings__Default="Data Source=/tmp/quotes.db" `
  quotes-api:0.1.0
```

Two env overrides were needed to make this image actually run, both of which are honest gaps in the *app* rather than the container plumbing:

- **`KeyVault__Uri=`** — `appsettings.json` points at a real Azure Key Vault. Inside a local container, `DefaultAzureCredential` has nothing to grab onto (no Azure CLI, no IMDS) and the chain takes ~30s to time out. The empty override short-circuits the `if (!string.IsNullOrWhiteSpace(keyVaultUri))` block in `Program.cs`.
- **`ConnectionStrings__Default=Data Source=/tmp/quotes.db`** — the SDK image runs as non-root user `app` with `/app` as the working directory and read-only. The default `Data Source=quotes.db` resolves to `/app/quotes.db` and fails with `SQLite Error 14: 'unable to open database file'`. `/tmp` is world-writable in the base image.

Both are reminders that "works on my laptop" config silently relied on `dotnet run` happening as my user, in a writable repo directory, with `az login` already done.

---

## Hit /health

```
$ curl -sS -i http://localhost:8080/health
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Server: Kestrel

{"status":"ok"}
```

And the matching log line from inside the container (Serilog from piece 4 still wired up, OTel from piece 5 too):

```
[INF] HTTP GET /health responded 200 in 32.85 ms
```

---

## Image size — Debian vs Alpine

The piece instructions suggest `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` for a smaller image. I tried it; the container exits 139 on startup with:

```
Error relocating /app/libe_sqlite3.so: fcntl64: symbol not found
```

The diagnosis: the SQLite native library that ships with `Microsoft.EntityFrameworkCore.Sqlite` is compiled against **glibc**. Alpine uses **musl libc**. The two are ABI-incompatible — `fcntl64` is a glibc symbol that doesn't exist in musl. To stay on Alpine I would need to publish for `linux-musl-x64` *and* make sure the SQLitePCLRaw package shipped a musl-built `e_sqlite3.so`. Without that, "alpine for smaller images" is a one-way trip to a startup crash.

I stayed on the Debian base. Final image is **366MB on disk / 103MB compressed**, which is what the Debian-based aspnet:10.0 image costs me.

---

## What I didn't do (and why those would be the next steps)

- **No `HEALTHCHECK` instruction in the image.** The SDK doesn't add one. Kubernetes/Docker can't tell a hung process from a healthy one without external probes. For Kubernetes I'd configure `livenessProbe` / `readinessProbe` in the pod spec instead.
- **No persistent volume.** SQLite at `/tmp/quotes.db` is ephemeral — every container restart loses every quote and refresh token. SQLite is the wrong storage for a containerized API the moment you scale past one replica; a real deploy would point at PostgreSQL or SQL Server.
- **No DataProtection key persistence.** Startup logs warn: `Storing keys in a directory '/home/app/.aspnet/DataProtection-Keys' that may not be persisted outside of the container.` On restart every issued anti-forgery / cookie token becomes invalid. The fix is `PersistKeysToAzureBlobStorage` + `ProtectKeysWithAzureKeyVault`, or a mounted volume.
- **No registry push.** I built to the local Docker daemon. To ship to ACR I'd add `<ContainerRegistry>acravishkar.azurecr.io</ContainerRegistry>` and `az acr login --name acravishkar` before publish.
- **No CI step.** The same `dotnet publish -t:PublishContainer` line goes into the GitHub Actions workflow, gated on `main`.

---

## Exercise reflection

### Q1 — What did you learn this session?

The idea that clicked is **how much of "writing a Dockerfile" is just boilerplate that the SDK already knows for ASP.NET Core apps**. With three MSBuild properties and one `dotnet publish` invocation I went from source code to a tagged image in my local Docker daemon. There's no `FROM`, no `COPY bin/Release/...`, no `WORKDIR`, no `ENTRYPOINT`, no `RUN adduser` to create a non-root user — the SDK picks the right base image variant for the RID, copies the published output to `/app`, sets the entrypoint, chooses port 8080, and runs as `app` instead of root. That last part — secure defaults out of the box — is the bit I'll keep. A hand-written Dockerfile is opinionated only insofar as the author thought to be opinionated. The SDK is opinionated *by default*, in the direction of "non-root, minimum surface, runtime matches build."

The other half of the lesson was that **"no Dockerfile" doesn't mean "no container thinking."** I still had to reason about which base image works with my native dependencies (Alpine/musl vs Debian/glibc for SQLite), where the process can write files (non-root `app` user can't write to `/app`, hence `/tmp` for the SQLite file), and which config is environment-specific (KeyVault URI baked into `appsettings.json` is a leak waiting to happen — even with the SDK as the layer above, that string ships in layer 1 of my image). The SDK removes the boilerplate. It doesn't remove the engineering.

### Q2 — What would break this?

The biggest failure mode is exactly what bit me: **a base image whose libc doesn't match the native libraries my dependencies ship.** SQLite was the giveaway — `aspnet:10.0-alpine` looks like an obvious win for a smaller image, but `e_sqlite3.so` is built against glibc, the container crashes at startup with a cryptic `fcntl64: symbol not found`, and exit code 139 is the only hint that this is a native-loader problem and not application code. The same trap exists for any NuGet package with native code (`System.Drawing.Common`, `Grpc.Core`, anything bundling libicu or skia). The fix is either `--os linux-musl --arch x64` plus a musl-compatible package variant, or just staying on the Debian base — but you don't know until the container exits 139 in CI.

Other things I didn't handle that would break a real deploy:

- **Secrets in the image.** `appsettings.json` (containing the Key Vault URI and the dev JWT signing key) is baked into the published `/app` directory and ships inside layer 1 of the image. The JWT key is labelled "dev-only" but it's right there in plain text — anyone with `docker pull` access has it. The fix is to keep `appsettings.json` for non-secret structure and source all real secrets from Key Vault, environment, or a mounted secret file at runtime.
- **Stateful SQLite in an ephemeral container.** I wrote `/tmp/quotes.db`, which means every restart loses everything. The image doesn't even ship the migration history table — the first request after restart triggers `db.Database.Migrate()` to recreate the schema from scratch.
- **Port hardcoding.** `-p 8080:8080` assumes the SDK keeps its default (`ASPNETCORE_HTTP_PORTS=8080`). If a future SDK changes the default, or if I set `ASPNETCORE_URLS` to something else, the `-p` mapping silently does nothing useful.
- **No `HEALTHCHECK` instruction.** The HTTP `/health` endpoint exists, but Docker has no idea the container has one. `docker ps` shows `Up` indefinitely even if the process has hung — only an external load balancer or k8s probe would catch it.

---

## Links

- **Repository:** [https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil)
- **Folder:** [Day-5/piece-2](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil/tree/main/Day-5/piece-2)
