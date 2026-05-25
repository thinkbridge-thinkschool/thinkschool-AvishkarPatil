# Day 5 · Piece 4 — Deploy via `azd`

Azure Developer CLI orchestrates build + push + deploy in one command. `azure.yaml` + `infra/*.bicep` describe the shape; `azd up` makes it real and prints the live URL.

---

## 1. `azure.yaml`

The whole file — one service, dotnet, hosted on Container Apps.

```yaml
# yaml-language-server: $schema=https://raw.githubusercontent.com/Azure/azure-dev/main/schemas/v1.0/azure.yaml.json

name: quotes-api-piece-4
metadata:
  template: quotes-api-piece-4@1.0.0
services:
  api:
    project: ./QuotesApi.csproj
    host: containerapp
    language: dotnet
```

The infra it points at lives in [infra/](infra/) — [main.bicep](infra/main.bicep) (subscription-scoped wrapper), [resources.bicep](infra/resources.bicep) (ACR + user-assigned MI + AcrPull role + Container App), [main.parameters.json](infra/main.parameters.json) (env-var-driven parameters).

Two deviations from the stock `azd init` scaffold, both forced by the Azure-for-Students subscription:

- **Reuses the Container Apps environment from [piece-3](../piece-3/output.md)** (`thinkschool-env` in `thinkschool-rg`). The subscription is capped at **1 managed environment per region** in southeastasia, so creating a fresh one fails preflight with `MaxNumberOfRegionalEnvironmentsInSubExceeded`. `main.bicep` references the existing env with `existing` and deploys the new ACR + Container App into the same RG.
- **Region pinned to `southeastasia`.** The student subscription's `Allowed resource deployment regions` policy blocks everything except `austriaeast, eastasia, uaenorth, malaysiawest, southeastasia`, and `southeastasia` is the one that also offers `Microsoft.App/managedEnvironments`.

`azd` was wired to reuse the existing `az` CLI login (no separate browser/device-code flow, no service principal) with:

```pwsh
azd config set auth.useAzCliAuth true
azd env new piece4 --location southeastasia --subscription bbcfff0f-9093-4e77-97cb-dc0b230a1707
```

---

## 2. `azd up` output

```
> azd up --no-prompt

Initialize bicep provider

Provisioning and deploying (azd up)
Packaging overlaps with provisioning for faster execution.

  api: Packaging
Initialize bicep provider
Creating a deployment plan
Comparing deployment state
Validating deployment
Creating/Updating resources
  You can view detailed progress in the Azure Portal:
  https://portal.azure.com/#view/HubsExtension/DeploymentDetailsBlade/~/overview/id/...

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

The image was built by the .NET SDK's `PublishContainer` target (no Dockerfile in the repo) and pushed straight to ACR — same csproj container properties as [piece-2](../piece-2/docker-output.md#1-csproj-container-properties), just a remote registry this time.

After the deploy the resource group looks like this:

| Resource | Name | Notes |
|---|---|---|
| Container Apps env (reused) | `thinkschool-env` | from piece-3 |
| Container Registry (new) | `acroug3e3rejocbe` | Basic SKU, ACR-pull granted to MI |
| User-assigned MI (new) | `mi-api-oug3e3rejocbe` | `AcrPull` on the registry |
| Container App (new) | `ca-api-oug3e3rejocbe` | tagged `azd-service-name: api`, 0.5 CPU / 1 GiB, 0-3 replicas |

The Container App is configured with `ConnectionStrings__Default=Data Source=/tmp/quotes.db` and `KeyVault__Uri=""` so it starts cleanly without an Azure dependency — same workaround as piece-2's `docker run` ([docker-output.md §2](../piece-2/docker-output.md#2-docker-run-output)). Liveness + readiness probes both hit `/health`.

---

## 3. Live URL

**https://ca-api-oug3e3rejocbe.purplecoast-dcd0caac.southeastasia.azurecontainerapps.io**

The FQDN follows the env's domain (`purplecoast-dcd0caac.southeastasia.azurecontainerapps.io`) with the app name as the subdomain — same `defaultDomain` we saw in piece-3's `env show` output.

---

## 4. `curl` to `/health`

```
$ curl -sS -i https://ca-api-oug3e3rejocbe.purplecoast-dcd0caac.southeastasia.azurecontainerapps.io/health
HTTP/1.1 200 OK
content-type: application/json; charset=utf-8
date: Mon, 25 May 2026 15:16:09 GMT
server: Kestrel
transfer-encoding: chunked

