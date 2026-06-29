using Courtly.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Courtly.Tests.Configuration;

public class CourtlyOptionsBindingTests
{
    private static Dictionary<string, string?> FullEnv() => new()
    {
        ["DB_CONNECTION_STRING"] = "Host=postgres;Port=5432;Database=200067;Username=courtly;Password=pw",
        ["JWT_KEY"] = "a-32-plus-character-secret-value-here",
        ["JWT_ISSUER"] = "courtly",
        ["JWT_AUDIENCE"] = "courtly",
        ["JWT_ACCESS_MINUTES"] = "20",
        ["STRIPE_SECRET_KEY"] = "sk_test_x",
        ["STRIPE_PUBLISHABLE_KEY"] = "pk_test_x",
        ["STRIPE_WEBHOOK_SECRET"] = "whsec_x",
        ["SMTP_HOST"] = "sandbox.smtp.mailtrap.io",
        ["SMTP_PORT"] = "2525",
        ["SMTP_USER"] = "smtp-user",
        ["SMTP_PASSWORD"] = "smtp-pass",
        ["SMTP_FROM"] = "no-reply@courtly.local",
        ["RABBITMQ_HOST"] = "rabbitmq",
        ["RABBITMQ_PORT"] = "5672",
        ["RABBITMQ_USER"] = "guest",
        ["RABBITMQ_PASSWORD"] = "guest",
        ["API_BASE_URL"] = "http://localhost:5000",
        ["INTERNAL_PUSH_KEY"] = "internal-push-secret",
    };

    private static ServiceProvider BuildProvider(IDictionary<string, string?> env)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(env).Build();
        var services = new ServiceCollection();
        services.AddCourtlyOptions(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Binds_all_option_groups_from_env_keys()
    {
        using var provider = BuildProvider(FullEnv());

        Assert.Equal(
            "Host=postgres;Port=5432;Database=200067;Username=courtly;Password=pw",
            provider.GetRequiredService<IOptions<DbOptions>>().Value.ConnectionString);

        var jwt = provider.GetRequiredService<IOptions<JwtOptions>>().Value;
        Assert.Equal("a-32-plus-character-secret-value-here", jwt.Key);
        Assert.Equal("courtly", jwt.Issuer);
        Assert.Equal("courtly", jwt.Audience);
        Assert.Equal(20, jwt.AccessMinutes);

        var stripe = provider.GetRequiredService<IOptions<StripeOptions>>().Value;
        Assert.Equal("sk_test_x", stripe.SecretKey);
        Assert.Equal("pk_test_x", stripe.PublishableKey);
        Assert.Equal("whsec_x", stripe.WebhookSecret);

        var smtp = provider.GetRequiredService<IOptions<SmtpOptions>>().Value;
        Assert.Equal("sandbox.smtp.mailtrap.io", smtp.Host);
        Assert.Equal(2525, smtp.Port);
        Assert.Equal("smtp-user", smtp.User);
        Assert.Equal("smtp-pass", smtp.Password);
        Assert.Equal("no-reply@courtly.local", smtp.From);

        var rabbit = provider.GetRequiredService<IOptions<RabbitOptions>>().Value;
        Assert.Equal("rabbitmq", rabbit.Host);
        Assert.Equal(5672, rabbit.Port);
        Assert.Equal("guest", rabbit.User);
        Assert.Equal("guest", rabbit.Password);

        var api = provider.GetRequiredService<IOptions<ApiOptions>>().Value;
        Assert.Equal("http://localhost:5000", api.BaseUrl);
        Assert.Equal("internal-push-secret", api.InternalPushKey);
    }

    [Fact]
    public void Falls_back_to_defaults_for_missing_or_invalid_numeric_keys()
    {
        var env = FullEnv();
        env.Remove("JWT_ACCESS_MINUTES");
        env["SMTP_PORT"] = "not-a-number";
        using var provider = BuildProvider(env);

        Assert.Equal(15, provider.GetRequiredService<IOptions<JwtOptions>>().Value.AccessMinutes);
        Assert.Equal(1025, provider.GetRequiredService<IOptions<SmtpOptions>>().Value.Port);
    }

    [Fact]
    public void Db_validation_fails_when_connection_string_missing()
    {
        var env = FullEnv();
        env.Remove("DB_CONNECTION_STRING");
        using var provider = BuildProvider(env);

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<DbOptions>>().Value);
    }

    [Fact]
    public void Rabbit_validation_fails_when_host_missing()
    {
        var env = FullEnv();
        env.Remove("RABBITMQ_HOST");
        using var provider = BuildProvider(env);

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<RabbitOptions>>().Value);
    }
}
