using System;
using System.Collections.Generic;
using System.Collections;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;

namespace NiCloud;

/// <summary>
/// This class might be deprecated as Microsoft enabled serialization of CookieContainer class.
/// </summary>
public class CookieContainerConverter : JsonConverter<CookieContainer>
{
    private static readonly FieldInfo DomainTableField = typeof(CookieContainer)
        .GetField("m_domainTable", BindingFlags.Instance | BindingFlags.NonPublic);

    readonly bool ignoreSessionCookies;
    private static FieldInfo? PathListField;

    public CookieContainerConverter()
    {
    }

    public CookieContainerConverter(bool ignoreSessionCookies)
    {
        this.ignoreSessionCookies = ignoreSessionCookies;
    }

    public override CookieContainer Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var cookies = JsonSerializer.Deserialize<List<CookiePoco>>(ref reader, options);
        var clonedCookieCollection = new CookieCollection();
        foreach (var cookie in cookies)
        {
            clonedCookieCollection.Add(cookie.ToCookie());
        }

        var container = new CookieContainer();
        container.Add(clonedCookieCollection);
        return container;
    }

    public override void Write(Utf8JsonWriter writer, CookieContainer value, JsonSerializerOptions options)
    {
        var cookies = GetAllCookies(value, ignoreSessionCookies);
        JsonSerializer.Serialize(writer, cookies, options);
    }

    private static List<CookiePoco> GetAllCookies(CookieContainer container, bool ignoreSessionCookies)
    {
        List<CookiePoco> allCookies = new();
        var domainTable = (Hashtable)DomainTableField.GetValue(container);
        var pathLists = new List<SortedList>();
        lock (domainTable.SyncRoot)
        {
            foreach (var domain in domainTable.Values)
            {
                PathListField ??= domain.GetType().GetField("m_list", BindingFlags.Instance | BindingFlags.NonPublic);
                var pathList = (SortedList)PathListField.GetValue(domain);
                pathLists.Add(pathList);
            }
        }

        foreach (var pathList in pathLists)
        {
            lock (pathList.SyncRoot)
            {
                allCookies.AddRange(
                    from CookieCollection cookies in pathList.GetValueList()
                    from Cookie cookie in cookies
                    where !cookie.Expired && (cookie.Expires != default || ignoreSessionCookies)
                    select new CookiePoco(cookie)
                );
            }
        }

        return allCookies;
    }
}

public class CookiePoco
{
    public string Name { get; set; }
    public string Value { get; set; }
    public string Path { get; set; }
    public string Domain { get; set; }
    public string Comment { get; set; }
    public Uri? CommentUri { get; set; }
    public string Port { get; set; }
    public bool HttpOnly { get; set; }
    public bool Discard { get; set; }
    public DateTime Expires { get; set; }
    public int Version { get; set; }

    public CookiePoco()
    {
    }

    public CookiePoco(Cookie cookie)
    {
        Name = cookie.Name;
        Value = cookie.Value;
        Path = cookie.Path == "/" ? null : cookie.Path;
        Domain = cookie.Domain;
        Comment = NullIfEmpty(cookie.Comment);
        CommentUri = cookie.CommentUri;
        Port = NullIfEmpty(cookie.Port);
        HttpOnly = cookie.HttpOnly;
        Discard = cookie.Discard;
        Expires = cookie.Expires;
        Version = cookie.Version;
    }

    private static string? NullIfEmpty(string s)
        => string.IsNullOrEmpty(s) ? null : s;

    public Cookie ToCookie()
    {
        var cookie = new Cookie(Name, Value, Path ?? "/", Domain)
        {
            Expires = Expires,
            CommentUri = CommentUri,
            Discard = Discard,
            HttpOnly = HttpOnly,
        };

        if (!string.IsNullOrEmpty(Comment))
        {
            cookie.Comment = Comment;
        }

        // especially this property needs check because setting it to empty value, sets m_port_implicit to false
        // which in turn causes $Port cookie to be added to every request and fail them
        if (!string.IsNullOrEmpty(Port))
        {
            cookie.Port = Port;
        }

        if (Version != default)
        {
            cookie.Version = Version;
        }

        return cookie;
    }
}