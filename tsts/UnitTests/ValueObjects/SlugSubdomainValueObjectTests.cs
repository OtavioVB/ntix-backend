using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class SlugSubdomainValueObjectTests
{
    [Theory]
    [InlineData("Ação Eventos & Shows", "acao-eventos-shows")]
    [InlineData("Eventos&Shows", "eventos-shows")]
    [InlineData("O'Neil Produções", "o-neil-producoes")]
    [InlineData("--Festa!!!--", "festa")]
    [InlineData("Conceição 2026", "conceicao-2026")]
    public void Given_Name_When_BuildingSubdomain_Should_FollowLabelRules(string name, string expected)
    {
        // Act
        var slug = SlugValueObject.BuildSubdomain(name);

        // Assert
        Assert.True(slug.IsValid);
        Assert.Equal(expected, slug.Value);
    }

    [Theory]
    [InlineData("www")]
    [InlineData("API")]
    [InlineData("Admin")]
    [InlineData("app")]
    [InlineData("hub")]
    public void Given_ReservedName_When_BuildingSubdomain_Should_ReturnRandomVersion(string name)
    {
        // Act
        var slug = SlugValueObject.BuildSubdomain(name);

        // Assert
        Assert.Matches($"^{name.ToLowerInvariant()}-[a-z0-9]{{{SlugValueObject.RANDOM_VERSION_LENGTH}}}$", slug.Value);
    }

    [Fact]
    public void Given_LongName_When_BuildingSubdomain_Should_RespectLimit()
    {
        // Act
        var slug = SlugValueObject.BuildSubdomain(new string('a', 100));

        // Assert
        Assert.Equal(SlugValueObject.SUBDOMAIN_MAX_LENGTH, slug.Value.Length);
    }

    [Fact]
    public void Given_NameCutAtSeparator_When_BuildingSubdomain_Should_NotEndWithSeparator()
    {
        // Arrange
        var name = $"{new string('a', SlugValueObject.SUBDOMAIN_MAX_LENGTH - 1)} bcd";

        // Act
        var slug = SlugValueObject.BuildSubdomain(name);

        // Assert
        Assert.Equal(new string('a', SlugValueObject.SUBDOMAIN_MAX_LENGTH - 1), slug.Value);
    }

    [Fact]
    public void Given_LongSlug_When_BuildingRandomVersion_Should_RespectLimit()
    {
        // Arrange
        var slug = SlugValueObject.BuildSubdomain(new string('a', 100));

        // Act
        var version = slug.BuildRandomVersion();

        // Assert
        Assert.Equal(SlugValueObject.SUBDOMAIN_MAX_LENGTH, version.Value.Length);
        Assert.Matches($"^a+-[a-z0-9]{{{SlugValueObject.RANDOM_VERSION_LENGTH}}}$", version.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("!!!")]
    public void Given_NameWithoutLettersOrNumbers_When_BuildingSubdomain_Should_ReturnNotification(string? name)
    {
        // Act
        var slug = SlugValueObject.BuildSubdomain(name);

        // Assert
        Assert.False(slug.IsValid);
        Assert.Equal("SLUG_COULD_NOT_BE_GENERATED", Assert.Single(slug.MethodResult.Notifications).Code);
    }
}
