using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuotesApi.Configuration;

namespace Quotes.Tests.Unit;

/// <summary>
/// These tests confirm that DataAnnotation validation on JwtOptions actually
/// runs when IOptions&lt;JwtOptions&gt; is resolved — the "fail loud at startup"
/// behavior the IOptions pattern is supposed to give us.
/// </summary>
public class JwtOptionsValidationTests
{
    private static IOptions<JwtOptions> Resolve(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services
            .AddOptions<JwtOptions>()
            .Bind(config.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations();

        return services.BuildServiceProvider().GetRequiredService<IOptions<JwtOptions>>();
    }

    [Fact]
    public void Bind_WithMissingKey_ThrowsOnResolve()
    {
        var options = Resolve(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"]   = "QuotesApi",
            ["Jwt:Audience"] = "QuotesApiClients"
        });

        var act = () => _ = options.Value;

        act.Should().Throw<OptionsValidationException>()
           .Which.Failures.Should().Contain(f => f.Contains(nameof(JwtOptions.Key)));
    }

    [Fact]
    public void Bind_WithShortKey_ThrowsOnResolve()
    {
        var options = Resolve(new Dictionary<string, string?>
        {
            ["Jwt:Key"]      = "too-short",
            ["Jwt:Issuer"]   = "QuotesApi",
            ["Jwt:Audience"] = "QuotesApiClients"
        });

        var act = () => _ = options.Value;

        act.Should().Throw<OptionsValidationException>()
           .Which.Failures.Should().Contain(f => f.Contains(nameof(JwtOptions.Key)));
    }

    [Fact]
    public void Bind_WithValidConfig_PopulatesAllFields()
    {
        var options = Resolve(new Dictionary<string, string?>
        {
            ["Jwt:Key"]                         = "unit-test-key-32bytes-minimum-xx!!",
            ["Jwt:Issuer"]                      = "QuotesApi",
            ["Jwt:Audience"]                    = "QuotesApiClients",
            ["Jwt:AccessTokenExpiresInMinutes"] = "20",
            ["Jwt:RefreshTokenExpiresInDays"]   = "14"
        });

        var jwt = options.Value;

        jwt.Issuer.Should().Be("QuotesApi");
        jwt.AccessTokenLifetime.Should().Be(TimeSpan.FromMinutes(20));
        jwt.RefreshTokenLifetime.Should().Be(TimeSpan.FromDays(14));
    }
}
