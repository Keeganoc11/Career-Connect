using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CareerConnect.Api.Data;

public static class StringListConversion
{
    /// <summary>
    /// Stores a string list as a JSON column. The comparer is required — without
    /// it EF compares list references, so in-place edits go undetected and never
    /// persist.
    /// </summary>
    public static PropertyBuilder<List<string>> HasStringListConversion(
        this PropertyBuilder<List<string>> builder)
    {
        var converter = new ValueConverter<List<string>, string>(
            list => JsonSerializer.Serialize(list, (JsonSerializerOptions?)null),
            json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>());

        var comparer = new ValueComparer<List<string>>(
            (left, right) => (left ?? new List<string>()).SequenceEqual(right ?? new List<string>()),
            list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
            list => list.ToList());

        return builder.HasConversion(converter, comparer);
    }
}
