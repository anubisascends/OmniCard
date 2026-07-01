using System.Text.Json;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OmniCard.Data;

internal static class PropertyBuilderExtensions
{
    internal static PropertyBuilder<T> HasJsonConversion<T>(this PropertyBuilder<T> builder)
    {
        return builder.HasConversion(
            new ValueConverter<T, string>(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<T>(v, (JsonSerializerOptions?)null)!));
    }
}
