using System;
using System.Globalization;
using System.Net;
using System.Reflection;

namespace NiCloud;

public static class MemoryExtensions
{
    public static bool Equals(this string s, ReadOnlyMemory<char> value, StringComparison comparisonType)
        => s.AsSpan().Equals(value.Span, comparisonType);
}

public enum CookieToken
{
    None,
    NameValue,  // X=Y
    Attribute,      // X
    EndToken,       // ';'
    EndCookie,      // ','
    EOL,            // EOLN
    EqualsSign,
    Comment,
    CommentUrl,
    CookieName,
    Discard,
    Domain,
    Expires,
    MaxAge,
    Path,
    Port,
    Secure,
    HttpOnly,
    Unknown,
    Version
}

internal class CookieTokenizer
{
    int index;
    int length;
    int start;
    int tokenLength;
    string cookieString;

    public bool EndOfCookie { get; set; }

    public bool Eof => index >= length;

    public string Name { get; set; }

    public bool Quoted { get; set; }

    public CookieToken Token { get; set; }

    public string Value { get; set; }

    public CookieTokenizer(string cookieString)
    {
        length = cookieString.Length;
        this.cookieString = cookieString;
    }

    public string Extract()
    {
        if (tokenLength != 0)
        {
            var tokenString = cookieString.AsSpan(start, tokenLength);
            return Quoted
                ? tokenString.ToString()
                : tokenString.Trim().ToString();
        }

        return string.Empty;
    }

    //  Find the start and length of the next token. The token is terminated
    //  by one of:
    //
    //      - end-of-line
    //      - end-of-cookie: unquoted comma separates multiple cookies
    //      - end-of-token: unquoted semi-colon
    //      - end-of-name: unquoted equals
    //
    // Inputs:
    //  <argument>  ignoreComma
    //      true if parsing doesn't stop at a comma. This is only true when
    //      we know we're parsing an original cookie that has an expires=
    //      attribute, because the format of the time/date used in expires
    //      is:
    //          Wdy, dd-mmm-yyyy HH:MM:SS GMT
    //
    //  <argument>  ignoreEquals
    //      true if parsing doesn't stop at an equals sign. The LHS of the
    //      first equals sign is an attribute name. The next token may
    //      include one or more equals signs. E.g.,
    //
    //          SESSIONID=ID=MSNx45&q=33
    //
    // Outputs:
    //  <member>    m_index
    //      incremented to the last position in m_tokenStream contained by
    //      the current token
    //
    //  <member>    m_start
    //      incremented to the start of the current token
    //
    //  <member>    m_tokenLength
    //      set to the length of the current token
    //
    // Assumes:
    //  Nothing
    //
    // Returns:
    //  type of CookieToken found:
    //
    //      End         - end of the cookie string
    //      EndCookie   - end of current cookie in (potentially) a
    //                    multi-cookie string
    //      EndToken    - end of name=value pair, or end of an attribute
    //      Equals      - end of name=
    //
    // Throws:
    //  Nothing
    //

    private CookieToken FindNext(bool ignoreComma, bool ignoreEquals)
    {
        tokenLength = 0;
        start = index;
        while ((index < length) && char.IsWhiteSpace(cookieString[index]))
        {
            ++index;
            ++start;
        }

        CookieToken token = CookieToken.EOL;
        int increment = 1;

        if (!Eof)
        {
            if (cookieString[index] == '"')
            {
                Quoted = true;
                index++;
                bool quoteOn = false;
                while (index < length)
                {
                    char currChar = cookieString[index];
                    if (!quoteOn && currChar == '"')
                        break;
                    if (quoteOn)
                        quoteOn = false;
                    else if (currChar == '\\')
                        quoteOn = true;
                    ++index;
                }
                if (index < length)
                {
                    index++;
                }
                tokenLength = index - start;
                increment = 0;
                // if we are here, reset ignoreComma
                // In effect, we ignore everything after quoted string till next delimiter
                ignoreComma = false;
            }
            while ((index < length)
                   && (cookieString[index] != ';')
                   && (ignoreEquals || (cookieString[index] != '='))
                   && (ignoreComma || (cookieString[index] != ',')))
            {

                // Fixing 2 things:
                // 1) ignore day of week in cookie string
                // 2) revert ignoreComma once meet it, so won't miss the next cookie)
                if (cookieString[index] == ',')
                {
                    start = index + 1;
                    tokenLength = -1;
                    ignoreComma = false;
                }
                index++;
                tokenLength += increment;

            }
            if (!Eof)
            {
                token = cookieString[index] switch
                {
                    ';' => CookieToken.EndToken,
                    '=' => CookieToken.EqualsSign,
                    _ => CookieToken.EndCookie,
                };
                index++;
            }
        }
        return token;
    }

