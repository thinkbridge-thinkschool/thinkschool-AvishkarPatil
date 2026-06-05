# Day-17 — True Managed-Identity auth (end-to-end, live)

The browser→API JWT flow is **replaced** by a Managed-Identity path. The SPA calls a
**broker Container App** that holds a **system-assigned managed identity**; the broker
acquires an MI access token for the Week-1 API's Entra app and forwards the call. The
API **requires and validates** that token. **No client secret, key, or connection
string anywhere** — not in the repo, the images, or app settings.

```
Browser (SWA) ──/api/*──▶ ca-quotes-broker (system MI)
   no token ever            DefaultAzureCredential.getToken("api://<api-app>/.default")
                            Authorization: Bearer <MI token>
                                   │
                                   ▼
                          ca-quotes-day17 (Week-1 API)
                          EntraId JwtBearer validates signature + iss + aud + role
                          → 200 with token · 401 without
```

## Live endpoints

| Component | URL |
|---|---|
| Frontend (SWA) | https://witty-hill-0f9d14b00.7.azurestaticapps.net |
| Broker (MI) | https://ca-quotes-broker.purplecoast-dcd0caac.southeastasia.azurecontainerapps.io |
| Week-1 API | https://ca-quotes-day17.purplecoast-dcd0caac.southeastasia.azurecontainerapps.io |

## Azure resources created for the MI path (all free)

| Resource | Type | Purpose |
|---|---|---|
| `quotes-api-day17` | Entra app registration (`appId abb9a212-0298-4302-985a-f5be1676d00d`, URI `api://abb9a212…`) | Identity of the Week-1 API; exposes the `Quotes.Access` app role (Application type) |
| `quotes-api-day17` SP | Service principal (`oid 75df18f7…`) | Holds the app role for assignment |
| `ca-quotes-broker` | Container App + **system-assigned MI** (`oid bb7b9b7b…`, `client/azp c1df08ea…`) | The MI token-acquirer/forwarder. Consumption, scale-to-zero → $0 |
| app-role assignment | Graph `appRoleAssignedTo` | Grants the broker MI the `Quotes.Access` role on the API app |

`requestedAccessTokenVersion = 2` on the app so the issued token is v2 (issuer contains `microsoftonline.com`, matching the API's scheme forwarder).

## Evidence

**1. The MI token the broker sends** (decoded payload from the broker's `/whoami`):

```json
{
  "aud":   "abb9a212-0298-4302-985a-f5be1676d00d",
  "iss":   "https://login.microsoftonline.com/7e394fc8-4b86-4cfe-810e-43f86f8bec47/v2.0",
  "azp":   "c1df08ea-5314-4d06-9cf1-e2b51e9d0410",
  "roles": ["Quotes.Access"],
  "oid":   "bb7b9b7b-502e-4e4f-9657-4d93e7320510"
}
```
- `aud` = the Week-1 API's app id · `roles` = the assigned app role · `oid`/`azp` = the broker's managed identity. A real MI token, obtained with **no secret**.

**2. The API validates it** — direct call without a token is rejected:
```
GET  https://ca-quotes-day17…/api/quotes?page=1&size=3        (no Authorization)  → 401
```

**3. Through the broker (MI token attached) it succeeds:**
```
GET  /api/quotes?page=1&size=3   → 200  Quote[] (seeded)
GET  /api/quotes/1               → 200  {"id":1,"author":"Marcus Aurelius",…}
POST /api/auth/login             → 200  {"accessToken":"…"}   (anonymous on the API)
```

**4. Browser-equivalent (CORS from the SWA origin → broker):**
```
OPTIONS /api/quotes  → 204  access-control-allow-origin: https://witty-hill-0f9d14b00…
GET     /api/quotes  → 200  access-control-allow-origin: https://witty-hill-0f9d14b00…
```

**5. Live SWA wired to the broker:** the deployed `main-*.js` references
`ca-quotes-broker…`, and the served CSP is
`connect-src 'self' https://ca-quotes-broker…`.

## No secret anywhere (verified)

- **Broker:** `DefaultAzureCredential` → platform-injected system MI. Env vars are non-secret only: `ApiBaseUrl`, `ApiScope` (`api://<app-id>/.default`), `Cors__AllowedOrigins`.
- **ACR image pull:** both Container Apps pull via `--registry-identity system` (AcrPull) — no registry username/password.
- **Repo:** no `client_secret` / key / connection string committed (SQLite `Data Source=/tmp/quotes.db` is the only connection string and is not a credential).
- The browser holds **no token at all** — it only talks to the broker, same-origin-style over CORS.

## States exercised

| State | How | Result |
|---|---|---|
| success (MI) | broker `/api/quotes`, `/api/quotes/{id}` | 200 + data |
| **401 / failed token** | direct API call with no MI token | 401 (API rejects) |
| login | broker `POST /api/auth/login` | 200 (anonymous endpoint, still reachable) |
| error / cold start | broker scale-to-zero first hit | retried; warm responses < 1 s |
| empty | a page past the last row | `[]` |

## Day-17 deliverable status

| Requirement | Status |
|---|---|
| Live on Azure Static Web Apps | ✅ |
| Lighthouse ≥ 95 | ✅ 96 / 100 / 100 / 100 |
| **Call the API via Managed Identity (no client secret)** | ✅ broker MI token, `roles:[Quotes.Access]`, API validates; 401 without it |
| No secret in repo / app settings | ✅ verified |
| Custom domain | ❌ intentionally out of scope (no domain/DNS) |
