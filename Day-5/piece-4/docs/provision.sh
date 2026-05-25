#!/usr/bin/env bash
# Reproducible provisioning for Day 4 / piece 6.
# Idempotent: re-running against existing resources updates them in place.
# Run from Git Bash on Windows, with MSYS_NO_PATHCONV=1 so resource IDs (which
# start with `/`) are not rewritten into local Windows paths.
set -euo pipefail
export MSYS_NO_PATHCONV=1

LOCATION=${LOCATION:-southeastasia}
RG=${RG:-rg-avishkar}
LAW=${LAW:-law-avishkar}
APPI=${APPI:-appinsights-avishkar}
KV=${KV:-kv-avishkar}
AG=${AG:-ag-avishkar}
ALERT=${ALERT:-alert-post-quotes-latency-avishkar}
EMAIL=${EMAIL:-avishkarpatil071@gmail.com}

# 0. Make sure the providers we touch are registered (one-time per subscription).
az provider register -n Microsoft.Insights            --wait
az provider register -n Microsoft.OperationalInsights --wait
az provider register -n Microsoft.KeyVault            --wait

# 1. Resource group.
az group create --name "$RG" --location "$LOCATION" >/dev/null

# 2. Log Analytics workspace (App Insights is workspace-based in 2026).
az monitor log-analytics workspace create \
  --resource-group "$RG" --workspace-name "$LAW" --location "$LOCATION" >/dev/null
WORKSPACE_ID=$(az monitor log-analytics workspace show \
  --resource-group "$RG" --workspace-name "$LAW" --query id -o tsv)

# 3. App Insights component linked to the workspace.
az extension add --name application-insights --yes --only-show-errors
az monitor app-insights component create \
  --app "$APPI" --location "$LOCATION" --resource-group "$RG" \
  --workspace "$WORKSPACE_ID" --kind web --application-type web >/dev/null
CONN=$(az monitor app-insights component show \
  --app "$APPI" --resource-group "$RG" --query connectionString -o tsv)

# 4. Key Vault (RBAC mode) + role grant to the signed-in user.
az keyvault create \
  --name "$KV" --resource-group "$RG" --location "$LOCATION" \
  --enable-rbac-authorization true >/dev/null
KV_ID=$(az keyvault show --name "$KV" --query id -o tsv)
ME_OID=$(az ad signed-in-user show --query id -o tsv)
az role assignment create \
  --assignee-object-id "$ME_OID" --assignee-principal-type User \
  --role "Key Vault Secrets Officer" --scope "$KV_ID" >/dev/null || true

# RBAC propagation: retry a few times before writing the secret.
for i in 1 2 3 4 5; do
  if az keyvault secret set \
       --vault-name "$KV" --name AppInsights--ConnectionString \
       --value "$CONN" >/dev/null 2>&1; then
    echo "Secret stored on attempt $i."
    break
  fi
  echo "Secret write failed (attempt $i), sleeping 10s..."
  sleep 10
done

# 5. Action group: email me.
az monitor action-group create \
  --name "$AG" --resource-group "$RG" --short-name agavishkar \
  --action email avishkar-email "$EMAIL" >/dev/null
AG_ID=$(az monitor action-group show --name "$AG" --resource-group "$RG" --query id -o tsv)
AI_ID=$(az monitor app-insights component show \
  --app "$APPI" --resource-group "$RG" --query id -o tsv)

# 6. Scheduled-query alert: avg POST /api/quotes latency > 500ms over 5 min.
az extension add --name scheduled-query --yes --only-show-errors

KQL='requests | where timestamp > ago(5m) | where name == "POST Quotes/Create" or name == "POST /api/quotes" or (tostring(customDimensions["http.request.method"]) == "POST" and url endswith "/api/quotes") | summarize AvgMs = avg(duration)'

az monitor scheduled-query create \
  --name "$ALERT" \
  --resource-group "$RG" \
  --scopes "$AI_ID" \
  --description "POST /api/quotes average latency exceeds 500ms over 5 minutes" \
  --severity 2 \
  --evaluation-frequency 5m \
  --window-size 5m \
  --condition 'avg AvgMs from "latencyQ" > 500' \
  --condition-query latencyQ="$KQL" \
  --action-groups "$AG_ID" >/dev/null

echo "Done. App Insights resource: $AI_ID"
echo "Connection string lives in Key Vault $KV as secret 'AppInsights--ConnectionString'."
