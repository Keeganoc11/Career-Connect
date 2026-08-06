namespace CareerConnect.Api.Domain;

/// <summary>
/// One concrete, copy-pasteable resume edit suggested by the analyzer — where
/// it applies, why, and ready-to-paste text (bracketed placeholders stand in
/// for any real detail the model can't truthfully supply on its own).
/// </summary>
public class SuggestedEdit
{
    public required string Section { get; set; }
    public required string Guidance { get; set; }
    public required string SuggestedText { get; set; }
}
