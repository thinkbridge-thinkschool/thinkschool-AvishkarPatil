# Day 5 · Piece 5 — Verify in App Insights with KQL

[Piece 4](../piece-4/README.md) put the app on Azure. [Piece 1](../piece-1/) wired OpenTelemetry into the app so it emits spans, logs, and metrics. This piece closes the loop between those two: prove the telemetry actually lands in Application Insights, then write the first KQL query that turns "raw requests" into "p50/p99 by route."

The submission deliverable lives in [output.md](output.md) (the screenshots + observation + Q1/Q2). This README is the longer write-up: why no telemetry was reaching Azure when I started, the one-env-var fix, what I sent through to generate signal, and what saving the query as a function actually does under the hood.

---

## What was actually broken at the start

After [piece 4](../piece-4/README.md) the Container App was happily serving traffic at `https://ca-api-oug3e3rejocbe.purplecoast-dcd0caac.southeastasia.azurecontainerapps.io/`, and OpenTelemetry was registered in code with `UseAzureMonitor` from [piece 6 of Day 4](../../Day-4/piece-6/). But running the exercise KQL against the App Insights resource returned **zero rows**. Not an error — just an empty table.

The cause was one missing env var. The deploy in piece 4 declared `AppInsights__ConnectionString` on the container as a *named* env var, but with an empty value:

```bash
$ az containerapp show --resource-group thinkschool-rg --name ca-api-oug3e3rejocbe \
    --query "properties.template.containers[0].env[?name=='AppInsights__ConnectionString'].value" -o tsv
# (empty line)
```

