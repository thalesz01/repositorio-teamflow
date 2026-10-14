using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TeamFlow.Api.Data;

// Stores an enum as its name with a lowercase first letter ("completed", "cli"). That is the text these columns
// already hold and the JSON wire name, so typing a column needs no data migration.
public sealed class LowercaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(value => ToText(value), text => FromText(text))
    where TEnum : struct, Enum
{
    private static string ToText(TEnum value)
    {
        var name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static TEnum FromText(string text) => Enum.Parse<TEnum>(text, ignoreCase: true);
}
