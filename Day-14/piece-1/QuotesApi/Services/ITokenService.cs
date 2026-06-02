using QuotesApi.Models;

namespace QuotesApi.Services;

public interface ITokenService
{
    string CreateAccessToken(User user);
    string CreateRefreshToken();
    string HashToken(string rawToken);
}
