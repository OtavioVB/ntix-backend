using Domain.ValueObjects;
using System.Text.RegularExpressions;

namespace UnitTests.ValueObjects;

public sealed class SlugValueObjectSubdomainTests
{
    [Fact]
    public void Given_ProducerName_When_BuildingSubdomainSlug_Should_FollowTheRequirementExample()
    {
        // Arrange & Act
        var slug = SlugValueObject.Build("Ação Eventos & Shows", SlugValueObject.SUBDOMAIN_MAX_LENGTH);

        // Assert
        Assert.Equal("acao-eventos-shows", slug.Value);
    }

    [Fact]
    public void Given_NameLongerThanSubdomain_When_Building_Should_TruncateTo63CharactersWithoutTrailingHyphen()
    {
        // Arrange
        var source = new string('a', 62) + " bcd";

        // Act
        var slug = SlugValueObject.Build(source, SlugValueObject.SUBDOMAIN_MAX_LENGTH);

        // Assert
        Assert.Equal(new string('a', 62), slug.Value);
    }

    [Fact]
    public void Given_SlugAtSubdomainLimit_When_BuildingRandomVersion_Should_KeepWithinLimit()
    {
        // Arrange
        var slug = SlugValueObject.Build(new string('a', 100), SlugValueObject.SUBDOMAIN_MAX_LENGTH);

        // Act
        var version = slug.BuildRandomVersion();

        // Assert
        Assert.Equal(SlugValueObject.SUBDOMAIN_MAX_LENGTH, version.Value.Length);
        Assert.Matches(new Regex($"^a{{57}}-[a-z0-9]{{5}}$"), version.Value);
    }

    [Fact]
    public void Given_SlugWithoutLimit_When_Building_Should_NotTruncate()
    {
        // Arrange & Act
        var slug = SlugValueObject.Build(new string('a', 100));

        // Assert
        Assert.Equal(100, slug.Value.Length);
    }

    [Theory]
    [InlineData("www")]
    [InlineData("API")]
    [InlineData("Admin")]
    [InlineData("app")]
    [InlineData("hub")]
    [InlineData("produtores")]
    public void Given_ReservedWord_When_Building_Should_BeReservedSubdomain(string source)
    {
        // Arrange & Act
        var slug = SlugValueObject.Build(source, SlugValueObject.SUBDOMAIN_MAX_LENGTH);

        // Assert
        Assert.True(slug.IsReservedSubdomain);
        Assert.False(slug.BuildRandomVersion().IsReservedSubdomain);
    }

    [Theory]
    [InlineData("hub eventos")]
    [InlineData("apis")]
    public void Given_NameContainingReservedWord_When_Building_Should_NotBeReservedSubdomain(string source)
    {
        // Arrange & Act
        var slug = SlugValueObject.Build(source, SlugValueObject.SUBDOMAIN_MAX_LENGTH);

        // Assert
        Assert.False(slug.IsReservedSubdomain);
    }
}