    //
    // Next
    //
    //  Get the next cookie name/value or attribute
    //
    //  Cookies come in the following formats:
    //
    //      1. Version0
    //          Set-Cookie: [<name>][=][<value>]
    //                      [; expires=<date>]
    //                      [; path=<path>]
    //                      [; domain=<domain>]
    //                      [; secure]
    //          Cookie: <name>=<value>
    //
    //          Notes: <name> and/or <value> may be blank
    //                 <date> is the RFC 822/1123 date format that
    //                 incorporates commas, e.g.
    //                 "Wednesday, 09-Nov-99 23:12:40 GMT"
    //
    //      2. RFC 2109
    //          Set-Cookie: 1#{
    //                          <name>=<value>
    //                          [; comment=<comment>]
    //                          [; domain=<domain>]
    //                          [; max-age=<seconds>]
    //                          [; path=<path>]
    //                          [; secure]
    //                          ; Version=<version>
    //                      }
    //          Cookie: $Version=<version>
    //                  1#{
    //                      ; <name>=<value>
    //                      [; path=<path>]
    //                      [; domain=<domain>]
    //                  }
    //
    //      3. RFC 2965
    //          Set-Cookie2: 1#{
    //                          <name>=<value>
    //                          [; comment=<comment>]
    //                          [; commentURL=<comment>]
    //                          [; discard]
    //                          [; domain=<domain>]
    //                          [; max-age=<seconds>]
    //                          [; path=<path>]
    //                          [; ports=<portlist>]
    //                          [; secure]
    //                          ; Version=<version>
    //                       }
    //          Cookie: $Version=<version>
    //                  1#{
    //                      ; <name>=<value>
    //                      [; path=<path>]
    //                      [; domain=<domain>]
    //                      [; port="<port>"]
    //                  }
    //          [Cookie2: $Version=<version>]
    //
    // Inputs:
    //  <argument>  first
    //      true if this is the first name/attribute that we have looked for
    //      in the cookie stream
    //
    // Outputs:
    //
    // Assumes:
    //  Nothing
    //
    // Returns:
    //  type of CookieToken found:
    //
    //      - Attribute
    //          - token was single-value. May be empty. Caller should check
    //            Eof or EndCookie to determine if any more action needs to
    //            be taken
    //
    //      - NameValuePair
    //          - Name and Value are meaningful. Either may be empty
    //
    // Throws:
    //  Nothing
    //

    public CookieToken Next(bool first, bool parseResponseCookies)
    {
        Reset();

        var terminator = FindNext(false, false);
        if (terminator == CookieToken.EndCookie)
        {
            EndOfCookie = true;
        }

        if ((terminator == CookieToken.EOL) || (terminator == CookieToken.EndCookie))
        {
            if ((Name = Extract()).Length != 0)
            {
                Token = TokenFromName(parseResponseCookies);
                return CookieToken.Attribute;
            }

            return terminator;
        }

        Name = Extract();
        if (first)
        {
            Token = CookieToken.CookieName;
        }
        else
        {
            Token = TokenFromName(parseResponseCookies);
        }

        if (terminator == CookieToken.EqualsSign)
        {
            terminator = FindNext(!first && (Token == CookieToken.Expires), true);
            if (terminator == CookieToken.EndCookie)
            {
                EndOfCookie = true;
            }

            Value = Extract();
            return CookieToken.NameValue;
        }
        else
        {
            return CookieToken.Attribute;
        }
    }

    /// <summary>
    /// set up this tokenizer for finding the next name/value pair or
    ///  attribute, or end-of-[token, cookie, or line]
    /// </summary>
    public void Reset()
    {
        EndOfCookie = false;
        Name = string.Empty;
        Quoted = false;
        start = index;
        Token = CookieToken.None;
        tokenLength = 0;
        Value = string.Empty;
    }


    private struct RecognizedAttribute
    {
        private readonly string name;

        internal CookieToken Token { get; init; }

        public RecognizedAttribute(string name, CookieToken token)
        {
            this.name = name;
            Token = token;
        }

        internal bool IsEqualTo(string value)
        {
            return string.Compare(name, value, StringComparison.OrdinalIgnoreCase) == 0;
        }
    }

    internal const string CommentAttributeName = "Comment";
    internal const string CommentUrlAttributeName = "CommentURL";
    internal const string DiscardAttributeName = "Discard";
    internal const string DomainAttributeName = "Domain";
    internal const string ExpiresAttributeName = "Expires";
    internal const string MaxAgeAttributeName = "Max-Age";
    internal const string PathAttributeName = "Path";
    internal const string PortAttributeName = "Port";
    internal const string SecureAttributeName = "Secure";
    internal const string VersionAttributeName = "Version";
    internal const string HttpOnlyAttributeName = "HttpOnly";

