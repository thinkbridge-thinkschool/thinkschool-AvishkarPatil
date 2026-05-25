# Day 5 · Piece 4 — Deploy via `azd`

[Piece 3](../piece-3/README.md) stood up the Azure surface — a resource group, a Container Apps environment, the resource providers and CLI extensions, the static IP, the DNS suffix. No application code was running on it yet. This piece deploys [piece 2](../piece-2/docker-output.md)'s container image into that environment using the **Azure Developer CLI** (`azd`), which collapses "build the image, push to ACR, declare the infra, apply the infra, swap the image into the running app" into one command (`azd up`). The deliverable is a working public URL serving `GET /health` over HTTPS.

The concise exercise submission lives in [output.md](output.md). This README is the longer write-up: what `azd` actually is, why the file layout is what it is, what went wrong on the first try (twice), and what's still missing.

---

## What `azd` actually is (one paragraph each)

**A thin orchestrator over three things you already know.** At the bottom is **Bicep** (or Terraform) that describes Azure resources declaratively. In the middle is an **image build** — for a .NET project with no Dockerfile, that means `dotnet publish -t:PublishContainer` pushing straight to ACR; for other languages it's a Buildpack or a Dockerfile build. At the top is **`az containerapp update`** swapping the image in the running app. `azd` itself contributes none of that — it just ties them together with a single config file and a single command.

**`azure.yaml` is a routing table, not an app definition.** It says "service `api` lives in `./QuotesApi.csproj`, is written in `dotnet`, deploys to `containerapp`." It does *not* say what the container app's name is, what ingress it has, what env vars it gets — that all lives in the Bicep. The link between `azure.yaml` and Bicep is a single tag: the Container App resource in Bicep is tagged `azd-service-name: api`, and at deploy time `azd` finds the app with that tag and updates its image. Without that tag the deploy step would have no idea which app to point at.

**`azd up` = `azd provision` + `azd deploy`.** `provision` runs the Bicep against your subscription (creates RG, ACR, MI, Container App). `deploy` packages the source, pushes the image, updates the Container App revision. Running them separately is sometimes useful — you can `provision` once and then iterate on the app with `deploy` in seconds. `up` is the new-comer's command.

**State lives in two places.** Per-machine state (your azd login, the active environment name) lives in `~/.azure/`. Per-repo per-environment state (subscription ID, location, output values like the registry endpoint and the live URL) lives in `.azure/<env-name>/.env` inside the repo. The `.azure/` folder has its own `.gitignore` that excludes everything — `azd` is deliberate about not letting you commit subscription-specific state into a shared repo.

---

## Prerequisites this piece had to clear

The piece instructions show three friendly commands: install azd, `azd init`, `azd up`. Real first-time setup on this machine needed five extras:

```pwsh
# 1. azd binary itself. winget is the official Windows install path.
winget install --id Microsoft.Azd --accept-source-agreements --accept-package-agreements

# 2. Bicep CLI. azd uses Bicep but doesn't bundle it; az CLI installs it.
az bicep install

# 3. Tell azd to reuse the existing az CLI login (instead of opening its own
#    browser-based MSAL flow). This is per-user-config, not per-repo.
azd config set auth.useAzCliAuth true

# 4. Create the azd environment with subscription + region pinned non-interactively.
azd env new piece4 `
  --location southeastasia `
  --subscription bbcfff0f-9093-4e77-97cb-dc0b230a1707

# 5. Verify the chain. Should print the same user as `az account show`.
azd auth login --check-status
```

`auth.useAzCliAuth true` is the one that matters and isn't in the piece instructions. By default `azd` keeps its own MSAL token cache completely separate from `az`'s, so even with a working `az login` your first `azd up` will say "Not logged in, run `azd auth login`". The first instinct (`az ad sp create-for-rbac --role Contributor`) is the *wrong* fix — it mints a long-lived credential you don't need because you already have a valid identity. The one-line config flip makes `azd` walk the `az` token cache, same identity, no new credential, no cleanup.

`azd init` is normally what scaffolds `azure.yaml` + `infra/main.bicep` + `infra/main.parameters.json`. In auto mode without interactive prompts it errors with `prompt required` on the "Confirm and continue" survey, so this piece's scaffold was written by hand to match what `azd init --from-code` would have produced. Functionally identical; just typed out instead of generated.

---

