using System.Text.Json;
using System.Text.Json.Serialization;

namespace NiCloud;

public static class JsonSerializeOptions
{
    public static readonly JsonSerializerOptions CamelCaseProperties = new()
    {
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false)
        },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IgnoreReadOnlyFields = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
