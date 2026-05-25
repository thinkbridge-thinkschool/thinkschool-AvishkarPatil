using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using QuotesApi.Models;

namespace QuotesApi.Authorization;

public sealed class QuoteOwnerRequirement : IAuthorizationRequirement { }

public sealed class QuoteOwnerHandler : AuthorizationHandler<QuoteOwnerRequirement, Quote>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        QuoteOwnerRequirement requirement,
        Quote resource)
    {
        var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? context.User.FindFirstValue(JwtClaimTypes.Sub);

        if (sub is not null
            && resource.OwnerId.HasValue
            && resource.OwnerId.Value.ToString() == sub)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    // Avoids importing Microsoft.IdentityModel.JsonWebTokens just for the constant.
    private static class JwtClaimTypes
    {
        public const string Sub = "sub";
    }
}