## The four files

### `azure.yaml` — the routing table

```yaml
name: quotes-api-piece-4
metadata:
  template: quotes-api-piece-4@1.0.0
services:
  api:
    project: ./QuotesApi.csproj
    host: containerapp
    language: dotnet
```

Five lines that matter:

- `project: ./QuotesApi.csproj` — `azd` invokes `dotnet publish` against this csproj. The `<ContainerRepository>quotes-api</ContainerRepository>` + `<ContainerImageTag>0.1.0</ContainerImageTag>` properties from piece 2 are still there; `azd` doesn't replace them, it just adds `/p:ContainerRegistry=<acr-endpoint>` at publish time.
- `host: containerapp` — tells `azd` to look for a `Microsoft.App/containerApps` resource tagged `azd-service-name: api` when it's time to deploy. If this said `appservice` instead it would look for `Microsoft.Web/sites`.
- `language: dotnet` — tells `azd` which packaging strategy to use. For `dotnet` without a Dockerfile, it's `dotnet publish -t:PublishContainer` straight to ACR (no local Docker daemon required).

What's *not* in `azure.yaml`: the Container App's name, ingress, env vars, scale rules, identity, registry, env-id. All of that is in Bicep. `azure.yaml` is intentionally tiny.

### `infra/main.bicep` — the subscription-scoped wrapper

`targetScope = 'subscription'` because the standard azd template wants to create the resource group as part of provisioning. This deployment doesn't — it references an existing RG with `existing` instead — but keeping subscription scope leaves room to grow into the standard pattern later.

```bicep
resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' existing = {
  name: resourceGroupName  // 'thinkschool-rg' from piece 3
}

module resources 'resources.bicep' = {
  scope: resourceGroup
  name: 'resources'
  params: { ... }
}
```

The outputs `AZURE_CONTAINER_REGISTRY_ENDPOINT`, `SERVICE_API_NAME`, `SERVICE_API_URI` get written back into `.azure/piece4/.env` after provisioning, which is how `azd deploy` knows which registry to push to and which app to update.

### `infra/resources.bicep` — the actual resources

This is where the work happens, all scoped to `thinkschool-rg`:

1. **Existing Container Apps env reference**: `resource containerAppsEnv 'Microsoft.App/managedEnvironments@2024-03-01' existing = { name: 'thinkschool-env' }`. No new env created.
2. **New ACR** (`acr${resourceToken}`, Basic SKU, admin disabled, anonymous pull disabled).
3. **New user-assigned managed identity** (`mi-api-${resourceToken}`) — the Container App will run as this identity for the purposes of pulling images from ACR.
4. **AcrPull role assignment** binding the MI to the ACR. The Container App's `registries` block references the MI's resource ID; without the role assignment ARM would still let you deploy, but the app would fail to pull at runtime with a 401 from ACR.
5. **New Container App** (`ca-api-${resourceToken}`), tagged `azd-service-name: api`, with:
   - `environmentId: containerAppsEnv.id` — pointing at the existing env from piece 3.
   - `ingress.external: true`, `targetPort: 8080`, `transport: 'auto'` — the .NET app listens on 8080 in Production (set by `ASPNETCORE_HTTP_PORTS` in the published container) and Container Apps' front door terminates TLS and forwards to it.
   - `image: placeholderImage` on the first `provision` (a hello-world from MCR), swapped to the real ACR image by `azd deploy` later.
   - `env`: `ASPNETCORE_ENVIRONMENT=Production`, `ConnectionStrings__Default=Data Source=/tmp/quotes.db`, `KeyVault__Uri=""`, `AppInsights__ConnectionString=""`, `AZURE_CLIENT_ID=${mi.clientId}`. The first three are the same workaround as piece 2's `docker run`; the last lets the MSAL token chain find the right identity if/when Key Vault is wired back in.
   - **Liveness + readiness probes both on `/health`** — Container Apps uses readiness to gate traffic to a new revision (so the revision swap waits until `/health` returns 200), and liveness to restart a wedged replica.
   - `scale: { minReplicas: 0, maxReplicas: 3 }` — scales to zero when idle. Cold-start hits the first request after ~10 minutes of inactivity.

### `infra/main.parameters.json` — env-var-driven parameters

