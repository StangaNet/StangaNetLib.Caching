using FluentAssertions;
using StangaNetLib.Caching.Storage.Keys;

namespace StangaNetLib.Caching.Tests.Storage.Keys;

public class CacheKeyBuilderTests
{
    [Fact]
    public void Build_WithSinglePart_ShouldReturnIt()
    {
        CacheKeyBuilder.Build("profile").Should().Be("profile");
    }

    [Fact]
    public void Build_WithMultipleParts_ShouldJoinWithColon()
    {
        CacheKeyBuilder.Build("profile", "user", "123")
            .Should().Be("profile:user:123");
    }

    [Fact]
    public void Build_WithNamespaceAndQualifier_ShouldCombineCorrectly()
    {
        var key = CacheKeyBuilder.Build("discovery", "cursor", "abc", "5", "all");
        key.Should().Be("discovery:cursor:abc:5:all");
    }

    [Fact]
    public void Build_WithNoParts_ShouldThrow()
    {
        var act = () => CacheKeyBuilder.Build();
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_WithNullPartsArray_ShouldThrow()
    {
        var act = () => CacheKeyBuilder.Build(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_WithEmptyStringPart_ShouldThrow()
    {
        var act = () => CacheKeyBuilder.Build("valid", "", "key");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_WithEmptyNamespace_ShouldThrow()
    {
        var act = () => CacheKeyBuilder.Build("", "qualifier");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_WithEmptyQualifier_ShouldThrow()
    {
        var act = () => CacheKeyBuilder.Build("ns", "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildPrefix_ShouldAddTrailingColon()
    {
        CacheKeyBuilder.BuildPrefix("profile", "user")
            .Should().Be("profile:user:");
    }

    [Fact]
    public void BuildPrefix_TrailingColonPreventsCrossNamespaceMatch()
    {
        var prefix = CacheKeyBuilder.BuildPrefix("profile", "user");
        "profile:user:1".StartsWith(prefix).Should().BeTrue();
        "profile:user1".StartsWith(prefix).Should().BeFalse();
    }

    // --- IEnumerable<string> overload ---

    [Fact]
    public void Build_FromEnumerable_ShouldJoinWithColon()
    {
        var parts = new List<string> { "a", "b", "c" };
        CacheKeyBuilder.Build((IEnumerable<string>)parts).Should().Be("a:b:c");
    }

    [Fact]
    public void Build_FromEnumerable_Empty_ShouldThrow()
    {
        var act = () => CacheKeyBuilder.Build(Enumerable.Empty<string>());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_FromEnumerable_Null_ShouldThrow()
    {
        var act = () => CacheKeyBuilder.Build((IEnumerable<string>)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_FromEnumerable_WithEmptyElement_ShouldThrow()
    {
        var act = () => CacheKeyBuilder.Build(new[] { "ok", "", "also-ok" });
        act.Should().Throw<ArgumentException>();
    }
}
