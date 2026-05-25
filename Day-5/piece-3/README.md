# Day 5 · Piece 3 — Azure Container Apps fundamentals

Piece 2 produced a portable OCI image (`quotes-api:0.1.0`) via `dotnet publish -t:PublishContainer`. This piece sets up the Azure surface that will run it: a **resource group**, a **Container Apps environment**, and the small pile of subscription-level prerequisites the docs gloss over (resource providers, CLI extension, region allow-list). No app is deployed yet — that's the next piece. The deliverable here is "the boundary exists, here is its JSON."

The concise exercise submission lives in [output.md](output.md). This README is the longer write-up: what each concept means, why each command is shaped the way it is, what went wrong on the first try, and what's still missing.

---

## What Container Apps actually is (one paragraph each)

**The environment** is a logical boundary — not a runtime. It holds shared networking (one VNet, one shared egress IP), a single Log Analytics workspace that every app inside it writes to, and one DNS suffix (`*.<random>.<region>.azurecontainerapps.io`) under which every app inside the env is hosted. No CPU or memory is allocated to the environment itself; you don't pay for an empty env beyond the Log Analytics ingestion floor.

**An app** is a deployment of one container image plus a scale rule. Inside an env you can have many apps that don't know about each other, or many that can address each other on internal DNS without leaving the VNet. Each app declares its own ingress (external, internal, or none), its own target port, its own scale rule (HTTP concurrency, CPU, queue length, anything KEDA supports).

**Revisions** are immutable snapshots of an app. Every meaningful change (new image tag, new env var, new scale rule) creates a new revision; multiple revisions can be active at the same time with traffic split between them. Blue-green and canary aren't features bolted on top of "deployments" — they fall out of the revision model directly. You don't roll *back*; you shift traffic *off* the new revision and *onto* the old one, both of which are still running.

---

## Prerequisites this piece had to clear

The piece instructions show three friendly `az` commands. Real first-time setup on this subscription needed five more:

```pwsh
# 1. Resource provider for Container Apps itself
az provider register -n Microsoft.App --wait

# 2. Resource provider for Log Analytics (the env auto-creates a workspace)
az provider register -n Microsoft.OperationalInsights --wait

# 3. The containerapp CLI extension (it's preview; -y suppresses the prompt)
az extension add -n containerapp --upgrade -y
```

Registering a provider is idempotent but not instant — `Microsoft.App` took ~90s in this subscription. The `--wait` flag is important; without it the next command race-conditions against the registration and fails with a "subscription is not registered" error that doesn't suggest the actual cause.

The `containerapp` extension is still flagged as preview by Azure CLI; the `--upgrade -y` keeps the install non-interactive in case it's already installed at an older version.

---

## Resource group

```pwsh
az group create -n thinkschool-rg -l centralindia
```

Output:

```json
{
  "id": "/subscriptions/bbcfff0f-9093-4e77-97cb-dc0b230a1707/resourceGroups/thinkschool-rg",
  "location": "centralindia",
  "name": "thinkschool-rg",
  "properties": { "provisioningState": "Succeeded" },
  "type": "Microsoft.Resources/resourceGroups"
}
```

The RG already existed from earlier pieces, so this returned the existing one rather than creating anything new — `az group create` is idempotent on `name+location`.

**Location note.** The RG sits in `centralindia` but the env will end up in `southeastasia`. That's allowed: a resource group's location only controls where its *metadata* lives, not where resources inside it are deployed. Each resource picks its own region.

---

## Container Apps environment — first attempt (failed)

The straightforward command per the piece instructions:

```pwsh
az containerapp env create -n thinkschool-env -g thinkschool-rg -l centralindia
```

Failed:

```
WARNING: No Log Analytics workspace provided.
WARNING: Generating a Log Analytics workspace with name "workspace-thinkschoolrgLIZE"
ERROR: (RequestDisallowedByAzure) Resource 'workspace-thinkschoolrgLIZE' was disallowed
by Azure: This policy maintains a set of best available regions where your subscription
can deploy resources.
```

This is the **Azure-for-Students region allow-list** at work. Reading the assigned policy directly:

```pwsh
az policy assignment list --query "[?contains(displayName, 'region')]
  .{name:displayName, params:parameters}" -o json
```

```json
[
  {
    "name": "Allowed resource deployment regions",
    "params": {
      "listOfAllowedLocations": {
        "value": [
          "austriaeast", "eastasia", "uaenorth",
          "malaysiawest", "southeastasia"
        ]
      }
    }
  }
]
```

`centralindia` isn't on the list, so the auto-generated Log Analytics workspace was rejected. The env never even started provisioning.

The misleading part is *which* resource the error names. It says "Resource `workspace-thinkschoolrgLIZE` was disallowed" — the *workspace*, not the env. If you read past the warnings without noticing the auto-create line you'd think Container Apps itself is unsupported, which isn't true (it is — `centralindia` is in `Microsoft.App/managedEnvironments` regions).

---

## Container Apps environment — second attempt (works)

Cross-referencing the allow-list with what `Microsoft.App` actually supports:

```pwsh
az provider show -n Microsoft.App `
  --query "resourceTypes[?resourceType=='managedEnvironments'].locations[]" -o tsv