```json
{
  "parameters": {
    "environmentName":              { "value": "${AZURE_ENV_NAME}" },
    "location":                     { "value": "${AZURE_LOCATION}" },
    "resourceGroupName":            { "value": "${AZURE_RESOURCE_GROUP=thinkschool-rg}" },
    "containerAppsEnvironmentName": { "value": "${AZURE_CONTAINER_APPS_ENVIRONMENT_NAME=thinkschool-env}" },
    "apiExists":                    { "value": "${SERVICE_API_RESOURCE_EXISTS=false}" },
    "apiDefinition":                { "value": { "image": "${SERVICE_API_IMAGE_NAME}" } }
  }
}
```

The `${VAR=default}` syntax is azd's, not ARM's — it substitutes `VAR` from `.azure/piece4/.env` and falls back to `default` if missing. The first time `azd provision` runs, `SERVICE_API_RESOURCE_EXISTS` is unset → false → Bicep deploys with the placeholder image. After the first `azd deploy`, azd writes `SERVICE_API_RESOURCE_EXISTS=true` and `SERVICE_API_IMAGE_NAME=acr.../quotes-api:0.1.0` to the env file, so subsequent `azd provision` runs deploy with the real image directly.

---

## Container Apps env — first attempt (failed)

The straightforward Bicep, before learning about the quota: `resource containerAppsEnv 'Microsoft.App/managedEnvironments@2024-03-01' = { name: 'cae-${resourceToken}', location: 'southeastasia', ... }`. A fresh env per `azd env`, as the standard `azd init` scaffold does it.

`azd provision` failed at preflight validation, before any resource was created:

```
ERROR: The Container Apps deployment template is invalid.

Validation Error Details:
InvalidTemplateDeployment: ... reported preflight validation errors. ...
MaxNumberOfRegionalEnvironmentsInSubExceeded:
  The subscription 'bbcfff0f-...' cannot have more than 1 Container App
  Environments in Southeast Asia.
```