{"status":"ok"}
```

Same 200 / `{"status":"ok"}` as the local `docker run` in piece-2, just over Container Apps' fronting proxy instead of host port 8080. `server: Kestrel` confirms the request reached the .NET process inside the container, not a Container Apps default page.

---

## GitHub link

- **Repository:** [https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil)
- **Folder:** [Day-5/piece-4](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil/tree/main/Day-5/piece-4)

---

## Q1 — What did you learn this session?

The idea that clicked is that `azd` is just a thin orchestrator over three things I already know how to do separately — Bicep at the bottom, `docker build`/`dotnet publish -t:PublishContainer` in the middle, `az containerapp update` at the top — and what it actually buys you is a *protocol* for tying them together. The `azd-service-name: api` tag on the Container App is the whole magic: provision lays down a placeholder revision with that tag, deploy builds the image, looks up the tagged app, and swaps the image. Once I saw that, the file layout stopped feeling magical — `azure.yaml` is just "which Bicep output goes to which service" and the Bicep is "stamp the right tags so deploy can find me later." That mental model is the one I'll keep.

The other thing I'll keep is `azd config set auth.useAzCliAuth true`. The default `azd auth login` is its own MSAL cache, separate from `az`, and the obvious-looking workaround (create a service principal, log in with --client-id/--client-secret) was actively the wrong instinct — it mints a long-lived credential that wasn't needed because `az` was already authenticated. The one-line config flip makes `azd` walk the `az` token cache instead, which is the same identity, no extra blast radius, no cleanup. Worth knowing because the official azd docs lead with `azd auth login` and don't surface this until you go digging.

## Q2 — What would break this?

The thing that *did* break it once and would break it again on a clean subscription: **per-region quotas at the Container Apps environment level**. The first `azd provision` failed preflight with `MaxNumberOfRegionalEnvironmentsInSubExceeded` because the student plan caps managed environments at 1 per region and `thinkschool-env` from piece-3 already filled that slot. The fix here was to reuse the existing env via `existing` in Bicep, but on a non-student subscription with a free hand you'd never see this — and on a student subscription with two team members both running `azd up` in the same region you'd see it constantly. The general failure mode: `azd` templates default to "stamp a fresh environment per `azd env`" and that assumption is silently wrong under quota.

The other failure mode I haven't tested but know is there: **the placeholder-image trick during the very first `provision`**. My `resources.bicep` deploys the Container App with `mcr.microsoft.com/azuredocs/containerapps-helloworld:latest` when `apiExists` is false, on the assumption that the subsequent `deploy` step will swap it. If a `provision` succeeds but the `deploy` then fails (bad Dockerfile, image push timeout, ACR pull RBAC propagation race), I'm left with a "deployed" container app that's actually serving the Microsoft hello-world page. `/health` would 404 and nothing in the `azd up` exit code would tell me — only hitting the endpoint would. A real production template would either use a `deploymentScripts` resource to look up the most-recently-pushed image at provision time, or fail loud if the image isn't there yet.

Other near-misses worth flagging:

- **Container App `properties.template.containers[].image` is part of the revision template, not configuration.** That means every `azd deploy` mutates the revision template, which spawns a new revision. With `activeRevisionsMode: 'Single'` this is fine (old one is decommissioned), but in `Multiple` mode you'd quickly accumulate revisions per CI build. Easy to overlook because nothing in `azure.yaml` says "deploys create revisions" — it's implicit in how Container Apps interprets the resource.
- **The `auth.useAzCliAuth` flag is per-user-config, not per-project.** If I sit down at this repo tomorrow on a different machine without that flag set, `azd up` will silently switch to its own MSAL cache and prompt for a browser login — different identity, potentially different RBAC. Nothing in the repo records the requirement; only `output.md` does.
- **`SERVICE_API_RESOURCE_EXISTS` defaults to `false` in `main.parameters.json` but azd sets it to `true` after the first deploy.** This is fine when `azd up` walks both phases in order, but if someone runs `azd provision` standalone against a stale state file, the Container App would be re-stamped with the helloworld placeholder, replacing the real image. The Bicep should arguably look up the current image from the existing Container App rather than trusting the env var.
