using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CareerConnect.Api.Data;

public static class JsonConversion
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Stores a document-shaped value (a resume layout, a review) as one JSON
    /// column. These are always read and written whole — nothing queries inside
    /// them — so a table per nested record would be ceremony with no payoff.
    /// </summary>
    public static PropertyBuilder<T?> HasJsonConversion<T>(this PropertyBuilder<T?> builder) where T : class
    {
        var converter = new ValueConverter<T?, string?>(
            value => value == null ? null : JsonSerializer.Serialize(value, JsonOptions),
            json => json == null ? null : JsonSerializer.Deserialize<T>(json, JsonOptions));

        var comparer = new ValueComparer<T?>(
            (left, right) => Serialize(left) == Serialize(right),
            value => (Serialize(value) ?? "").GetHashCode(),
            value => value == null ? null : JsonSerializer.Deserialize<T>(Serialize(value)!, JsonOptions));

        return builder.HasConversion(converter, comparer);
    }

    public static PropertyBuilder<List<T>> HasJsonListConversion<T>(this PropertyBuilder<List<T>> builder)
    {
        var converter = new ValueConverter<List<T>, string>(
            list => JsonSerializer.Serialize(list, JsonOptions),
            json => JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>());

        var comparer = new ValueComparer<List<T>>(
            (left, right) => JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions),
            list => JsonSerializer.Serialize(list, JsonOptions).GetHashCode(),
            list => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(list, JsonOptions), JsonOptions)!);

        return builder.HasConversion(converter, comparer);
    }

    private static string? Serialize<T>(T? value) where T : class =>
        value == null ? null : JsonSerializer.Serialize(value, JsonOptions);
}
