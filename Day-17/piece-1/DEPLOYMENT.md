# Day-17 Piece-1 — Deployment

## What is live now

| Item | Value |
|---|---|
| **Live URL** | https://witty-hill-0f9d14b00.7.azurestaticapps.net |
| SWA resource | `stapp-quotes-day17` |
| Resource group | `rg-avishkar` |
| SWA location | East Asia (nearest SWA-supported region to your `southeastasia` quota) |
| SKU | Free |
| Subscription | Azure for Students (`bbcfff0f-…`) |

## Azure CLI commands actually run

```bash
# 1. One-time: register the resource provider (subscription wasn't registered)
az provider register --namespace Microsoft.Web        # waited until state = Registered

# 2. Create the Static Web App (Free tier)
az staticwebapp create \
  --name stapp-quotes-day17 \
  --resource-group rg-avishkar \
  --location eastasia \
  --sku Free

# 3. Fetch the deployment token (NOT committed — used locally / as a GH secret)
az staticwebapp secrets list \
  --name stapp-quotes-day17 --resource-group rg-avishkar \
  --query properties.apiKey -o tsv

# 4. Build + deploy the static artifact
npm run build      # → dist/quotes-signals-app/browser  (+ staticwebapp.config.json copied in)
npx @azure/static-web-apps-cli deploy dist/quotes-signals-app/browser \
  --deployment-token <token> --env production
```

## CI/CD

`.github/workflows/azure-static-web-apps-day17.yml` redeploys on every push to `Day-17/piece-1/**`.
It needs one repo secret — the SWA deployment token — added once:

```bash
az staticwebapp secrets list -n stapp-quotes-day17 -g rg-avishkar --query properties.apiKey -o tsv
# GitHub → Settings → Secrets and variables → Actions → New repository secret
#   Name:  AZURE_STATIC_WEB_APPS_API_TOKEN_DAY17
#   Value: <token>
```

That deployment token only authorizes uploading static content to this one SWA. It is **not** an Azure login, **not** a client secret for the Week-1 API.

---

## Managed-Identity architecture (the correct design — and why it is not fully live)

**A browser SPA cannot use Managed Identity.** MI tokens come from the Azure IMDS endpoint (`169.254.169.254`), reachable only from inside Azure compute. Browser JS cannot get one. The correct architecture is a **server-side broker**:

```
Browser (SPA)  ──GET /api/quotes──▶  SWA linked backend (Azure Functions)
                                      │  has system-assigned Managed Identity
                                      │  DefaultAzureCredential.getToken(API_SCOPE)
                                      ▼
                              Week-1 QuotesApi (Azure, Entra-protected)
                              validates the MI token, returns Quote[]
```

The broker is implemented in [`api/src/functions/quotes.ts`](api/src/functions/quotes.ts): it forwards `/api/quotes*` to the Week-1 API with a managed-identity bearer token, **zero secrets**.

### Why it is not fully live (honest blockers)

1. **SWA Free tier has no managed identity** (`az staticwebapp show … --query identity` → `null`). System MI + linked backends require **Standard** tier (billable) — and you authorized Free only.
2. **The Week-1 API is localhost** (`http://localhost:5075`). MI is Azure→Entra-protected-resource; it cannot target localhost, and an Azure Function cannot reach your laptop. There is no Azure-hosted, Entra-protected Week-1 API for the MI token to be valid against.

### Exact upgrade path to make MI real

```bash
# (a) Deploy the Week-1 QuotesApi to Azure + protect it with Entra ID
#     (Container App or App Service + Easy Auth / JwtBearer on an Entra app).
#     Expose an app role/scope, e.g.  api://<week1-app-id>/.default

# (b) Move the SWA to Standard and turn on system-assigned MI
az staticwebapp update -n stapp-quotes-day17 -g rg-avishkar --sku Standard
az staticwebapp identity assign -n stapp-quotes-day17 -g rg-avishkar

# (c) Link the Functions broker as the backend
az staticwebapp backends link -n stapp-quotes-day17 -g rg-avishkar \
  --backend-resource-id <functions-app-resource-id> --backend-region <region>

# (d) Grant the SWA's MI the app role on the Week-1 API's Entra app
#     (az ad app permission / app-role assignment), then set broker config:
#     WEEK1_API_BASE_URL = https://<week1-host>
#     WEEK1_API_SCOPE    = api://<week1-app-id>/.default
```

Only after (a)–(d) does an end-to-end MI call carry a real managed-identity token to the real API. Everything before that point is correct code with no valid upstream — stated plainly rather than faked.

---

## Custom domain

Not done — no domain with DNS access was available. Steps for when one is:

```bash
az staticwebapp hostname set -n stapp-quotes-day17 -g rg-avishkar \
  --hostname quotes.<yourdomain> --validation-method cname-delegation
# then add the CNAME (quotes → witty-hill-0f9d14b00.7.azurestaticapps.net) at your DNS provider
```

## Lighthouse

Run against the live URL (`Screenshots/lighthouse.report.html` / `.json`):

```bash
npx lighthouse https://witty-hill-0f9d14b00.7.azurestaticapps.net/ \
  --only-categories=performance,accessibility,best-practices,seo \
  --output=json --output=html --output-path=./Screenshots/lighthouse
```

Result: **Performance 96 · Accessibility 100 · Best-Practices 100 · SEO 100** — all ≥ 95.
