using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Metro.Infrastructure.Persistence.Mapping;

/// <summary>Preserves the SQL text contract (for example, PARTIALLY_REFUNDED).</summary>
internal sealed class UpperSnakeCaseEnumConverter<TEnum> : ValueConverter<TEnum, string>
    where TEnum : struct, Enum
{
    private static readonly IReadOnlyDictionary<TEnum, string> ToDatabase = Enum.GetValues<TEnum>()
        .ToDictionary(value => value, value => JsonNamingPolicy.SnakeCaseUpper.ConvertName(value.ToString()));

    private static readonly IReadOnlyDictionary<string, TEnum> FromDatabase = ToDatabase
        .ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    public UpperSnakeCaseEnumConverter()
        : base(value => ToDatabase[value], value => FromDatabase[value])
    {
    }
}
