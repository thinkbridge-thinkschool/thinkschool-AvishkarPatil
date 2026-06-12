using System.Text.Json;

namespace QuotesApi.Services;

/// <summary>
/// Fetches the OIDC discovery document from Entra ID.
/// The API uses Entra ID metadata to validate inbound tokens — every call to
/// <c>login.microsoftonline.com</c> is a real outbound HTTP dependency, so it
/// is the call we wrap in Polly.
/// </summary>
public interface IEntraIdMetadataClient
{
    Task<JsonElement> GetDiscoveryDocumentAsync(CancellationToken ct = default);
}
