# Day 5 · Piece 3 — Azure Container Apps fundamentals

Resource group + Container Apps environment for the QuotesApi image from [piece-2](../piece-2/docker-output.md). Output of `az containerapp env show` at the bottom, then reflections.

---

## 1. Resource group

```pwsh
az group create -n thinkschool-rg -l centralindia
```

Output (trimmed):

```json
{
  "id": "/subscriptions/bbcfff0f-9093-4e77-97cb-dc0b230a1707/resourceGroups/thinkschool-rg",
  "location": "centralindia",
  "name": "thinkschool-rg",
  "properties": { "provisioningState": "Succeeded" },
  "type": "Microsoft.Resources/resourceGroups"
}
```

---

## 2. Container Apps environment

First-time setup on this subscription needed the resource providers and the CLI extension:

```pwsh
az provider register -n Microsoft.App --wait
az provider register -n Microsoft.OperationalInsights --wait
az extension add -n containerapp --upgrade -y
```

Create the env. Note the `-l southeastasia` — the Azure-for-Students policy
`Allowed resource deployment regions` blocks `centralindia` for new resources, so the auto-created Log Analytics workspace was rejected with `RequestDisallowedByAzure`. The five allowed regions are `austriaeast, eastasia, uaenorth, malaysiawest, southeastasia`; `southeastasia` is the one that also supports `Microsoft.App/managedEnvironments`.

```pwsh
az containerapp env create -n thinkschool-env -g thinkschool-rg -l southeastasia
```

`az` auto-generated Log Analytics workspace `workspace-thinkschoolrgLIZE` in the same region (no `--logs-workspace-id` was passed). Result: `provisioningState: Succeeded`, `defaultDomain: purplecoast-dcd0caac.southeastasia.azurecontainerapps.io`, `staticIp: 4.145.31.56`.

---

## 3. `az containerapp env show`

```pwsh
az containerapp env show -n thinkschool-env -g thinkschool-rg -o json
```

```json
{
  "id": "/subscriptions/bbcfff0f-9093-4e77-97cb-dc0b230a1707/resourceGroups/thinkschool-rg/providers/Microsoft.App/managedEnvironments/thinkschool-env",
  "location": "Southeast Asia",
  "name": "thinkschool-env",
  "properties": {
    "appInsightsConfiguration": null,
    "appLogsConfiguration": {
      "destination": "log-analytics",
      "logAnalyticsConfiguration": {
        "customerId": "6f8183fc-632a-4a78-b985-f647696b71b9",
        "dynamicJsonColumns": false,
        "sharedKey": null
      }
    },
    "availabilityZones": null,
    "customDomainConfiguration": {
      "certificateKeyVaultProperties": null,
      "certificatePassword": null,
      "certificateValue": null,
      "customDomainVerificationId": "AD21C7BDD4EB97B7ECFD4C1C447D4705A916E0FF67D6D4A09B7D88900AA0A992",
      "dnsSuffix": null,
      "expirationDate": null,
      "subjectName": null,
      "thumbprint": null
    },
    "daprAIConnectionString": null,
    "daprAIInstrumentationKey": null,
    "daprConfiguration": { "version": "1.16.4-msft.6" },
    "defaultDomain": "purplecoast-dcd0caac.southeastasia.azurecontainerapps.io",
    "diskEncryptionConfiguration": null,
    "environmentMode": "WorkloadProfiles",
    "eventStreamEndpoint": "https://southeastasia.azurecontainerapps.dev/subscriptions/bbcfff0f-9093-4e77-97cb-dc0b230a1707/resourceGroups/thinkschool-rg/managedEnvironments/thinkschool-env/eventstream",
    "infrastructureResourceGroup": null,
    "ingressConfiguration": null,
    "kedaConfiguration": { "version": "2.18.1" },
    "openTelemetryConfiguration": null,
    "peerAuthentication": { "mtls": { "enabled": false } },
    "peerTrafficConfiguration": { "encryption": { "enabled": false } },
    "provisioningState": "Succeeded",
    "publicNetworkAccess": "Enabled",
    "staticIp": "4.145.31.56",
    "vnetConfiguration": null,
    "workloadProfiles": [
      {
        "enableFips": false,
        "name": "Consumption",
        "workloadProfileType": "Consumption"
      }
    ],
    "zoneRedundant": false
  },
  "resourceGroup": "thinkschool-rg",
  "systemData": {
    "createdAt": "2026-05-25T14:16:30.7304474",
    "createdBy": "0120200528@msteams.mitaoe.ac.in",
    "createdByType": "User",
    "lastModifiedAt": "2026-05-25T14:16:30.7304474",
    "lastModifiedBy": "0120200528@msteams.mitaoe.ac.in",
    "lastModifiedByType": "User"
  },
  "type": "Microsoft.App/managedEnvironments"
}
```

---

## GitHub link

- **Repository:** [https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil)
- **Folder:** [Day-5/piece-3](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil/tree/main/Day-5/piece-3)

---

## Q1 — What did you learn this session?

The thing that clicked is that the **environment is not the runtime, it's the boundary** — it doesn't run any of my code on its own. It's a shared shell that gives a group of future apps the same private network, the same Log Analytics workspace (`workspace-thinkschoolrgLIZE` here), the same shared egress IP (`4.145.31.56`), and one DNS suffix (`*.purplecoast-dcd0caac.southeastasia.azurecontainerapps.io`). The actual workload comes later when I `az containerapp create` an app inside it. That mental model is the one I'll keep — the env is to apps what a Kubernetes namespace+VNet is to pods: a unit of *sharing*, not a unit of *running*.

The other idea I'll keep is **revisions as immutable snapshots**. Every change to an app produces a new revision; blue-green and canary aren't extra features bolted on top, they're just "leave the old revision running and shift traffic weight onto a new one." That's why this whole thing is described as serverless containers rather than as a deployment service — the platform's primitive is the revision, not the rollout.

## Q2 — What would break this?

The thing that already broke it once: **subscription-level region policy versus the resource group's region**. My RG was created in `centralindia` (allowed historically) but Azure-for-Students now enforces a five-region allow-list and `centralindia` is no longer on it — so `az containerapp env create -l centralindia` silently tried to provision its auto-generated Log Analytics workspace there and got `RequestDisallowedByAzure` with no hint that the *workspace*, not the env, was the disallowed resource. The fix was to pass `-l southeastasia` on the env, but if I had passed `--logs-workspace-id` for a pre-existing workspace in `centralindia` I'd have hit the same wall with a different error.

The general failure mode here: **region availability is at least three layers deep** — subscription policy ∩ `Microsoft.App` regional support ∩ `Microsoft.OperationalInsights` regional support — and the CLI surfaces only the bottom layer's rejection. So the obvious next step (retry the same command) doesn't help; you have to go read the policy assignment to find the allowed set, then cross-reference it with what each provider supports, and only then pick a region that satisfies all three.

Other near-misses I noticed while doing this:

- **Implicit Log Analytics workspace creation.** Not passing `--logs-workspace-id` makes `az` auto-create one with a randomized name (`workspace-thinkschoolrgLIZE`). Fine for a one-off exercise; a problem in a real deploy because the workspace doesn't follow any naming convention, doesn't get tags, and lives or dies with whichever env first triggered it.
- **The shared egress IP `4.145.31.56`.** If a future app in this env needs to call an upstream that allow-lists IPs, all apps in the env share the same source IP — there's no per-app egress identity unless you put a NAT gateway in front.
- **`ingressConfiguration: null` at the env level.** Ingress is configured per-app, not on the env. Easy to assume otherwise from the docs phrasing.
