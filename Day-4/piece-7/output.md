# Day 4 · Piece 6 — Azure App Insights submission

## App Insights connection setup

The connection string is **never** in source. It's written to Key Vault by the provisioning script, then pulled into `IConfiguration` at startup via `DefaultAzureCredential`.

**Step 1 — Provisioned in Azure** ([docs/provision.sh](docs/provision.sh)):

```bash
az monitor app-insights component create \
  --app appinsights-avishkar \
  --location southeastasia \
  --resource-group rg-avishkar \
  --workspace "$(az monitor log-analytics workspace show \
                  -g rg-avishkar -n law-avishkar --query id -o tsv)" \
  --kind web --application-type web

az keyvault secret set \
  --vault-name kv-avishkar \
  --name AppInsights--ConnectionString \
  --value "$(az monitor app-insights component show \
              --app appinsights-avishkar -g rg-avishkar \
              --query connectionString -o tsv)"
```

**Step 2 — Key Vault provider in [Program.cs](Program.cs):**

```csharp
var keyVaultUri = builder.Configuration["KeyVault:Uri"]; // https://kv-avishkar.vault.azure.net/
if (!string.IsNullOrWhiteSpace(keyVaultUri)
    && !builder.Environment.IsEnvironment("Testing"))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUri),
        new DefaultAzureCredential());
}
```

The secret name `AppInsights--ConnectionString` becomes the config key `AppInsights:ConnectionString`.

**Step 3 — OTel wire-up in [Extensions/InfrastructureExtensions.cs](Extensions/InfrastructureExtensions.cs):**

```csharp
var appInsightsConnection = configuration["AppInsights:ConnectionString"];

services
    .AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(QuotesTelemetry.ServiceName))
    .UseAzureMonitor(o => o.ConnectionString = appInsightsConnection)
    .WithTracing(t => t
        .AddSource(QuotesTelemetry.ServiceName)
        .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true));
```

`UseAzureMonitor()` is the single line that ships logs + metrics + traces to App Insights. The `WithTracing` block layers in the EF Core instrumentation and the custom `QuotesApi` `ActivitySource` on top of the AspNetCore + HttpClient instrumentation that `UseAzureMonitor` adds for free.

---

## KQL — slowest 10 requests in the last hour

```kql
requests
| where timestamp > ago(1h)
| top 10 by duration desc
| project timestamp, name, url, duration, resultCode, operation_Id, user_Id
```

`duration` is in milliseconds. `operation_Id` is the W3C trace ID — paste it back into a `union requests, dependencies, traces` query to see everything the slowest request did, including each EF query.

---

## Q1 — What did you learn this session?

The thing that clicked is that **`UseAzureMonitor()` is the smallest meaningful chunk of telemetry you can ship to prod.** One line, and I get traces, dependency spans, logs, exceptions, runtime metrics, *and* live metrics — all correlated by the same trace ID I was already emitting in Serilog. The model is consistent end-to-end: in piece 4 my logs got a TraceId. In piece 5 OTel started emitting traces with that same TraceId to Jaeger. In piece 6 I just swapped the exporter — the *code* that produces spans didn't change. The other idea I'll keep is that **the connection string is just a config value**, not a piece of infrastructure. Putting it in Key Vault with a `--` instead of a `:` in the secret name means everything I already understand about `IConfiguration` (sectioned reads, environment override, the Options pattern) still works. There's no Azure-flavoured "secret-fetching" API my domain code has to know about — `configuration["AppInsights:ConnectionString"]` is the whole interface, and the provider chain underneath decides whether that string came from `appsettings.json`, an env var, or Key Vault. Layered config is the abstraction; Key Vault is just another layer.

## Q2 — What would break this?

The most realistic failure mode is **`DefaultAzureCredential` silently picking up the *wrong* identity**. In dev it walks: Visual Studio → Azure CLI → environment variables → managed identity. If I'm signed into `az` as the right tenant but VS is signed into a stale account, VS wins and the call to Key Vault fails with a confusing 403 — and the app starts up *anyway*, because my code falls back to the empty config value and quietly emits no telemetry. There's no loud error; the symptom is that App Insights stays blank. The fix would be either `ExcludeVisualStudioCredential = true` to pin the order, or — better — fail loud if `AppInsights:ConnectionString` is empty in `Production`. The second one is **cost shape**. `UseAzureMonitor` ships *every* request + *every* EF query + *every* log line at default sampling, and the workspace-based pricing tier ingests by GB. A hot read endpoint at 100 RPS times a 200-byte trace times a chatty EF query times Serilog's structured properties adds up to a daily ingest bill that's wildly out of proportion to the value, especially during a load test. The mitigation is the same as in piece 5: a `TraceIdRatioBasedSampler(0.1)` on the trace pipeline, and the App Insights *adaptive sampling* knob (it's on by default for new resources — worth confirming, not assuming). The third is **the alert as written counts an empty window as "no data, no fire"**, which is the right default *except* during a partial outage where the API is up enough to return 500s but no successful POSTs are reaching `summarize` — the alert never fires because the `summarize` row doesn't exist, and I find out from a user that the endpoint has been broken for an hour. The fix is to add a parallel "request rate dropped to zero" alert on the same scope.

---

## Resource summary

| Resource | Name |
| --- | --- |
| Resource group | `rg-avishkar` |
| Log Analytics workspace | `law-avishkar` |
| App Insights | `appinsights-avishkar` |
| Key Vault | `kv-avishkar` |
| Action group | `ag-avishkar` (email avishkarpatil071@gmail.com) |
| Alert rule | `alert-post-quotes-latency-avishkar` (avg POST /api/quotes > 500 ms / 5 min, sev 2) |

## GitHub link

- **Repository:** [https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil)
- **Folder:** [Day-4/piece-6](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil/tree/main/Day-4/piece-6)
