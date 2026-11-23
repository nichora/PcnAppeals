namespace PcnAppeals.Core;

// The code under test. Read it first, then write tests for its BEHAVIOUR.
// Note: the 28-day window is an example rule for practice, not legal advice.

public enum AppealStatus { Received, Late, Rejected }

public record Pcn(string Reference, string Vrm, DateOnly IssuedOn, decimal Amount);

public record AppealRequest(string PcnReference, string Vrm, string Reason, DateOnly SubmittedOn);

public record AppealResult(AppealStatus Status, string Message);

public interface IPcnRepository
{
    Task<Pcn?> FindAsync(string reference, CancellationToken ct = default);
}

public interface INotificationService
{
    Task SendAcknowledgementAsync(string pcnReference, AppealStatus status, CancellationToken ct = default);
}

public class AppealService
{
    public const int AppealWindowDays = 28;

    private readonly IPcnRepository _repository;
    private readonly INotificationService _notifications;

    public AppealService(IPcnRepository repository, INotificationService notifications)
    {
        _repository = repository;
        _notifications = notifications;
    }

    public async Task<AppealResult> SubmitAsync(AppealRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Reason))
            return new AppealResult(AppealStatus.Rejected, "A reason is required.");

        var pcn = await _repository.FindAsync(request.PcnReference, ct);
        if (pcn is null)
            return new AppealResult(AppealStatus.Rejected, "PCN not found.");

        if(string.IsNullOrWhiteSpace(request.Vrm))
            return new AppealResult(AppealStatus.Rejected, "Vehicle registration not provided");

        if (Normalise(pcn.Vrm) != Normalise(request.Vrm))
            return new AppealResult(AppealStatus.Rejected, "Vehicle registration does not match.");

        var deadline = pcn.IssuedOn.AddDays(AppealWindowDays);
        var status = request.SubmittedOn > deadline ? AppealStatus.Late : AppealStatus.Received;

        await _notifications.SendAcknowledgementAsync(pcn.Reference, status, ct);

        return new AppealResult(status,
            status == AppealStatus.Late ? "Received after the deadline." : "Appeal received.");
    }

    private static string Normalise(string vrm) => vrm.Replace(" ", "").ToUpperInvariant();
}
