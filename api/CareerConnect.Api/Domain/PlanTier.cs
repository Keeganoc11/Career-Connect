namespace CareerConnect.Api.Domain;

/// <summary>
/// What a user is entitled to. Free is the plain tracker — applications typed
/// in by hand, status changes, interviews on the agenda. Pro is everything the
/// app does on its own: tailoring, scoring, cover letters, Gmail scanning,
/// calendar sync and the interview tracker.
/// </summary>
public enum PlanTier
{
    Free,
    Pro
}
