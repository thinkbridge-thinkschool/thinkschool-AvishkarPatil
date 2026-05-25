# Day 4 · Piece 6 — Connect to Azure App Insights

Local Jaeger was fine for getting OTel right; production telemetry lives somewhere it survives a laptop reboot. This piece ships logs, metrics, and traces from the Quotes API to **Azure Application Insights**, pulls the connection string from **Key Vault** (never hardcoded), and wires a real **scheduled-query alert** that emails me when the average POST /api/quotes latency goes over 500 ms.

---

## Azure resources

Provisioned in subscription `Azure for Students`, resource group **`rg-avishkar`**, region `southeastasia`.

| Resource | Name | Purpose |
| --- | --- | --- |
| Resource group | `rg-avishkar` | Container for everything below |
| Log Analytics workspace | `law-avishkar` | Backing store for App Insights (workspace-based mode) |
| App Insights | `appinsights-avishkar` | Logs + metrics + traces sink |
| Key Vault | `kv-avishkar` | Holds the App Insights connection string |
| Action Group | `ag-avishkar` | Sends email to my address on alert firing |
| Scheduled-query alert | `alert-post-quotes-latency-avishkar` | Fires when avg POST /api/quotes duration > 500 ms over 5 min |

The exact `az` calls that built them are in [docs/provision.sh](docs/provision.sh) — they're idempotent enough to re-run.

---

## Connection-string setup

### 1. Packages — [QuotesApi.csproj](QuotesApi.csproj)

```xml
<PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore"           Version="1.3.0" />
<PackageReference Include="Azure.Identity"                                   Version="1.14.2" />
<PackageReference Include="Azure.Extensions.AspNetCore.Configuration.Secrets" Version="1.4.0" />
```

The first one is the magic single-line wire-up. The other two are how the connection string travels from Key Vault into `IConfiguration` at startup.

### 2. Key Vault provider — [Program.cs](Program.cs)

```csharp
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri)
    && !builder.Environment.IsEnvironment("Testing"))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUri),
        new DefaultAzureCredential());
}
```

`KeyVault:Uri` is the only thing in [appsettings.json](appsettings.json) that points at Azure — it resolves to `https://kv-avishkar.vault.azure.net/`. `DefaultAzureCredential` does the credential dance for me: Visual Studio token → Azure CLI token → managed identity in prod, in that order, so the same code works locally and deployed.

The secret in the vault is named **`AppInsights--ConnectionString`**. Azure's config provider turns the `--` into the `:` separator, so it lands in `IConfiguration` as `AppInsights:ConnectionString` — exactly what the OTel wire-up reads.

```bash
az keyvault secret set \
  --vault-name kv-avishkar \
  --name AppInsights--ConnectionString \
  --value "$(az monitor app-insights component show \
              --app appinsights-avishkar -g rg-avishkar \
              --query connectionString -o tsv)"
```

The `Testing` guard exists because the integration tests use `WebApplicationFactory` and have no business reaching Azure — leaving it on would slow tests by ~3 s each as `DefaultAzureCredential` walks its chain and times out.

### 3. OTel wire-up — [Extensions/InfrastructureExtensions.cs](Extensions/InfrastructureExtensions.cs)

```csharp
var appInsightsConnection = configuration["AppInsights:ConnectionString"];
var useAzureMonitor = !string.IsNullOrWhiteSpace(appInsightsConnection);

var otel = services
    .AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(QuotesTelemetry.ServiceName));

if (useAzureMonitor)
{
    otel.UseAzureMonitor(o => o.ConnectionString = appInsightsConnection);
    otel.WithTracing(t => t
        .AddSource(QuotesTelemetry.ServiceName)
        .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true));
}
else
{
    otel.WithTracing(t => t
        .AddSource(QuotesTelemetry.ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true)
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());
}
```

`.UseAzureMonitor()` from `Azure.Monitor.OpenTelemetry.AspNetCore` is the whole point of this piece: one call sets up an **Azure-Monitor-flavoured exporter for all three pillars** (logs, metrics, traces) and adds the AspNetCore + HttpClient instrumentation by default. I then add the EF Core instrumentation and the custom `QuotesApi` `ActivitySource` on top.