```

Returns ~35 regions including `Southeast Asia`, `East Asia`, `UAE North`. The intersection with the allow-list is `southeastasia`, `eastasia`, `uaenorth`. I picked `southeastasia` for the lowest expected latency from India.

```pwsh
az containerapp env create -n thinkschool-env -g thinkschool-rg -l southeastasia
```

Provisioning succeeded. Auto-generated workspace `workspace-thinkschoolrgLIZE` landed in the same region. Key fields from the create response:

| Field | Value |
| --- | --- |
| `location` | `Southeast Asia` |
| `provisioningState` | `Succeeded` |
| `defaultDomain` | `purplecoast-dcd0caac.southeastasia.azurecontainerapps.io` |
| `staticIp` | `4.145.31.56` |
| `environmentMode` | `WorkloadProfiles` |
| `workloadProfiles[0]` | `Consumption` (the default; no dedicated profile yet) |
| `peerTrafficConfiguration.encryption.enabled` | `false` |
| `publicNetworkAccess` | `Enabled` |
| `daprConfiguration.version` | `1.16.4-msft.6` |
| `kedaConfiguration.version` | `2.18.1` |

`defaultDomain` is the bit that matters in a few pieces' time — any app I deploy here will get `https://<app>.purplecoast-dcd0caac.southeastasia.azurecontainerapps.io` as its public URL, free TLS included.

---

## `az containerapp env show`

The full JSON is in [output.md](output.md#3-az-containerapp-env-show). Three fields worth flagging here:

**`appLogsConfiguration.logAnalyticsConfiguration.customerId`** is the workspace ID (`6f8183fc-632a-4a78-b985-f647696b71b9`). Every future app in this env writes its stdout/stderr to this workspace as `ContainerAppConsoleLogs_CL`. That's how observability bootstraps for free.

**`staticIp: 4.145.31.56`** is the env's shared egress IP. Anything an app inside this env calls out to (an upstream API, a database with IP allow-listing) will see this IP. There's no per-app egress identity unless I add a NAT gateway in front, which I haven't.

**`ingressConfiguration: null`** at the env level. Ingress is configured per-app, not on the env. Easy to assume otherwise from the way the docs phrase "the environment provides ingress."

---

## What I didn't do (and why those would be the next steps)

- **No app deployed yet.** That's piece 4 — `az containerapp create --image <ACR>/quotes-api:0.1.0 --target-port 8080 --ingress external --scale-rule ...`. Image needs to be pushed to ACR first (also next piece).
- **Auto-generated Log Analytics workspace.** Fine for a one-off exercise; a problem in a real deploy because the workspace doesn't follow any naming convention, doesn't get tags, doesn't share retention policy with anything else, and lives or dies with whichever env first triggered it. A real setup would pre-create the workspace with `az monitor log-analytics workspace create` and pass `--logs-workspace-id` explicitly.
- **No VNet integration.** The env is on the default Microsoft-managed network. For a real production app talking to a private SQL or Key Vault you'd want `--infrastructure-subnet-resource-id` pointed at a delegated subnet.
- **Consumption workload profile only.** Cheap and serverless but cold-start-y. A latency-sensitive app would add a `D4` or `D8` dedicated profile with `az containerapp env workload-profile add` and pin the app to it.
- **`peerTrafficConfiguration.encryption.enabled: false`.** Pod-to-pod traffic inside the env is not encrypted by default. Fine for non-PII demos; not fine for compliance workloads. Toggle with `--enable-peer-to-peer-encryption true`.

---

## Exercise reflection

### Q1 — What did you learn this session?

The thing that clicked is that the **environment is not the runtime, it's the boundary** — it doesn't run any of my code on its own. It's a shared shell that gives a group of future apps the same private network, the same Log Analytics workspace (`workspace-thinkschoolrgLIZE` here), the same shared egress IP (`4.145.31.56`), and one DNS suffix (`*.purplecoast-dcd0caac.southeastasia.azurecontainerapps.io`). The actual workload comes later when I `az containerapp create` an app inside it. That mental model is the one I'll keep — the env is to apps what a Kubernetes namespace+VNet is to pods: a unit of *sharing*, not a unit of *running*.

The other idea I'll keep is **revisions as immutable snapshots**. Every change to an app produces a new revision; blue-green and canary aren't extra features bolted on top, they're just "leave the old revision running and shift traffic weight onto a new one." That's why this whole platform is described as serverless containers rather than as a deployment service — the platform's primitive is the revision, not the rollout.

### Q2 — What would break this?

The thing that already broke it once: **subscription-level region policy versus the resource group's region**. My RG was created in `centralindia` (allowed historically) but Azure-for-Students now enforces a five-region allow-list and `centralindia` is no longer on it — so `az containerapp env create -l centralindia` silently tried to provision its auto-generated Log Analytics workspace there and got `RequestDisallowedByAzure` with no hint that the *workspace*, not the env, was the disallowed resource. The fix was to pass `-l southeastasia` on the env, but if I had passed `--logs-workspace-id` for a pre-existing workspace in `centralindia` I'd have hit the same wall with a different error message.

The general failure mode here: **region availability is at least three layers deep** — subscription policy ∩ `Microsoft.App` regional support ∩ `Microsoft.OperationalInsights` regional support — and the CLI surfaces only the bottom layer's rejection. So the obvious next step (retry the same command) doesn't help; you have to go read the policy assignment to find the allowed set, then cross-reference it with what each provider supports, and only then pick a region that satisfies all three.

Other near-misses worth flagging:

- **Implicit Log Analytics workspace creation.** Not passing `--logs-workspace-id` makes `az` auto-create one with a randomized name (`workspace-thinkschoolrgLIZE`). Fine for a one-off; a problem in production because nothing about that workspace follows any naming convention.
- **The shared egress IP.** If a future app in this env needs to call an upstream that allow-lists source IPs, every app shares `4.145.31.56` — there's no per-app egress identity unless you put a NAT gateway in front.
- **Race between `az provider register` and the next command.** Without `--wait`, the env-create command can fire before the provider finishes registering and fail with a "subscription is not registered" error that doesn't make the timing issue obvious.

---

## Links

- **Repository:** [https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil)
- **Folder:** [Day-5/piece-3](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil/tree/main/Day-5/piece-3)
- **Exercise deliverable:** [output.md](output.md)
