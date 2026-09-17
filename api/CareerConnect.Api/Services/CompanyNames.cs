using System.Text.RegularExpressions;

namespace CareerConnect.Api.Services;

/// <summary>
/// Whether two ways of writing a company name mean the same employer. Emails
/// and postings are inconsistent — "Delta Dental of Missouri", "Delta Dental
/// MO" and "Delta Dental" all arrived for one application — so exact matching
/// lets the same job in three times.
/// </summary>
public static partial class CompanyNames
{
    private static readonly HashSet<string> Noise =
        ["the", "of", "and", "inc", "llc", "llp", "ltd", "co", "corp", "corporation", "company"];

    /// <summary>
    /// Same when their first two meaningful words agree — or, when one name is a
    /// single word, that word. "Delta Dental MO" matches "Delta Dental of Missouri";
    /// "Capital One" doesn't match "Capital Group".
    /// </summary>
    public static bool Same(string a, string b)
    {
        var left = Words(a);
        var right = Words(b);
        var n = Math.Min(2, Math.Min(left.Count, right.Count));
        return n > 0 && left.Take(n).SequenceEqual(right.Take(n));
    }

    /// <summary>Same role, ignoring case and punctuation. An unstated role matches anything.</summary>
    public static bool SameRole(string a, string b)
    {
        var left = string.Join(' ', Words(a, dropNoise: false));
        var right = string.Join(' ', Words(b, dropNoise: false));
        return left.Length == 0 || right.Length == 0 || left == right;
    }

    private static List<string> Words(string name, bool dropNoise = true) =>
        NonWord().Split(name.ToLowerInvariant())
            .Where(w => w.Length > 0 && (!dropNoise || !Noise.Contains(w)))
            .ToList();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();
}
