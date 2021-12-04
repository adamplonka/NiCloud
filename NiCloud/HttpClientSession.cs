using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace NiCloud;

public class CookieBox
{
    private readonly HashSet<Uri> uris = new();

    private readonly CookieContainer CookieContainer = new();

    public void Add(Uri uri, Cookie cookie)
    {
        uris.Add(uri);
        CookieContainer.Add(uri, cookie);
    }

    public string GetCookieHeader(Uri uri)
    {
        var header = CookieContainer.GetCookieHeader(uri);
        if (header == string.Empty)
        {
            uris.Remove(uri);
        }

        return header;
    }
}

public class HttpClientSession
{
    public CookieBox? CookieBox { get; set; } = new CookieBox();

    private HttpClient client;

    private HttpRequestHeaders defaultRequestHeaders;

    public HttpHeaders DefaultRequestHeaders => defaultRequestHeaders;

    public HttpClientSession(HttpClient client)
    {
        this.client = client;
        defaultRequestHeaders = client.DefaultRequestHeaders;
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken = default)
    {
        PrepareRequestMessage(request);

        var response = await client.SendAsync(request, completionOption, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookieString in cookies)
            {
                var parser = new CookieParser(cookieString);
                var cookie = parser.Get();
                CookieBox?.Add(request.RequestUri!, cookie);
            }
        }
        return response;
    }

    private void PrepareRequestMessage(HttpRequestMessage request)
    {
        var cookieHeader = CookieBox?.GetCookieHeader(request.RequestUri!);
        if (!string.IsNullOrEmpty(cookieHeader))
        {
            request.Headers.Add("Cookie", cookieHeader);
        }

        foreach (var header in defaultRequestHeaders)
        {
            request.Headers.Add(header.Key, header.Value);
        }
    }
}