The `else` branch is the local-dev fallback — if no connection string is configured (e.g. you're running offline), traces still flow to local Jaeger via OTLP exactly like piece 5. This is what makes the test suite work without Azure credentials.

---

## What lands where in App Insights

| App Insights table | What it holds | Where it comes from |
| --- | --- | --- |
| `requests` | One row per HTTP request — method, URL, duration, status code | `AddAspNetCoreInstrumentation` (auto-added by `UseAzureMonitor`) |
| `dependencies` | One row per outbound call — EF queries, HTTP clients | `AddEntityFrameworkCoreInstrumentation` + `AddHttpClientInstrumentation` |
| `traces` | Every `ILogger.Log*` call, with structured properties as `customDimensions` | `UseAzureMonitor` plugs into `Microsoft.Extensions.Logging` |
| `customMetrics` | `Meter`-emitted metrics + runtime counters (CPU, GC, GenN allocations) | `UseAzureMonitor` adds `RuntimeInstrumentation` automatically |
| `exceptions` | Unhandled exceptions surfaced by ASP.NET Core | Auto |

The trace ID I see in App Insights's `operation_Id` column is the **same W3C trace ID** that Serilog logs emit — so click a slow request in Application Map → jump to its `traces`/`dependencies` → that's the full story for that one user.

---

## KQL queries I actually use

App Insights opens at the **Logs** blade with a KQL prompt. These are the ones I keep saved.

**Slowest 10 requests in the last hour** *(the one the exercise asks for)*:

```kql
requests
| where timestamp > ago(1h)
| top 10 by duration desc
| project timestamp, name, url, duration, resultCode, operation_Id, user_Id
```

**The exact query the alert evaluates** *(POST /api/quotes avg latency, last 5 min)*:

```kql
requests
| where timestamp > ago(5m)
| where name == "POST Quotes/Create"
     or name == "POST /api/quotes"
     or (tostring(customDimensions["http.request.method"]) == "POST"
         and url endswith "/api/quotes")
| summarize AvgMs = avg(duration)
```

**Logs for a specific user, oldest first** *(the example from the prompt)*:

```kql
traces
| where timestamp > ago(15m)
| where customDimensions.UserId == "abc"
| order by timestamp asc
```

**Stitch a slow request to everything it did** *(pick one `operation_Id` from query #1, see all child spans + logs)*:

```kql
union requests, dependencies, traces, exceptions
| where operation_Id == "<paste-trace-id-here>"
| order by timestamp asc
| project timestamp, itemType, name, duration, resultCode, message
```

---

## Alert: avg POST /api/quotes > 500 ms

A **scheduled-query alert** (not a metric alert) — because the platform metric `requests/duration` doesn't slice cleanly on "POST and path = /api/quotes". KQL gives me exact control over the filter.

| Field | Value |
| --- | --- |
| Name | `alert-post-quotes-latency-avishkar` |
| Scope | `appinsights-avishkar` |
| Query | The "alert evaluates" KQL above |
| Threshold | `AvgMs > 500` |
| Evaluation frequency | 5 min |
| Window | 5 min |
| Severity | 2 (Warning) |
| Action group | `ag-avishkar` → email **avishkarpatil071@gmail.com** |

> *"Alerts that page only when they need to be acted on; everything else is a dashboard."* — that's why this lives at sev-2 with a 5-minute window, not sev-0 with a 1-minute window. A single outlier request shouldn't wake anyone up; a *sustained* 5-minute degradation should.

---

## Exercise reflection

### Q1 — What did you learn this session?

The thing that clicked is that **`UseAzureMonitor()` is the smallest meaningful chunk of telemetry you can ship to prod.** One line, and I get traces, dependency spans, logs, exceptions, runtime metrics, *and* live metrics — all correlated by the same trace ID I was already emitting in Serilog. The model is consistent end-to-end: in piece 4 my logs got a TraceId. In piece 5 OTel started emitting traces with that same TraceId to Jaeger. In piece 6 I just swapped the exporter — the *code* that produces spans didn't change. The other idea I'll keep is that **the connection string is just a config value**, not a piece of infrastructure. Putting it in Key Vault with a `--` instead of a `:` in the secret name means everything I already understand about `IConfiguration` (sectioned reads, environment override, the Options pattern) still works. There's no Azure-flavoured "secret-fetching" API my domain code has to know about — `configuration["AppInsights:ConnectionString"]` is the whole interface, and the provider chain underneath decides whether that string came from `appsettings.json`, an env var, or Key Vault. Layered config is the abstraction; Key Vault is just another layer.

### Q2 — What would break this?

The most realistic failure mode is **`DefaultAzureCredential` silently picking up the *wrong* identity**. In dev it walks: Visual Studio → Azure CLI → environment variables → managed identity. If I'm signed into `az` as the right tenant but VS is signed into a stale account, VS wins and the call to Key Vault fails with a confusing 403 — and the app starts up *anyway*, because my code falls back to the empty config value and quietly emits no telemetry. There's no loud error; the symptom is that App Insights stays blank. The fix would be either `ExcludeVisualStudioCredential = true` to pin the order, or — better — fail loud if `AppInsights:ConnectionString` is empty in `Production`. The second one is **cost shape**. `UseAzureMonitor` ships *every* request + *every* EF query + *every* log line at default sampling, and the workspace-based pricing tier ingests by GB. A hot read endpoint at 100 RPS times a 200-byte trace times a chatty EF query times Serilog's structured properties adds up to a daily ingest bill that's wildly out of proportion to the value, especially during a load test. The mitigation is the same as in piece 5: a `TraceIdRatioBasedSampler(0.1)` on the trace pipeline, and the App Insights *adaptive sampling* knob (it's on by default for new resources — worth confirming, not assuming). The third is **the alert as written counts an empty window as "no data, no fire"**, which is the right default *except* during a partial outage where the API is up enough to return 500s but no successful POSTs are reaching `summarize` — the alert never fires because the `summarize` row doesn't exist, and I find out from a user that the endpoint has been broken for an hour. The fix is to add a parallel "request rate dropped to zero" alert on the same scope.

---

## Links

- **Repository:** [https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil)
- **Folder:** [Day-4/piece-6](https://github.com/thinkbridge-thinkschool/thinkschool-AvishkarPatil/tree/main/Day-4/piece-6)
