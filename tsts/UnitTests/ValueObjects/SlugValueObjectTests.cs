using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class SlugValueObjectTests
{
    private const string SOURCE = "Maria da Silva";
    private const string BASE_SLUG = "maria-da-silva";

    [Theory]
    [InlineData(SOURCE, BASE_SLUG)]
    [InlineData("MARIA", "maria")]
    [InlineData("João Conceição", "joao-conceicao")]
    [InlineData("  Maria   Silva  ", "maria-silva")]
    [InlineData("Ana-Maria O'Neil", "ana-maria-oneil")]
    [InlineData("Ana - Maria", "ana-maria")]
    [InlineData("- Maria !", "maria")]
    [InlineData("José 2º Filho", "jose-2-filho")]
    public void Given_Source_When_Building_Should_ReturnSlug(string source, string expected)
    {
        // Arrange & Act
        var slug = SlugValueObject.Build(source);

        // Assert
        Assert.True(slug.IsValid);
        Assert.Equal(expected, slug.Value);
    }

    [Theory]
    [InlineData("João da Silva & Filhos (Ltda.)")]
    [InlineData("Ñandú_Çelik @ 100%")]
    [InlineData("  --  Maria  --  ")]
    public void Given_SourceWithSpecialCharacters_When_Building_Should_KeepOnlyLettersNumbersAndHyphens(string source)
    {
        // Arrange & Act
        var slug = SlugValueObject.Build(source);

        // Assert
        Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", slug.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!! ---")]
    public void Given_SourceWithoutLettersOrNumbers_When_Building_Should_ReturnNotGeneratedNotification(string? source)
    {
        // Arrange & Act
        var slug = SlugValueObject.Build(source);

        // Assert
        Assert.False(slug.IsValid);
        var notification = Assert.Single(slug.MethodResult.Notifications);
        Assert.Equal("SLUG_COULD_NOT_BE_GENERATED", notification.Code);
    }

    [Fact]
    public void Given_ValidSlug_When_BuildingRandomVersion_Should_AppendFiveLettersOrNumbers()
    {
        // Arrange
        var slug = SlugValueObject.Build(SOURCE);

        // Act
        var version = slug.BuildRandomVersion();

        // Assert
        Assert.True(version.IsValid);
        Assert.Matches($"^{BASE_SLUG}-[a-z0-9]{{5}}$", version.Value);
    }

    [Fact]
    public void Given_SlugAlreadyVersioned_When_BuildingRandomVersion_Should_VersionFromBaseSlug()
    {
        // Arrange
        var slug = SlugValueObject.Build(SOURCE).BuildRandomVersion();

        // Act
        var version = slug.BuildRandomVersion();

        // Assert
        Assert.Matches($"^{BASE_SLUG}-[a-z0-9]{{5}}$", version.Value);
    }

    [Fact]
    public void Given_ValidSlug_When_BuildingRandomVersion_Should_KeepOriginalSlugUnchanged()
    {
        // Arrange
        var slug = SlugValueObject.Build(SOURCE);

        // Act
        slug.BuildRandomVersion();

        // Assert
        Assert.Equal(BASE_SLUG, slug.Value);
    }

    [Fact]
    public void Given_InvalidSlug_When_BuildingRandomVersion_Should_ThrowInvalidOperationException()
    {
        // Arrange
        var slug = SlugValueObject.Build("!!!");

        // Act
        Action buildRandomVersion = () => slug.BuildRandomVersion();

        // Assert
        Assert.Throws<InvalidOperationException>(buildRandomVersion);
    }
}
