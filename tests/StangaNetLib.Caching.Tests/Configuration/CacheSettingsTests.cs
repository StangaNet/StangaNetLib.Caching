using FluentAssertions;
using StangaNetLib.Caching.Configuration;

namespace StangaNetLib.Caching.Tests.Configuration;

public class CacheSettingsTests
{
    [Fact]
    public void Validate_ValidSettings_ShouldNotThrow()
    {
        var settings = new CacheSettings { Provider = CacheProvider.InMemory };
        var act = () => settings.Validate();
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_RedisMissingConnectionString_ShouldThrow()
    {
        var settings = new CacheSettings { Provider = CacheProvider.Redis };
        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>().WithMessage("*RedisConnectionString*");
    }

    [Fact]
    public void Validate_NonPositiveDefaultExpiry_ShouldThrow()
    {
        var settings = new CacheSettings { DefaultExpiry = TimeSpan.Zero };
        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>().WithMessage("*DefaultExpiry*");
    }

    [Fact]
    public void Validate_KeyPrefixWithColon_ShouldThrow()
    {
        var settings = new CacheSettings { KeyPrefix = "my:app" };
        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>().WithMessage("*':'*");
    }

    [Fact]
    public void Validate_KeyPrefixWithWhitespace_ShouldThrow()
    {
        var settings = new CacheSettings { KeyPrefix = "my app" };
        var act = () => settings.Validate();
        act.Should().Throw<InvalidOperationException>().WithMessage("*whitespace*");
    }

    [Fact]
    public void Validate_ValidRedisSettings_ShouldNotThrow()
    {
        var settings = new CacheSettings
        {
            Provider = CacheProvider.Redis,
            RedisConnectionString = "localhost:6379",
            KeyPrefix = "myapp"
        };
        var act = () => settings.Validate();
        act.Should().NotThrow();
    }
}
