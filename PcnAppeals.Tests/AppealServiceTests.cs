using Moq;
using PcnAppeals.Core;
using Xunit;

namespace PcnAppeals.Tests;

// STARTER FILE – write your tests here. Model answers are in ModelAnswers.cs.txt
// (rename to .cs only if you want to run them – or keep them hidden until you've finished!)

public class AppealServiceTests
{
    private static readonly Pcn ExistingPcn = new("PCN123", "AB12 CDE", new DateOnly(2026, 11, 1), 70m);

    private readonly Mock<IPcnRepository> _repository = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly AppealService _sut;   // sut = "system under test"

    public AppealServiceTests()
    {
        _repository
            .Setup(r => r.FindAsync("PCN123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingPcn);

        _sut = new AppealService(_repository.Object, _notifications.Object);
    }

    // Helper: a valid request, with the option to change one thing per test.
    private static AppealRequest ValidRequest(
        string reference = "PCN123",
        string vrm = "AB12 CDE",
        string reason = "The signs were obscured",
        DateOnly? submittedOn = null) =>
        new(reference, vrm, reason, submittedOn ?? new DateOnly(2026, 11, 10));

    private static AppealRequest NoVrmRequest(
        string reference = "PCN123",
        string reason = "The signs were obscured",
        DateOnly? submittedOn = null) =>
        new(reference, null, reason, submittedOn ?? new DateOnly(2026, 11, 10));

    [Theory]
    [InlineData(0, AppealStatus.Received)]
    [InlineData(28, AppealStatus.Received)]
    [InlineData(29, AppealStatus.Late)]
    public async Task Submit_AroundDeadline_ReturnsExpectedStatus(int daysAfterIssue, AppealStatus  expectedStatus)
    {
        // Arrange
        var request = ValidRequest(submittedOn: ExistingPcn.IssuedOn.AddDays(daysAfterIssue));

        // Act
        var result = await _sut.SubmitAsync(request);

        // Assert
        Assert.Equal(expectedStatus, result.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Submit_BlankReason_ReturnsRejected(string? reason)
    {
        // Arrange
        var request = ValidRequest(reason: reason!);

        // Act
        var result = await _sut.SubmitAsync(request);

        // Assert
        Assert.Equal(AppealStatus.Rejected, result.Status);
        _repository.Verify(r => r.FindAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

    }

    [Fact]
    public async Task Submit_UnknownPCN_ReturnsRejected()
    {
        // Arrange
        _repository
            .Setup(r => r.FindAsync("NOPE", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pcn?)null);
        var request = ValidRequest(reference: "NOPE");

        // Act
        var result = await _sut.SubmitAsync(request);

        // Assert
        Assert.Equal(AppealStatus.Rejected, result.Status);
        _notifications.Verify(n => n.SendAcknowledgementAsync(
         It.IsAny<string>(), It.IsAny<AppealStatus>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("ab12cde")]
    [InlineData("ab12 cde")]
    [InlineData("AB12 CDE")]
    public async Task Submit_RegistrationWithDifferentSpacingOrCase_StillMatches(string vrm)
    {
        // Arrange
        var request = ValidRequest(vrm: vrm);

        // Act
        var result = await _sut.SubmitAsync(request);

        // Assert
        Assert.Equal(AppealStatus.Received, result.Status);
    }

    [Fact]
    public async Task Submit_WrongRegistration_ReturnsRejectedAndSendsNoNotification()
    {
        // Arrange
        var request = ValidRequest(vrm: "123ABC");

        // Act
        var result = await _sut.SubmitAsync(request);

        // Assert
        Assert.Equal(AppealStatus.Rejected, result.Status);
        _notifications.Verify(n => n.SendAcknowledgementAsync(
        It.IsAny<string>(), It.IsAny<AppealStatus>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Submit_LateAcknowledgement_ReturnsStatusLate()
    {
        // Arrange
        var request = ValidRequest(submittedOn: ExistingPcn.IssuedOn.AddDays(29));

        // Act
        var result = await _sut.SubmitAsync(request);

        // Assert
        Assert.Equal(AppealStatus.Late, result.Status);
        _notifications.Verify(n => n.SendAcknowledgementAsync(
        "PCN123", AppealStatus.Late, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Submit_Null_ReturnFail()
    {
        // Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.SubmitAsync(null!));
    }

    [Fact]
    public async Task Submit_NoRegistration_ReturnsRejected()
    {
        // Arrange
        var request = ValidRequest(vrm: null!);

        // Act
        var result = await _sut.SubmitAsync(request);

        // Assert
        Assert.Equal(AppealStatus.Rejected, result.Status);
        _notifications.Verify(n => n.SendAcknowledgementAsync(
         It.IsAny<string>(), It.IsAny<AppealStatus>(), It.IsAny<CancellationToken>()), Times.Never);
    }

}
