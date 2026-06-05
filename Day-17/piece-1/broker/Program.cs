using Azure.Core;
using Azure.Identity;

// ── Day-17 Managed-Identity broker ─────────────────────────────────────────
// The browser cannot hold a Managed-Identity token (IMDS is only reachable from
// inside Azure compute). This broker runs as its OWN Azure Container App with a
// SYSTEM-ASSIGNED managed identity. The SWA browser calls the broker at /api/*;
// the broker acquires an MI access token for the Week-1 API's Entra app and
// forwards the request with `Authorization: Bearer <MI token>`.
//
// ZERO secrets: DefaultAzureCredential uses the platform-injected managed
// identity at runtime. No client secret, key, or connection string anywhere —
// not in the image, not in app settings, not in the repo.
//
// Config (all NON-secret):
//   ApiBaseUrl        — https://ca-quotes-day17.<region>.azurecontainerapps.io
//   ApiScope          — api://<api-app-id>/.default
//   Cors:AllowedOrigins — the SWA origin

var builder = WebApplication.CreateBuilder(args);

var corsOrigins = (builder.Configuration["Cors:AllowedOrigins"]
        ?? "https://witty-hill-0f9d14b00.7.azurestaticapps.net")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(corsOrigins).AllowAnyHeader()
    .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")));
builder.Services.AddHttpClient();

var app = builder.Build();
app.UseCors();

var apiBase  = builder.Configuration["ApiBaseUrl"]?.TrimEnd('/') ?? "";
var apiScope = builder.Configuration["ApiScope"] ?? "";
var credential = new DefaultAzureCredential();   // resolves to this app's system MI in Azure

app.MapGet("/health", () => Results.Ok(new { status = "broker-ok", apiBase, scopeConfigured = apiScope.Length > 0 }));

// Evidence endpoint: acquire the Managed-Identity token and return its decoded
// payload claims (aud / iss / appid / roles / oid). Read-only base64 decode of
// the JWT payload — NOT validation, just proof of what the MI token contains.
// No secret involved; the token comes from the platform-injected managed identity.
app.MapGet("/whoami", async () =>
{
    AccessToken token = await credential.GetTokenAsync(new TokenRequestContext(new[] { apiScope }));
    var parts = token.Token.Split('.');
    string DecodePart(string p)
    {
        p = p.Replace('-', '+').Replace('_', '/');
        switch (p.Length % 4) { case 2: p += "=="; break; case 3: p += "="; break; }
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(p));
    }
    var header  = System.Text.Json.JsonDocument.Parse(DecodePart(parts[0])).RootElement;
    var payload = System.Text.Json.JsonDocument.Parse(DecodePart(parts[1])).RootElement;
    System.Text.Json.JsonElement? Get(string n) => payload.TryGetProperty(n, out var v) ? v : (System.Text.Json.JsonElement?)null;
    return Results.Json(new
    {
        note   = "Decoded payload of the Managed-Identity access token the broker sends to the API. No secret used to obtain it.",
        alg    = header.TryGetProperty("alg", out var a) ? a.GetString() : null,
        aud    = Get("aud"),
        iss    = Get("iss"),
        appid  = Get("appid"),
        azp    = Get("azp"),
        roles  = Get("roles"),
        oid    = Get("oid"),
        idtyp  = Get("idtyp"),
    });
});

// Forward every /api/* call to the Week-1 API with an MI bearer token.
app.MapMethods("/api/{**rest}", new[] { "GET", "POST", "PUT", "DELETE" },
    async (HttpContext ctx, IHttpClientFactory factory) =>
{
    if (apiBase.Length == 0 || apiScope.Length == 0)
        return Results.Json(new { error = "broker not configured (ApiBaseUrl/ApiScope)" }, statusCode: 503);

    var client = factory.CreateClient();
    var target = $"{apiBase}{ctx.Request.Path}{ctx.Request.QueryString}";
    using var req = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), target);

    var incomingAuth = ctx.Request.Headers.Authorization.FirstOrDefault();
    var isMutation = ctx.Request.Method == "POST" || ctx.Request.Method == "PUT" || ctx.Request.Method == "DELETE";

    if (isMutation && !string.IsNullOrWhiteSpace(incomingAuth))
    {
        req.Headers.TryAddWithoutValidation("Authorization", incomingAuth);
    }
    else
    {
        // Acquire the Managed-Identity access token for the API's Entra scope.
        AccessToken token = await credential.GetTokenAsync(new TokenRequestContext(new[] { apiScope }));
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
    }

    if (ctx.Request.ContentLength is > 0)
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync();
        req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
    }

    using var resp = await client.SendAsync(req);
    var respBody = await resp.Content.ReadAsStringAsync();
    ctx.Response.StatusCode = (int)resp.StatusCode;
    ctx.Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
    await ctx.Response.WriteAsync(respBody);
    return Results.Empty;
});

app.Run();