    static readonly (string name, CookieToken token)[] ClientAttributes = {
        (PathAttributeName, CookieToken.Path),
        (MaxAgeAttributeName, CookieToken.MaxAge),
        (ExpiresAttributeName, CookieToken.Expires),
        (VersionAttributeName, CookieToken.Version),
        (DomainAttributeName, CookieToken.Domain),
        (SecureAttributeName, CookieToken.Secure),
        (DiscardAttributeName, CookieToken.Discard),
        (PortAttributeName, CookieToken.Port),
        (CommentAttributeName, CookieToken.Comment),
        (CommentUrlAttributeName, CookieToken.CommentUrl),
        (HttpOnlyAttributeName, CookieToken.HttpOnly)
    };

    static readonly (string name, CookieToken token)[] ServerAttributes =
    {
        ('$' + PathAttributeName, CookieToken.Path),
        ('$' + VersionAttributeName, CookieToken.Version),
        ('$' + DomainAttributeName, CookieToken.Domain),
        ('$' + PortAttributeName, CookieToken.Port),
        ('$' + HttpOnlyAttributeName, CookieToken.HttpOnly),
    };

    internal CookieToken TokenFromName(bool parseResponseCookies)
    {
        if (!parseResponseCookies)
        {
            for (var i = 0; i < ServerAttributes.Length; ++i)
            {
                if (ServerAttributes[i].name.Equals(Name, StringComparison.OrdinalIgnoreCase))
                {
                    return ServerAttributes[i].token;
                }
            }
        }
        else
        {
            for (var i = 0; i < ClientAttributes.Length; ++i)
            {
                if (ClientAttributes[i].name.Equals(Name, StringComparison.OrdinalIgnoreCase))
                {
                    return ClientAttributes[i].token;
                }
            }
        }

        return CookieToken.Unknown;
    }
}

