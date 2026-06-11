using System.Text.Json;

namespace QuotesApi.Services;

public sealed class EntraIdMetadataClient : IEntraIdMetadataClient
{
    private readonly HttpClient _http;

    public EntraIdMetadataClient(HttpClient http) => _http = http;

    public async Task<JsonElement> GetDiscoveryDocumentAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(".well-known/openid-configuration", ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }
}
