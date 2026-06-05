# Day 17 · Piece 1 — Deploy to Azure Static Web Apps

**Live URL:** https://witty-hill-0f9d14b00.7.azurestaticapps.net
**Lighthouse:** Performance 96 · Accessibility 100 · Best-Practices 100 · SEO 100 (all ≥ 95)
**Auth model:** Managed-Identity *architecture* implemented as a server-side broker — **no client secret anywhere**. See the honest blockers below for why the MI hop is not yet end-to-end live.

---

## (1) Brief to the agent

> Deploy the existing Day-16/piece-2 Angular 21 SPA to **Azure Static Web Apps** on my Azure-for-Students subscription (`rg-avishkar`, nearest SWA region to `southeastasia`). Target a live `*.azurestaticapps.net` URL and Lighthouse ≥ 95.
>
> **Real Week-1 API it must hit** (same-origin `/api`):
> - `GET /api/quotes?page={page}&size={size}` → `Quote[]`
> - `GET /api/quotes/{id}` → `Quote` (200) or 404
> - `POST /api/quotes` → 201 + `Quote`
> - `Quote = { id, author, text, createdAt, isDeleted, ownerId }`
>
> **Auth requirement:** the call to the Week-1 API must use **Managed Identity — no client secret, key, or connection string in the repo or app settings.** Reject any client-secret fallback.
>
> If a browser SPA cannot do MI directly, implement the correct Azure architecture instead of faking it; if any part is impossible with current resources, say so explicitly rather than inventing a URL/score/token.

## (2) Agent output

**Files created / modified**

| File | Purpose |
|---|---|
| `staticwebapp.config.json` | SPA fallback routing, security headers + CSP, `/api/*` reserved for the linked backend |
| `.github/workflows/azure-static-web-apps-day17.yml` | CI/CD deploy; only credential is a GH-secret deployment token (never committed) |
| `api/src/functions/quotes.ts` | **The Managed-Identity broker** — `DefaultAzureCredential` → MI token → forwards `/api/quotes*` to the Week-1 API. Zero secrets. |
| `api/package.json`, `api/host.json`, `api/tsconfig.json` | Azure Functions broker project |
| `DEPLOYMENT.md` | Every `az` command run, the MI upgrade path, custom-domain steps |
| `src/index.html` | `+meta description` (SEO) |
| `src/robots.txt` + `angular.json` assets | `robots.txt` (SEO) |
| `angular.json` | `optimization.styles.inlineCritical: false` — removes the inline `onload` handler that violated CSP (Best-Practices) |
| `src/app/app.component.ts` | `<main>` landmark (Accessibility) |
| 6 component/style files | darkened `#6c757d → #565d64` + nav link `#0d6efd → #0a58ca` (color-contrast) |

**Azure resources created**

| Resource | Detail |
|---|---|
| `Microsoft.Web` provider | registered on the subscription (was unregistered) |
| `stapp-quotes-day17` | Static Web App, **Free**, East Asia, in `rg-avishkar` |

**The MI broker (the part that actually does Managed Identity)** — `api/src/functions/quotes.ts`:

```typescript
const credential = new DefaultAzureCredential();   // resolves to the backend's system-assigned MI

async function forward(req, context) {
  const token  = await credential.getToken(API_SCOPE);          // MI token — no secret
  const url    = new URL(req.url);
  const res    = await fetch(`${API_BASE}${url.pathname}${url.search}`, {
    method: req.method,
    headers: { Authorization: `Bearer ${token.token}`, 'Content-Type': 'application/json' },
    body: req.method === 'GET' ? undefined : await req.text(),
  });
  return { status: res.status, body: await res.text() };
}
```

