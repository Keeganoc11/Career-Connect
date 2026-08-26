using System.Text.Json;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CareerConnect.Api.Data;

public static class PrepStepListConversion
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Stores the step log as a JSON column — written and read whole, never queried by element.</summary>
    public static PropertyBuilder<List<PrepStep>> HasPrepStepListConversion(
        this PropertyBuilder<List<PrepStep>> builder)
    {
        var converter = new ValueConverter<List<PrepStep>, string>(
            list => JsonSerializer.Serialize(list, JsonOptions),
            json => JsonSerializer.Deserialize<List<PrepStep>>(json, JsonOptions) ?? new List<PrepStep>());

        var comparer = new ValueComparer<List<PrepStep>>(
            (left, right) =>
                JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions),
            list => JsonSerializer.Serialize(list, JsonOptions).GetHashCode(),
            list => JsonSerializer.Deserialize<List<PrepStep>>(
                JsonSerializer.Serialize(list, JsonOptions), JsonOptions) ?? new List<PrepStep>());

        return builder.HasConversion(converter, comparer);
    }
}