public class CookieParser
{
    private static readonly FieldInfo IsQuotedDomainField
        = typeof(Cookie).GetField("IsQuotedDomain", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo IsQuotedVersionField
        = typeof(Cookie).GetField("IsQuotedVersion", BindingFlags.NonPublic | BindingFlags.Instance);        

    readonly CookieTokenizer tokenizer;
    Cookie m_savedCookie;

    public CookieParser(string cookieString)
    {
        tokenizer = new CookieTokenizer(cookieString);
    }

    public CookieCollection GetAll()
    {
        var collection = new CookieCollection();
        Cookie cookie;
        do
        {
            cookie = Get();
            if (cookie != null)
            {
                collection.Add(cookie);
            }
        }
        while (cookie != null || !EndofHeader());

        return collection;
    }

    public Cookie Get()
    {
        Cookie cookie = null;

        bool commentSet = false;
        bool commentUriSet = false;
        bool domainSet = false;
        bool expiresSet = false;
        bool pathSet = false;
        bool portSet = false;
        bool versionSet = false;
        bool secureSet = false;
        bool discardSet = false;

        do
        {
            CookieToken token = tokenizer.Next(cookie == null, true);
            if (cookie == null && (token == CookieToken.NameValue || token == CookieToken.Attribute))
            {
                cookie = new Cookie(tokenizer.Name, tokenizer.Value);
            }
            else
            {
                switch (token)
                {
                    case CookieToken.NameValue:
                        switch (tokenizer.Token)
                        {
                            case CookieToken.Comment when !commentSet:
                                commentSet = true;
                                cookie.Comment = tokenizer.Value;
                                break;

                            case CookieToken.CommentUrl when !commentUriSet:
                                commentUriSet = true;
                                if (Uri.TryCreate(CheckQuoted(tokenizer.Value), UriKind.Absolute, out var parsedUri))
                                {
                                    cookie.CommentUri = parsedUri;
                                }
                                break;

                            case CookieToken.Domain when !domainSet:
                                domainSet = true;
                                cookie.Domain = CheckQuoted(tokenizer.Value);
                                IsQuotedDomainField?.SetValue(cookie, tokenizer.Quoted);
                                break;

                            case CookieToken.Expires when !expiresSet:
                                expiresSet = true;

                                if (DateTime.TryParse(CheckQuoted(tokenizer.Value),
                                    CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime expires))
                                {
                                    cookie.Expires = expires;
                                }
                                else
                                {
                                    //this cookie will be rejected
                                    cookie.Name = string.Empty;
                                }
                                break;

                            case CookieToken.MaxAge when !expiresSet:
                                expiresSet = true;
                                if (int.TryParse(CheckQuoted(tokenizer.Value), out int parsed))
                                {
                                    cookie.Expires = DateTime.Now.AddSeconds(parsed);
                                }
                                else
                                {
                                    //this cookie will be rejected
                                    cookie.Name = string.Empty;
                                }
                                break;

                            case CookieToken.Path when !pathSet:
                                pathSet = true;
                                cookie.Path = tokenizer.Value;
                                break;

                            case CookieToken.Port when !portSet:
                                portSet = true;
                                try
                                {
                                    cookie.Port = tokenizer.Value;
                                }
                                catch
                                {
                                    //this cookie will be rejected
                                    cookie.Name = string.Empty;
                                }
                                break;

                            case CookieToken.Version when !versionSet:
                                versionSet = true;
                                if (int.TryParse(CheckQuoted(tokenizer.Value), out var parsedVersion))
                                {
                                    cookie.Version = parsedVersion;
                                    IsQuotedVersionField.SetValue(cookie, tokenizer.Quoted);
                                }
                                else
                                {
                                    //this cookie will be rejected
                                    cookie.Name = string.Empty;
                                }
                                break;
                        }
                        break;

                    case CookieToken.Attribute:
                        switch (tokenizer.Token)
                        {
                            case CookieToken.Discard:
                                if (!discardSet)
                                {
                                    discardSet = true;
                                    cookie.Discard = true;
                                }
                                break;

                            case CookieToken.Secure:
                                if (!secureSet)
                                {
                                    secureSet = true;
                                    cookie.Secure = true;
                                }
                                break;

                            case CookieToken.HttpOnly:
                                cookie.HttpOnly = true;
                                break;

                            case CookieToken.Port:
                                if (!portSet)
                                {
                                    portSet = true;
                                    cookie.Port = string.Empty;
                                }
                                break;
                        }
                        break;
                }
            }
        } while (!tokenizer.Eof && !tokenizer.EndOfCookie);
        return cookie;
    }

    public Cookie GetServer()
    {
        Cookie cookie = m_savedCookie;
        m_savedCookie = null;

        // only first occurence of an attribute value must be counted
        bool domainSet = false;
        bool pathSet = false;
        bool portSet = false;

        do
        {
            bool first = cookie == null || cookie.Name == null || cookie.Name.Length == 0;
            CookieToken token = tokenizer.Next(first, false);

            if (first && (token == CookieToken.NameValue || token == CookieToken.Attribute))
            {
                if (cookie == null)
                {
                    cookie = new Cookie();
                }
                cookie.Name = tokenizer.Name;
                cookie.Value = tokenizer.Value;
            }
            else
            {
                switch (token)
                {
                    case CookieToken.NameValue:
                        switch (tokenizer.Token)
                        {
                            case CookieToken.Domain:
                                if (!domainSet)
                                {
                                    domainSet = true;
                                    cookie.Domain = CheckQuoted(tokenizer.Value);
                                    IsQuotedDomainField?.SetValue(cookie, tokenizer.Quoted);
                                }
                                break;

                            case CookieToken.Path:
                                if (!pathSet)
                                {
                                    pathSet = true;
                                    cookie.Path = tokenizer.Value;
                                }
                                break;

                            case CookieToken.Port:
                                if (!portSet)
                                {
                                    portSet = true;
                                    try
                                    {
                                        cookie.Port = tokenizer.Value;
                                    }
                                    catch (CookieException)
                                    {
                                        //this cookie will be rejected
                                        cookie.Name = string.Empty;
                                    }
                                }
                                break;

                            case CookieToken.Version:
                                // this is a new cookie, this token is for the next cookie.
                                m_savedCookie = new Cookie();
                                if (int.TryParse(tokenizer.Value, out int parsed))
                                {
                                    m_savedCookie.Version = parsed;
                                }
                                return cookie;

                            case CookieToken.Unknown:
                                // this is a new cookie, the token is for the next cookie.
                                m_savedCookie = new Cookie(tokenizer.Name, tokenizer.Value);
                                return cookie;

                        }
                        break;

                    case CookieToken.Attribute:
                        switch (tokenizer.Token)
                        {
                            case CookieToken.Port:
                                if (!portSet)
                                {
                                    portSet = true;
                                    cookie.Port = string.Empty;
                                }
                                break;
                        }
                        break;
                }
            }
        } while (!tokenizer.Eof && !tokenizer.EndOfCookie);
        return cookie;
    }

    protected static string CheckQuoted(string value)
    {
        if (value.Length < 2 || value[0] != '\"' || value[value.Length - 1] != '\"')
        {
            return value;
        }

        return value.Length == 2 ? string.Empty : value[1..^1];
    }

    internal bool EndofHeader()
    {
        return tokenizer.Eof;
    }
}
