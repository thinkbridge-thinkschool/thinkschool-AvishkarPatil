import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';
import { DefaultAzureCredential } from '@azure/identity';

// ── Managed-Identity broker for the Week-1 QuotesApi ───────────────────────
//
// THIS is where Managed Identity actually happens. A browser SPA can never
// hold an MI token (the token endpoint at 169.254.169.254 only exists inside
// Azure compute). So the browser calls THIS function at /api/quotes; the
// function — running as the SWA's linked backend with a system-assigned
// managed identity — acquires an MI token via DefaultAzureCredential and
// forwards the request to the real Week-1 API with that token attached.
//
// Zero secrets:
//   • No client secret, key, or connection string anywhere.
//   • DefaultAzureCredential uses the platform-injected managed identity at
//     runtime (IDENTITY_ENDPOINT / IDENTITY_HEADER env vars that Azure sets —
//     never committed, never in app settings as a secret).
//   • The only config is the upstream API base URL and its Entra app-ID-URI
//     scope, both NON-secret values.
//
// PREREQUISITES (documented honestly — not yet satisfied in this submission):
//   1. SWA Standard tier (linked backends + system MI require Standard, not Free).
//   2. The Week-1 QuotesApi deployed to Azure (App Service / Container App) and
//      protected by Entra ID, exposing an app role / scope (API_SCOPE below).
//   3. The SWA's managed identity granted that app role on the API's Entra app.
// Until (1)–(3) exist, this broker is correct code with no valid upstream —
// the Week-1 API is currently localhost:5075, which MI cannot target.

const API_BASE  = process.env.WEEK1_API_BASE_URL ?? '';      // e.g. https://quotesapi.<region>.azurecontainerapps.io
const API_SCOPE = process.env.WEEK1_API_SCOPE    ?? '';      // e.g. api://<week1-app-id>/.default

// One credential instance, reused across invocations. In Azure it resolves to
// the function app's system-assigned managed identity.
const credential = new DefaultAzureCredential();

async function forward(req: HttpRequest, context: InvocationContext): Promise<HttpResponseInit> {
  if (!API_BASE || !API_SCOPE) {
    return {
      status: 503,
      jsonBody: {
        error: 'Broker not configured',
        detail: 'WEEK1_API_BASE_URL / WEEK1_API_SCOPE are unset — the Week-1 API is not yet deployed to Azure. MI has no valid upstream.',
      },
    };
  }

  // Acquire a Managed-Identity access token for the Week-1 API's Entra scope.
  // No secret involved — the platform issues this to the function's MI.
  const token = await credential.getToken(API_SCOPE);

  // Rebuild the upstream path (/api/quotes?... → {API_BASE}/api/quotes?...).
  const upstream = new URL(req.url);
  const target = `${API_BASE}${upstream.pathname}${upstream.search}`;

  const init: RequestInit = {
    method: req.method,
    headers: {
      Authorization: `Bearer ${token.token}`,           // ← the managed-identity token
      'Content-Type': 'application/json',
    },
  };
  if (req.method !== 'GET' && req.method !== 'HEAD') {
    init.body = await req.text();
  }

  const res = await fetch(target, init);
  const body = await res.text();
  context.log(`MI-forward ${req.method} ${target} → ${res.status}`);

  return {
    status: res.status,
    headers: { 'Content-Type': res.headers.get('content-type') ?? 'application/json' },
    body,
  };
}

// Route every /api/quotes* call through the broker. The SPA is unchanged — it
// still calls GET /api/quotes?page&size, GET /api/quotes/{id}, POST /api/quotes.
app.http('quotes', {
  route: 'quotes/{*rest}',
  methods: ['GET', 'POST'],
  authLevel: 'anonymous',   // the SPA→broker hop is same-origin; broker→API is MI-secured
  handler: forward,
});
app.http('quotes-root', {
  route: 'quotes',
  methods: ['GET', 'POST'],
  authLevel: 'anonymous',
  handler: forward,
});