This is the **per-region environment quota** at work. Azure-for-Students caps managed environments at one per region. Piece 3 already used the southeastasia slot with `thinkschool-env`. The five-region allow-list from [piece 3's README](../piece-3/README.md#prerequisites-this-piece-had-to-clear) is `austriaeast, eastasia, uaenorth, malaysiawest, southeastasia` — and **`southeastasia` is the only one of those five that supports `Microsoft.App/managedEnvironments`**, so there's no "just pick a different region" fallback. The slot is permanently in use until piece 3's env is torn down.

This was caught at preflight, not midway through a deploy, which is the redeeming feature — no half-created ACR or container app to clean up.

---

## Container Apps env — second attempt (works)

The fix is one line of Bicep: use `existing` instead of declaring a new resource.

```bicep
// Before (allocates new env, hits quota):
resource containerAppsEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${resourceToken}'
  // ... full properties block
}

// After (references piece 3's env, no new resource):
resource containerAppsEnv 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: containerAppsEnvironmentName  // 'thinkschool-env'
}
```

`existing` tells Bicep: don't try to create this, just look it up by name in the current scope (`thinkschool-rg`) and let me reference its `.id`. The Container App resource then sets `environmentId: containerAppsEnv.id` exactly as before — Container Apps doesn't care whether the env was created in the same deployment or already existed.

Second `azd provision` run output:

```
Creating/Updating resources
  (✓) Done: Container Registry: acroug3e3rejocbe (1m44.638s)
  (✓) Done: Container App: ca-api-oug3e3rejocbe (17.301s)

SUCCESS: Your application was provisioned in Azure in 3 minutes 11 seconds.
```

The 1m44s for ACR is dominated by name-availability checks + DNS provisioning for `acroug3e3rejocbe.azurecr.io`. The 17.3s Container App create includes pulling the placeholder hello-world image so the revision can become Active.

---

## `azd up` (end-to-end)

After provision + deploy worked separately, ran `azd up --no-prompt` to capture the canonical combined output:

```
> azd up --no-prompt

Provisioning and deploying (azd up)
Packaging overlaps with provisioning for faster execution.

  api: Packaging
Initialize bicep provider
Creating a deployment plan
Comparing deployment state
Validating deployment
Creating/Updating resources

  (✓) Done: Container Registry: acroug3e3rejocbe (12.955s)
  (✓) Done: Container App: ca-api-oug3e3rejocbe (17.493s)
  api: Publishing
  api: Publishing (Logging into registry) [2s]
  api: Publishing (Publishing container image) [5s]
  api: Deploying [13s]
  api: Deploying (Updating container app revision) [13s]
  api: Deploying (Waiting for container revision (15s)) [28s]
  api: Deploying (Fetching endpoints for service) [35s]
  api: Done [36s]
  - Endpoint: https://ca-api-oug3e3rejocbe.purplecoast-dcd0caac.southeastasia.azurecontainerapps.io/

SUCCESS: Your application was provisioned and deployed to Azure in 1 minute 59 seconds.
  Provisioning: 1 minute 23 seconds
  Deploying:    36 seconds
```

A few things worth noting in that log:

- **"Packaging overlaps with provisioning for faster execution"** — `azd up` runs `dotnet publish` in parallel with the Bicep deployment. The image build (`api: Packaging`) starts at second 0 even though the ACR doesn't exist yet; it builds the image *locally* first, then pushes to ACR once the registry exists. Saves ~30s on a cold provision.
- **ACR took 12.955s the second time vs 1m44.638s the first** — because the ARM deployment is idempotent and "no changes" on an existing ACR returns in seconds. Same for the Container App (17.493s, mostly the placeholder image pull, which was already cached).
- **"Updating container app revision"** is the step where the placeholder image gets replaced with the real one. Container Apps doesn't restart the existing revision; it creates a *new* revision pointing at the new image, waits for the readiness probe to pass on the new revision, then shifts traffic. The "Waiting for container revision (15s)" step is the readiness probe poll loop.
- **"Fetching endpoints for service"** is what writes `SERVICE_API_URI` back to `.azure/piece4/.env`. The endpoint URL is derived from the Container App's `properties.configuration.ingress.fqdn`, which is `<app-name>.<env-default-domain>`.

---

## Verification

```pwsh
curl -i https://ca-api-oug3e3rejocbe.purplecoast-dcd0caac.southeastasia.azurecontainerapps.io/health
```

```
HTTP/1.1 200 OK
content-type: application/json; charset=utf-8
date: Mon, 25 May 2026 15:36:02 GMT
server: Kestrel
transfer-encoding: chunked

{"status":"ok"}
```

The `server: Kestrel` header is the proof that the request reached the .NET process inside the container — Container Apps' front door doesn't add this header itself. Same 200 / `{"status":"ok"}` body as the local `docker run` in [piece 2](../piece-2/docker-output.md#3-curl-to-health), just over the Container Apps ingress instead of host port 8080.

`GET /` returns `HTTP/1.1 404 Not Found` with `server: Kestrel` — that's the .NET minimal-API correctly saying "no route at the root path," not a deployment failure. The only mapped routes are `/health` and `/api/{auth,quotes,collections}/...`.

---

## What I didn't do (and why those would be the next steps)

- **No Key Vault wiring.** `KeyVault__Uri` is empty in the Container App's env vars, which means the `AddAzureKeyVault` call in `Program.cs` is skipped and `AppInsights:ConnectionString` resolves to empty (so OTel falls back to OTLP/Jaeger which isn't reachable from Container Apps). A real production deploy would (1) give the MI `Key Vault Secrets User` on `kv-avishkar` and (2) set `KeyVault__Uri=https://kv-avishkar.vault.azure.net/`. The MI's client ID is already wired through via `AZURE_CLIENT_ID` for exactly this reason.
- **SQLite on the container's writable scratch dir.** `Data Source=/tmp/quotes.db` survives a container restart only because Container Apps gives each replica a writable `/tmp` — but on scale-out to multiple replicas, each replica gets its own `/tmp/quotes.db` and they diverge silently. The real fix is to replace SQLite with Azure SQL or Postgres flex-server, which is what later pieces will do.
- **No CI/CD pipeline.** `azd pipeline config` would scaffold a GitHub Actions workflow that runs `azd provision` + `azd deploy` on push, with federated identity instead of a service principal. Not in scope for this piece but mentioned because it's the natural next step.
- **Bicep is hand-written, not from `azd init`.** Functionally identical to what `azd init --from-code` would have generated, but means the scaffold doesn't automatically pick up new azd defaults if they ship in a future version.
- **Scale to zero is on.** Cold-start on the first request after idle is real. A latency-sensitive demo would set `minReplicas: 1` and accept the always-on cost.

---

## Exercise reflection

### Q1 — What did you learn this session?

The idea that clicked is that `azd` is just a thin orchestrator over three things I already know how to do separately — Bicep at the bottom, `docker build`/`dotnet publish -t:PublishContainer` in the middle, `az containerapp update` at the top — and what it actually buys you is a *protocol* for tying them together. The `azd-service-name: api` tag on the Container App is the whole magic: provision lays down a placeholder revision with that tag, deploy builds the image, looks up the tagged app, and swaps the image. Once I saw that, the file layout stopped feeling magical — `azure.yaml` is just "which Bicep output goes to which service" and the Bicep is "stamp the right tags so deploy can find me later." That mental model is the one I'll keep.

The other thing I'll keep is `azd config set auth.useAzCliAuth true`. The default `azd auth login` is its own MSAL cache, separate from `az`, and the obvious-looking workaround (create a service principal, log in with `--client-id`/`--client-secret`) was actively the wrong instinct — it mints a long-lived credential that wasn't needed because `az` was already authenticated. The one-line config flip makes `azd` walk the `az` token cache instead, which is the same identity, no extra blast radius, no cleanup. Worth knowing because the official azd docs lead with `azd auth login` and don't surface this until you go digging.

### Q2 — What would break this?

The thing that *did* break it once and would break it again on a clean subscription: **per-region quotas at the Container Apps environment level**. The first `azd provision` failed preflight with `MaxNumberOfRegionalEnvironmentsInSubExceeded` because the student plan caps managed environments at 1 per region and `thinkschool-env` from piece 3 already filled that slot. The fix here was to reuse the existing env via `existing` in Bicep, but on a non-student subscription with a free hand you'd never see this — and on a student subscription with two team members both running `azd up` in the same region you'd see it constantly. The general failure mode: `azd` templates default to "stamp a fresh environment per `azd env`" and that assumption is silently wrong under quota.

The other failure mode I haven't tested but know is there: **the placeholder-image trick during the very first `provision`**. The Bicep deploys the Container App with `mcr.microsoft.com/azuredocs/containerapps-helloworld:latest` when `apiExists` is false, on the assumption that the subsequent `deploy` step will swap it. If a `provision` succeeds but the `deploy` then fails (bad Dockerfile, image push timeout, ACR pull RBAC propagation race), I'm left with a "deployed" container app that's actually serving the Microsoft hello-world page. `/health` would 404 and nothing in the `azd up` exit code would tell me — only hitting the endpoint would. A real production template would either use a `deploymentScripts` resource to look up the most-recently-pushed image at provision time, or fail loud if the image isn't there yet.

Other near-misses worth flagging:

- **Container App `properties.template.containers[].image` is part of the revision template, not configuration.** That means every `azd deploy` mutates the revision template, which spawns a new revision. With `activeRevisionsMode: 'Single'` this is fine (old one is decommissioned); in `Multiple` mode you'd quickly accumulate revisions per CI build. Nothing in `azure.yaml` says "deploys create revisions" — it's implicit in how Container Apps interprets the resource.
- **The `auth.useAzCliAuth` flag is per-user-config, not per-project.** If I sit down at this repo tomorrow on a different machine without that flag set, `azd up` will silently switch to its own MSAL cache and prompt for a browser login — different identity, potentially different RBAC. Nothing in the repo records the requirement; only this README does.
- **`SERVICE_API_RESOURCE_EXISTS` defaults to `false` in `main.parameters.json` but azd flips it to `true` after the first deploy.** This is fine when `azd up` walks both phases in order; if someone runs `azd provision` standalone against a stale `.azure/piece4/.env`, the Container App gets re-stamped with the placeholder image, replacing the real image. The Bicep should arguably look up the current image from the existing Container App rather than trusting the env var.

---

## Links

- **Repository:** [https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil)
- **Folder:** [Day-5/piece-4](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil/tree/main/Day-5/piece-4)
- **Exercise deliverable:** [output.md](output.md)
- **Previous piece:** [Day-5/piece-3 — Azure Container Apps environment](../piece-3/README.md)
- **Live URL:** [https://ca-api-oug3e3rejocbe.purplecoast-dcd0caac.southeastasia.azurecontainerapps.io/health](https://ca-api-oug3e3rejocbe.purplecoast-dcd0caac.southeastasia.azurecontainerapps.io/health)
