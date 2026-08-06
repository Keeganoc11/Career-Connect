using System.Text.Json;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CareerConnect.Api.Data;

public static class SuggestedEditListConversion
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Stores suggestions as a JSON column. Reads tolerate the pre-upgrade shape
    /// (a plain string array) so scores generated before this change still load
    /// — they just show up with empty Section/SuggestedText instead of an error.
    /// </summary>
    public static PropertyBuilder<List<SuggestedEdit>> HasSuggestedEditListConversion(
        this PropertyBuilder<List<SuggestedEdit>> builder)
    {
        var converter = new ValueConverter<List<SuggestedEdit>, string>(
            list => JsonSerializer.Serialize(list, JsonOptions),
            json => Deserialize(json));

        var comparer = new ValueComparer<List<SuggestedEdit>>(
            (left, right) =>
                JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions),
            list => JsonSerializer.Serialize(list, JsonOptions).GetHashCode(),
            list => Deserialize(JsonSerializer.Serialize(list, JsonOptions)));

        return builder.HasConversion(converter, comparer);
    }

    private static List<SuggestedEdit> Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<SuggestedEdit>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            var legacy = JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
            return legacy
                .Select(text => new SuggestedEdit { Section = "General", Guidance = text, SuggestedText = "" })
                .ToList();
        }
    }
}
