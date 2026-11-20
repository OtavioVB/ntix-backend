using Domain.BoundedContexts.EventContext.Enums;
using Domain.ValueObjects;

namespace UnitTests.ValueObjects;

public sealed class EventValueObjectsTests
{
    private static string SingleCode(Domain.CrossCutting.MethodResultContext.MethodResult result)
        => Assert.Single(result.Notifications).Code;

    [Theory]
    [InlineData("Show")]
    [InlineData("Festival   Horizonte")]
    public void Given_TitleWithinLimits_When_Building_Should_BeValidAndNormalized(string title)
    {
        // Arrange & Act
        var valueObject = EventTitleValueObject.Build(title);

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.DoesNotContain("  ", valueObject.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AB")]
    [InlineData("   ")]
    public void Given_TitleTooShort_When_Building_Should_BeInvalid(string? title)
    {
        // Arrange & Act
        var valueObject = EventTitleValueObject.Build(title);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Equal("EVENT_TITLE_INVALID", SingleCode(valueObject.MethodResult));
    }

    [Fact]
    public void Given_TitleAboveMaxLength_When_Building_Should_BeInvalid()
    {
        // Arrange & Act
        var valueObject = EventTitleValueObject.Build(new string('a', EventTitleValueObject.MAX_LENGTH + 1));

        // Assert
        Assert.False(valueObject.IsValid);
    }

    [Fact]
    public void Given_EmptyDescription_When_Building_Should_BeValidAndAbsent()
    {
        // Arrange & Act
        var valueObject = EventDescriptionValueObject.Build("   ");

        // Assert
        Assert.True(valueObject.IsValid);
        Assert.Null(valueObject.Value);
    }

    [Fact]
    public void Given_DescriptionAboveMaxLength_When_Building_Should_BeInvalid()
    {
        // Arrange & Act
        var valueObject = EventDescriptionValueObject.Build(new string('a', EventDescriptionValueObject.MAX_LENGTH + 1));

        // Assert
        Assert.Equal("EVENT_DESCRIPTION_INVALID", SingleCode(valueObject.MethodResult));
    }

    [Theory]
    [InlineData("FESTIVALS", EventCategory.FESTIVALS)]
    [InlineData("STAND_UP", EventCategory.STAND_UP)]
    public void Given_KnownCategory_When_Building_Should_BeValid(string category, EventCategory expected)
    {
        // Arrange & Act
        var valueObject = EventCategoryValueObject.Build(category);

        // Assert
        Assert.Equal(expected, valueObject.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("RODEIO")]
    [InlineData("festivals")]
    public void Given_UnknownCategory_When_Building_Should_BeInvalid(string? category)
    {
        // Arrange & Act
        var valueObject = EventCategoryValueObject.Build(category);

        // Assert
        Assert.Equal("EVENT_CATEGORY_INVALID", SingleCode(valueObject.MethodResult));
    }

    [Theory]
    [InlineData("FREE")]
    [InlineData("AGE_18")]
    public void Given_KnownAgeRating_When_Building_Should_BeValid(string ageRating)
        => Assert.True(AgeRatingValueObject.Build(ageRating).IsValid);

    [Fact]
    public void Given_UnknownAgeRating_When_Building_Should_BeInvalid()
        => Assert.Equal("EVENT_AGE_RATING_INVALID", SingleCode(AgeRatingValueObject.Build("AGE_21").MethodResult));

    [Theory]
    [InlineData("pe", "PE")]
    [InlineData(" SP ", "SP")]
    public void Given_ValidState_When_Building_Should_NormalizeToUpperCase(string state, string expected)
        => Assert.Equal(expected, BrazilianStateValueObject.Build(state).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("XX")]
    [InlineData("Pernambuco")]
    public void Given_InvalidState_When_Building_Should_BeInvalid(string? state)
        => Assert.Equal("EVENT_STATE_INVALID", SingleCode(BrazilianStateValueObject.Build(state).MethodResult));

    [Fact]
    public void Given_CompleteVenue_When_Building_Should_BeValid()
    {
        // Arrange & Act
        var venue = VenueValueObject.Build("Parque das Dunas", "Av. Beira-Mar, 1200", "Recife", "pe");

        // Assert
        Assert.True(venue.IsValid);
        Assert.Equal("PE", venue.State);
    }

    [Fact]
    public void Given_VenueWithEveryFieldInvalid_When_Building_Should_ReportOneNotificationPerField()
    {
        // Arrange & Act
        var venue = VenueValueObject.Build(null, "", new string('a', VenueValueObject.CITY_MAX_LENGTH + 1), "XX");

        // Assert
        Assert.False(venue.IsValid);
        Assert.Equal(
            ["EVENT_VENUE_NAME_INVALID", "EVENT_VENUE_ADDRESS_INVALID", "EVENT_VENUE_CITY_INVALID", "EVENT_STATE_INVALID"],
            venue.MethodResult.Notifications.Select(notification => notification.Code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Given_NoImage_When_Building_Should_BeValidAndAbsent(string? url)
    {
        // Arrange & Act
        var image = EventImageUrlValueObject.Build(url);

        // Assert
        Assert.True(image.IsValid);
        Assert.Null(image.Value);
    }

    [Theory]
    [InlineData("http://cdn.exemplo.com/a.jpg")]
    [InlineData("/imagens/a.jpg")]
    [InlineData("javascript:alert(1)")]
    public void Given_ImageThatIsNotHttps_When_Building_Should_BeInvalid(string url)
        => Assert.Equal("EVENT_IMAGE_URL_INVALID", SingleCode(EventImageUrlValueObject.Build(url).MethodResult));

    [Fact]
    public void Given_ValidPeriod_When_Building_Should_KeepDatesInUtc()
    {
        // Arrange & Act
        var period = EventPeriodValueObject.Build("2027-03-20T15:00:00-03:00", "2027-03-21T03:00:00.000Z", "2027-03-20T17:00:00.000Z");

        // Assert
        Assert.True(period.IsValid);
        Assert.Equal(new DateTime(2027, 3, 20, 18, 0, 0, DateTimeKind.Utc), period.StartsAt);
        Assert.Equal(DateTimeKind.Utc, period.EndsAt.Kind);
    }

    [Theory]
    [InlineData(null, "2027-03-21T03:00:00Z")]
    [InlineData("2027-03-20T18:00:00Z", null)]
    [InlineData("amanhã", "2027-03-21T03:00:00Z")]
    [InlineData("2027-03-21T03:00:00Z", "2027-03-20T18:00:00Z")]
    [InlineData("2027-03-20T18:00:00Z", "2027-03-20T18:00:00Z")]
    [InlineData("2027-03-01T18:00:00Z", "2027-04-01T18:00:00Z")]
    public void Given_InvalidPeriod_When_Building_Should_ReportPeriodInvalid(string? startsAt, string? endsAt)
        => Assert.Equal("EVENT_PERIOD_INVALID", SingleCode(EventPeriodValueObject.Build(startsAt, endsAt, null).MethodResult));

    [Theory]
    [InlineData("2027-03-20T18:30:00Z")]
    [InlineData("2027-03-20T05:00:00Z")]
    [InlineData("ontem")]
    public void Given_DoorsOpeningOutOfRange_When_Building_Should_ReportDoorsOpenInvalid(string doorsOpenAt)
        => Assert.Equal("EVENT_DOORS_OPEN_INVALID", SingleCode(EventPeriodValueObject.Build("2027-03-20T18:00:00Z", "2027-03-21T03:00:00Z", doorsOpenAt).MethodResult));

    [Fact]
    public void Given_DoorsOpeningTwelveHoursBefore_When_Building_Should_BeValid()
        => Assert.True(EventPeriodValueObject.Build("2027-03-20T18:00:00Z", "2027-03-21T03:00:00Z", "2027-03-20T06:00:00Z").IsValid);

    [Theory]
    [InlineData("DRAFT", EventStatus.DRAFT)]
    [InlineData("PUBLISHED", EventStatus.PUBLISHED)]
    public void Given_KnownStatus_When_Building_Should_BeValid(string status, EventStatus expected)
        => Assert.Equal(expected, EventStatusValueObject.Build(status).Value);

    [Fact]
    public void Given_UnknownStatus_When_Building_Should_BeInvalid()
        => Assert.Equal("EVENT_STATUS_INVALID", SingleCode(EventStatusValueObject.Build("ENDED").MethodResult));
}