And the registration code in [Extensions/InfrastructureExtensions.cs:113-126](Extensions/InfrastructureExtensions.cs#L113-L126) uses that value to decide which exporter to wire up:

```csharp
var appInsightsConnection = configuration["AppInsights:ConnectionString"];
var useAzureMonitor = !string.IsNullOrWhiteSpace(appInsightsConnection);

var otel = services.AddOpenTelemetry()...;
if (useAzureMonitor)
{
    otel.UseAzureMonitor(o => o.ConnectionString = appInsightsConnection);
    otel.WithTracing(...);
}
else
{
    otel.WithTracing(t => t.AddOtlpExporter());   // ← local-dev path
}
```

Empty string → `useAzureMonitor` is false → OTel takes the local-dev branch and tries to ship spans over OTLP/gRPC to `localhost:4317`. There's no collector listening inside the Container App, so the exporter retries quietly in the background. **No errors in logs, no data in Azure — the worst kind of failure mode.** This is the single most important thing I learned this session and it's why the Q1 answer leads with "telemetry isn't a side effect of your code, it's an explicit pipe."

## The fix — one `az` command

```pwsh
az containerapp update `
  --resource-group thinkschool-rg `
  --name ca-api-oug3e3rejocbe `
  --set-env-vars "AppInsights__ConnectionString=InstrumentationKey=1cee68c2-1517-4d2a-a140-5db083f61422;IngestionEndpoint=https://southeastasia-1.in.applicationinsights.azure.com/;LiveEndpoint=https://southeastasia.livediagnostics.monitor.azure.com/;ApplicationId=9b1968b7-40f0-4a90-a5db-b83f20deec5b"
```

A few things worth noting about that command:

- **Env var, not Bicep.** I patched the running app instead of editing `infra/resources.bicep` and re-running `azd up`. That's deliberate — the connection string belongs to `appinsights-avishkar` in `rg-avishkar`, a separate resource group from the app's `thinkschool-rg`. Wiring it into Bicep would have meant either passing it in as a parameter (and storing it in `main.parameters.json`, which lands in git) or doing a cross-RG `existing` lookup. The env var override is the right scope for "I'm verifying telemetry today"; the Bicep fix is a follow-up if I want this to survive a fresh `azd up`.
- **The whole string in one argument.** `--set-env-vars` takes `NAME=VALUE` pairs space-separated. The connection string itself contains `=` and `;` characters — those parse fine inside a single quoted argument; pwsh doesn't split on `;` inside double quotes.
- **A new revision is created automatically.** Container Apps treats env var changes as part of the revision template, so this command produces `ca-api-oug3e3rejocbe--0000001` (vs the original `--azd-1779723217`). Both revisions stay active until the old one drains.
- **Two App Insights resources exist.** `azd up` from piece 4 auto-created `quotes-api-insights` in `thinkschool-rg`, but the connection string above points at `appinsights-avishkar` in `rg-avishkar` (the one I provisioned by hand in [Day 4 piece 6](../../Day-4/piece-6/)). That's why `quotes-api-insights` shows zero data and the exercise screenshots come from `appinsights-avishkar`. A real cleanup would delete the auto-created one or move `appinsights-avishkar` into `thinkschool-rg`.

After the update I confirmed the env var:

```pwsh
az containerapp show --resource-group thinkschool-rg --name ca-api-oug3e3rejocbe `
  --query "properties.template.containers[0].env[?name=='AppInsights__ConnectionString'].value" -o tsv
# → InstrumentationKey=1cee68c2-...;IngestionEndpoint=...;ApplicationId=9b1968b7-...
```

## Generating signal

App Insights only has data if requests actually arrive, and the exercise's `ago(30m)` window means stale data ages out. I shot a mix of traffic at the warmed-up revision:

- Login successes (`POST /api/auth/login` with `demo@example.com` / `P@ssw0rd!`) — 8 calls.
- Login failures (wrong password → 401) — 5 calls.
- Unauthenticated `GET /api/quotes` (→ 401) — 6 calls.
- Authenticated `GET /api/quotes?page=1&size=5` — 12 calls.
- `GET /api/quotes/{id}` for ids 1, 2, 3, 999, 1000 — 2 each → 10 calls (mostly 404s because only id=1 is seeded).
- A few `POST /api/quotes` (create) — 3 calls.
- A pile of `GET /health` — 20 calls (manual; the platform probe added ~70 more on its own).

Then waited about 2 minutes for App Insights to index. The ingestion latency is real — running the KQL immediately after the last request returns an empty table. The docs say "near real-time," which means roughly "within 60-180 seconds end-to-end" in southeastasia.

## The query

```kusto
requests
| where timestamp > ago(30m)
| summarize count(), p50=percentile(duration, 50), p99=percentile(duration, 99) by name
| order by p99 desc
```

The `requests` table is one of the built-in App Insights schemas — every incoming HTTP request lands there with `name` (the route template), `duration` (in ms), `resultCode`, `success`, plus a customer-id / operation-id chain that ties it back to the originating trace. `summarize ... by name` collapses all requests for the same route into one row. `percentile()` is exact in Kusto, not the approximate variant most metric systems give you — for n in the hundreds that's plenty fast.

`ago(30m)` is the silent failure mode worth flagging: if the app's been idle, the query returns zero rows without an error. The Logs blade's empty-state UI is easy to miss.

The result (full table + screenshot in [output.md](output.md#2-result--table-view)):

| name                       | count | p50 (ms) | p99 (ms) |
|----------------------------|------:|---------:|---------:|
| POST /api/auth/login       |    13 |    612.3 |    779.1 |
| POST /api/quotes/          |     3 |    124.1 |    174.9 |
| GET  /api/quotes/          |    18 |      2.4 |    163.4 |
| GET  /health               |    89 |      0.3 |     81.6 |
| GET  /api/quotes/{id:int}  |    10 |      1.7 |     30.1 |

## Saving the query as a function

The exercise says "save this as a function." In App Insights / Log Analytics, a saved function is actually a `savedSearch` resource on the underlying Log Analytics workspace, with `category: Functions` and a `functionAlias` set. Once that's done, the alias becomes callable from any KQL query in that workspace as if it were a built-in table.

I created it via the CLI rather than the portal so the command is reproducible:

```pwsh
az monitor log-analytics workspace saved-search create `
  --resource-group rg-avishkar `
  --workspace-name law-avishkar `
  -n requestsByRouteP99 `
  --category Functions `
  --display-name "Requests by route — count/p50/p99" `
  --saved-query "requests | where timestamp > ago(30m) | summarize count(), p50=percentile(duration, 50), p99=percentile(duration, 99) by name | order by p99 desc" `
  --fa requestsByRouteP99
```

Two flags worth knowing: `--category Functions` (with a capital F — `Function` doesn't work) is what makes it show up under the **Functions** tab in the workspace's Logs blade, and `--fa` (function alias) is the *short* name you'll actually type at the KQL prompt. Without `--fa` you get a saved query that you can open from the UI but can't call by name from another query.

After saving, the entire exercise query collapses to:

```kusto
requestsByRouteP99
```

That's the durable artifact this piece produced — not the screenshot, the function. Anyone in the workspace can re-run the same view in one word, and a follow-up piece can chain it (`requestsByRouteP99 | where p99 > 500`) without re-typing the percentile logic.

## File layout

```
piece-5/
├── README.md          ← this file
├── output.md          ← exercise submission (screenshots + observation + Q1/Q2)
├── image-6.png        ← App Insights Logs blade, Results tab (table view)
└── image-7.png        ← App Insights Logs blade, Chart tab (stacked bar)
```

Everything else in this folder (`Program.cs`, `infra/`, `azure.yaml`, etc.) is unchanged from [piece 4](../piece-4/). This piece adds no code — only Azure config and a saved KQL function on the workspace.

## What's next

The two follow-ups I'd want to do before considering observability "done":

1. **Wire the connection string into Bicep** so a fresh `azd up` on a new student account produces a container with telemetry already flowing. Right now if I `azd down` and `azd up` again, `AppInsights__ConnectionString` will be empty again and I'll repeat the silent-failure debug session.
2. **Add a "failed requests" pane** — `requests | where success == false | summarize count() by name, resultCode`. The current query only asks "how fast" — not "is anything broken." That's a bigger blind spot than the latency one.