The browser keeps calling `/api/quotes` exactly as before; the broker (running as the SWA's linked backend) attaches the managed-identity token on the server side. The browser never holds a token, and there is no secret in the bundle, the workflow, or app settings.

## (3) Verification log

### Live URL + Lighthouse (real)
- Live: **https://witty-hill-0f9d14b00.7.azurestaticapps.net** → `curl` returns `HTTP/1.1 200 OK` with the CSP + `X-Content-Type-Options` + `X-Frame-Options` + `Referrer-Policy` headers applied.
- Lighthouse v13.3.0 against the live URL → **Perf 96 / A11y 100 / Best-Practices 100 / SEO 100**. Reports: [`Screenshots/lighthouse.report.html`](Screenshots/lighthouse.report.html) / `.json`.

### States exercised
- **loaded / loading**: the shell loads instantly; the list issues `GET /api/quotes?page=1&size=10`.
- **error (real, in prod)**: with no linked backend, `/api/quotes` returns the static shell (no JSON) → the SPA's `listView()` enters its **error** state. This is the honest production behavior until the broker + API are live — not a mock.
- **empty / 401**: not reachable in prod without a backend; exercised locally in Day-16 against the live API.

### ONE wrong assumption I caught and made the agent fix
The agent's first `staticwebapp.config.json` declared a `/api/*` route whose only content was a `comment` (no action). The SWA CLI **rejected the whole config as schema-invalid**, so the security headers + SPA fallback silently never applied (first deploy shipped with no CSP). I caught it in the CLI output (`✖ Please fix … configuration`), removed the comment-only route, and let `navigationFallback` handle SPA routing — after redeploy `curl -D-` confirmed the CSP and security headers are now served. (Knock-on: that CSP then exposed Angular's inline-`onload` critical-CSS handler, which I fixed via `inlineCritical: false` — the Best-Practices 92 → 100 jump.)

### Managed-Identity evidence — and the honest blockers
- **No secret committed**: a tree-wide scan for `client_secret` / `apiKey` / `AccountKey=` / `ConnectionString` / the SWA deployment token returns nothing real (only code comments and a fake `test-token-123` in a unit test). The deployment token lives in `/tmp`, never in-repo; CI reads it from a GitHub secret.
- **MI not yet end-to-end live — two real blockers, stated not faked:**
  1. `az staticwebapp show … --query identity` → **`null`**. Managed identity + linked backends require **SWA Standard** (billable); you authorized **Free** only.
  2. The Week-1 API is **`http://localhost:5075`** — not in Azure. MI is Azure→Entra-protected-resource; it cannot mint a token for `localhost`, and an Azure Function cannot reach your laptop. There is no Entra-protected upstream for the MI token to be valid against.
- The broker is **correct, secret-free code with no valid upstream yet.** `DEPLOYMENT.md` has the exact upgrade path: deploy the Week-1 API to Azure + Entra-protect it, move the SWA to Standard, `az staticwebapp identity assign`, link the backend, grant the MI the app role.

### What breaks if the API's auth or a key endpoint changes
- **API moves off localhost into Azure (the intended fix):** set `WEEK1_API_BASE_URL` + `WEEK1_API_SCOPE` on the broker; the SPA is unchanged (still calls `/api/quotes`).
- **API switches from Entra/MI to API-key auth:** the whole MI design is invalidated — and a key would have to be stored *somewhere*, which the brief forbids. Correct response is to keep MI, not fall back to a key.
- **`/api/quotes` path or `Quote.id` field changes:** the broker forwards paths verbatim, so a path rename needs no broker change, but the SPA's `httpResource` URLs (`/api/quotes/${id}`) and `track quote.id` assume the current shape — covered in Day-16's contract notes.

### Custom domain
**Not done** — no domain with DNS access. `DEPLOYMENT.md` documents the `az staticwebapp hostname set` + CNAME steps for when one exists.

---

## Deliverable status (honest)

| Deliverable | Status |
|---|---|
| Live URL | ✅ https://witty-hill-0f9d14b00.7.azurestaticapps.net |
| Lighthouse ≥ 95 | ✅ 96 / 100 / 100 / 100 |
| Managed-Identity auth | ⚠️ **architecture implemented (broker, no secret)**; not end-to-end live — blocked by SWA Free tier + the Week-1 API being localhost. Upgrade path documented. |
| Custom domain | ❌ no domain/DNS available; steps documented |
| No secret in repo/app settings | ✅ verified by scan |

See [DEPLOYMENT.md](DEPLOYMENT.md) for every command, resource, and the exact MI go-live steps.
