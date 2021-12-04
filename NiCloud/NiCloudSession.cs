using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NiCloud;

public sealed class NiCloudSession
{
    public static readonly JsonSerializerOptions SessionSerialize = new(JsonSerializeOptions.CamelCaseProperties)
    {
        Converters = { new CookieContainerConverter(true) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    private static readonly Uri CookieUri = new("https://icloud.com");

    public string ClientId { get; init; }

    public ConcurrentDictionary<string, string> AuthHeaders { get; init; } = new();

    public string AccountCountry { get; set; }

    public string SessionToken { get; set; }

    public string TrustToken { get; set; }

    [JsonIgnore]
    public string AuthToken { get; set; }

    public bool HasSessionToken => !string.IsNullOrEmpty(TrustToken);

    [JsonConverter(typeof(CookieContainerConverter))]
    public CookieContainer Cookies { get; init; } = new CookieContainer();

    static string CreateClientId() =>
        "auth-" + Guid.NewGuid().ToString()[..^4].ToLowerInvariant();

    public NiCloudSession()
    {
        ClientId = CreateClientId();
    }

    public string Serialize()
{
        return JsonSerializer.Serialize(this, SessionSerialize);
    }

    public static NiCloudSession Deserialize(string json)
    {
        return JsonSerializer.Deserialize<NiCloudSession>(json, SessionSerialize);
    }
}
