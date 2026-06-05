# Day-17 — Week-1 API deployed to Azure Container Apps (end-to-end live)

The Week-1 ASP.NET API now runs on **Azure Container Apps** with **SQLite** storage,
and the Angular SWA calls it cross-origin. All three required endpoints are verified
working end-to-end. **No new paid resource was created** — the only billable-capable
resource (the container registry) already existed and was reused.

## Live endpoints

| Component | URL |
|---|---|
| Frontend (SWA) | https://witty-hill-0f9d14b00.7.azurestaticapps.net |
| **API (Container App)** | https://ca-quotes-day17.purplecoast-dcd0caac.southeastasia.azurecontainerapps.io |

## Every Azure resource (created vs reused)

| Resource | Type | RG / Region | Status | Cost |
|---|---|---|---|---|
| `ca-quotes-day17` | Container App | `thinkschool-rg` / Southeast Asia | **CREATED** | $0 — consumption, **min-replicas 0** (scale-to-zero); within the free monthly grant |
| `stapp-quotes-day17` | Static Web App (Free) | `rg-avishkar` / East Asia | created earlier (Day-17) | $0 — Free tier |
| `thinkschool-env` | Container Apps Environment | `thinkschool-rg` / Southeast Asia | **REUSED** (student quota = 1/region) | $0 — no Log Analytics attached |
| `acroug3e3rejocbe` | Container Registry | `thinkschool-rg` / Southeast Asia | **REUSED** (already existed) | no *new* cost — pre-existing |
| `Microsoft.Web`, `Microsoft.App` | Resource providers | subscription | **REGISTERED** | $0 |

**SQLite** = a file at `/tmp/quotes.db` inside the container. Free, but **ephemeral**:
`DbSeeder` re-seeds on each cold start, so demo data always regenerates; quotes created
at runtime reset when the container recycles (the documented trade-off of free, no Azure SQL).

## Commands run

```bash
# Providers (one-time)
az provider register --namespace Microsoft.Web      # (Day-17 SWA)
az provider register --namespace Microsoft.App      # Container Apps

# Build the image with the .NET SDK (NO Docker daemon) and push to the existing ACR.
# ACR Tasks are blocked on this student subscription, and there is no local Docker —
# so the SDK's built-in container build is the path. Auth = an ACR access token
# exchanged from `az acr login --expose-token` (no admin user, no stored secret).
dotnet publish -c Release -p:PublishProfile=DefaultContainer \
  -p:ContainerRegistry=acroug3e3rejocbe.azurecr.io \
  -p:ContainerRepository=quotesapi -p:ContainerImageTag=day17

# Container App — image from ACR via system-assigned MANAGED IDENTITY (no registry
# secret), external ingress on 8080, SQLite, scale-to-zero.
az containerapp create \
  --name ca-quotes-day17 --resource-group thinkschool-rg \
  --environment thinkschool-env \
  --image acroug3e3rejocbe.azurecr.io/quotesapi:day17 \
  --target-port 8080 --ingress external \
  --registry-server acroug3e3rejocbe.azurecr.io --registry-identity system \
  --min-replicas 0 --max-replicas 1 \
  --env-vars "Database__Provider=Sqlite" \
             "ConnectionStrings__Default=Data Source=/tmp/quotes.db" \
             "ASPNETCORE_ENVIRONMENT=Production"
```

Note: the Container App pulls its image from ACR using a **system-assigned managed
identity** (`--registry-identity system` → AcrPull) — so even the registry pull has
**no stored secret**.

## Code changes that made it deployable

| File | Change |
|---|---|
| `QuotesApi/Program.cs` | `db.Database.EnsureCreated()` for **both** providers (no EF migrations exist); added **CORS** (`AddCors` + `UseCors`) allowing the SWA origin |
| `QuotesApi/Dockerfile` + `.dockerignore` | multi-stage .NET 10 build; listens on `:8080` |
| `src/app/interceptors/api-base.interceptor.ts` | **NEW** — rewrites relative `/api/...` → the absolute Container App URL in production |
| `src/app/app.config.ts` | provides `API_BASE_URL` = the Container App URL; registers `apiBaseInterceptor` after `authInterceptor` |
| `src/app/interceptors/timeout.interceptor.ts` | 5 s → **30 s** to tolerate scale-to-zero cold start |
| `tsconfig.json` | excludes `api/**` + `QuotesApi/**` from the Angular build |
| `staticwebapp.config.json` | **CSP `connect-src`** extended to allow the Container App origin — without it the browser blocked every cross-origin API fetch (`Refused to connect … connect-src 'self'`) |

## Verification log (real)

```
GET  /api/quotes?page=1&size=10   → 200, seeded Quote[] (newest first)
GET  /api/quotes/1                → 200, {"id":1,"author":"Marcus Aurelius",…,"ownerId":1}
POST /api/auth/login              → 200, {"accessToken":"eyJ…","refreshToken":"…"}

CORS (Origin: <SWA>):
  OPTIONS /api/quotes  → 204, access-control-allow-origin: <SWA>, allow-methods GET,POST,…
  GET     /api/quotes  → 200, access-control-allow-origin: <SWA>
  POST    /api/auth/login → 200, accessToken returned
```

## Known trade-offs (free-tier honest notes)

- **Cold start:** first request after idle (scale-to-zero) takes a few seconds while
  .NET boots + `EnsureCreated` + seed run; the SWA's request timeout was raised to 30 s
  to tolerate it. A warm app answers in well under 500 ms.
- **Ephemeral data:** SQLite lives in the container; runtime-created quotes reset on
  recycle. Persisting them would need Azure SQL (+cost) — intentionally avoided.
- **Auth is the user-JWT model** (`POST /api/auth/login` → bearer token), not the
  Day-17 Managed-Identity-broker design (that remains blocked by SWA Free tier; see
  `DEPLOYMENT.md`). The Container App→ACR pull *does* use managed identity (no secret).
