namespace CareerConnect.Api.Domain;

/// <summary>
/// Where a status change originated. Phase 4 (email-based detection) will add
/// automated sources; keeping provenance from day one means historical rows
/// never need backfilling.
/// </summary>
public enum StatusChangeSource
{
    Manual
}
