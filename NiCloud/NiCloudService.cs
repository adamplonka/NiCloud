using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static NiCloud.JsonSerializeOptions;

namespace NiCloud;

public class NiCloudService
{
    const string WidgetKey = "d39ba9916b7251055b22c7f910e2ea796ee65e98b2ddecea8f5dde8d9d1a815d";
    const string AUTH_ENDPOINT = "https://idmsa.apple.com/appleauth/auth";
    const string HOME_ENDPOINT = "https://www.icloud.com";
    const string SETUP_ENDPOINT = "https://setup.icloud.com/setup/ws/1";

    private readonly NiCloudSession session;
    private readonly ILogger logger;
    private NiCloudData data = NiCloudData.Empty;
    private IReadOnlyDictionary<string, NiCloudWebService> webservices => data?.Webservices;
    readonly HttpClient client;


    public NiCloudSession Session => session;

    public string Dsid => data.DsInfo.Dsid;

    public string ClientId => session.ClientId;

    public bool Requires2sa =>
        data.DsInfo?.HsaVersion >= 1 &&
        (data.HsaChallengeRequired == true || !IsTrustedSession);

    public bool Requires2fa =>
        data.DsInfo?.HsaVersion == 2 &&
        (data.HsaChallengeRequired == true || !IsTrustedSession);

    public bool IsTrustedSession =>
        data.HsaTrustedBrowser == true;

    public NiCloudService() : this(null)
    {
    }

    public NiCloudService(ILogger<NiCloudService> logger) : this(new NiCloudSession(), logger)
    {
    }

    public NiCloudService(NiCloudSession session, ILogger<NiCloudService> logger)
        : this(session, new SocketsHttpHandler { CookieContainer = session.Cookies, PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30) }, logger)
    {
    }

    internal NiCloudService(NiCloudSession session, HttpMessageHandler messageHandler, ILogger<NiCloudService> logger)
    {
        this.session = session;
        var clientId = session.ClientId;
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls13 | SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;


        client = new(messageHandler, true)
        {
            DefaultRequestHeaders =
            {
                { "Accept", "application/json, text/javascript, */*; q=0.01" },
                { "X-Apple-OAuth-Client-Id", WidgetKey },
                { "X-Apple-OAuth-Client-Type", "firstPartyAuth" },
                { "X-Apple-OAuth-Redirect-URI", HOME_ENDPOINT },
                { "X-Apple-OAuth-Require-Grant-Code", "true" },
                { "X-Apple-OAuth-Response-Mode", "web_message" },
                { "X-Apple-OAuth-Response-Type", "code" },
                { "X-Apple-OAuth-State", clientId },
                { "X-Apple-Frame-Id", clientId },
                { "X-Apple-Domain-Id", "3" },
                { "Origin", HOME_ENDPOINT },
                { "Referer", HOME_ENDPOINT + '/' },
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/94.0.4606.71 Safari/537.36 Edg/94.0.992.38" },
                { "X-Apple-Widget-Key", WidgetKey },
                { "X-Requested-With", "XMLHttpRequest" }
            }
        };

        this.logger = logger ?? NullLogger<NiCloudService>.Instance;
    }

    private void RetrieveSessionData(HttpResponseHeaders headers)
    {
        foreach (var (name, values) in headers)
        {
            switch (name)
            {
                case "X-Apple-ID-Session-Id":
                case "X-Apple-Auth-Attributes":
                case "scnt":
                    Session.AuthHeaders[name] = values.First();
                    break;
                case "X-Apple-ID-Account-Country":
                    Session.AccountCountry = values.First();
                    break;
                case "X-Apple-Session-Token":
                    Session.SessionToken = values.First();
                    break;
                case "X-Apple-TwoSV-Trust-Token":
                    Session.TrustToken = values.First();
                    break;
            }
        }
    }

    public string GetCookie(string name)
    {
        return Session.Cookies
            .GetCookies(new Uri(HOME_ENDPOINT))
            .FirstOrDefault((Cookie cookie) => cookie.Name == name)
            ?.Value;
    }

    public string GetAuthToken()
    {
        var authCookie = GetCookie("X-APPLE-WEBAUTH-VALIDATE") ?? GetCookie("X-APPLE-WEBAUTH-TOKEN") ?? "";
        var match = Regex.Match(authCookie, @"^v=\d+:t=(.*)$");
        return match.Success ? match.Groups[1].Value : null;
    }


    public Task Init(string accountName, string password)
    {
        User user = new(accountName, password);
        return Authenticate(user);
    }

    internal Task<HttpContent> Post<T>(string url, T data, IDictionary<string, string> headers = null)
    {
        return Send(HttpMethod.Post, url, data, headers);
    }

    internal Task<HttpContent> Get(string url, IDictionary<string, string> headers = null)
    {
        return Send<object>(HttpMethod.Get, url, null, headers);
    }


    public class ErrorResponse
    {
        public bool Success { get; set; }
        public string[] TrustTokens { get; set; }
        public Requestinfo[] RequestInfo { get; set; }
        public Configbag ConfigBag { get; set; }
        public string Error { get; set; }
    }

    public class Configbag
    {
        public Urls urls { get; set; }
        public string accountCreateEnabled { get; set; }
    }

    public class Urls
    {
        public string accountCreateUI { get; set; }
        public string accountLoginUI { get; set; }
        public string accountLogin { get; set; }
        public string accountRepairUI { get; set; }
        public string downloadICloudTerms { get; set; }
        public string repairDone { get; set; }
        public string accountAuthorizeUI { get; set; }
        public string vettingUrlForEmail { get; set; }
        public string accountCreate { get; set; }
        public string getICloudTerms { get; set; }
        public string vettingUrlForPhone { get; set; }
    }

    public class Requestinfo
    {
        public string country { get; set; }
        public string timeZone { get; set; }
        public string region { get; set; }
    }


    private async Task<HttpContent> Send<T>(HttpMethod method, string url, T data, IDictionary<string, string> headers = null, bool retry = true)
    {
        using HttpRequestMessage msg = new(method, url)
        {
            Version = new Version(2, 0),
        };
        msg.Headers.Add("Proxy-Connection", "keep-alive");
        msg.Headers.ConnectionClose = false;
        if (data != null)
        {
            msg.Content = JsonContent.Create(data, new("application/json"), options: CamelCaseProperties);
        }

        if (headers != null)
        {
            foreach (var (key, value) in headers)
            {
                if (value != null)
                {
                    msg.Headers.Add(key, value);
                }
            }
        }

        var response = await client.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        RetrieveSessionData(response.Headers);
        Session.AuthToken = GetAuthToken();

        if (!response.IsSuccessStatusCode && (!IsJson(response) || (int)response.StatusCode is 421 or 450 or 500))
        {
            string reason;
            try
            {
                var errorMessage = await response.Content.ReadAs<ErrorResponse>();
                reason = errorMessage.Error;
            }
            catch
            {
                reason = response.ReasonPhrase;
            }

            if (reason == "Missing X-APPLE-WEBAUTH-TOKEN cookie")
            {
                throw new NiCloudSessionExpired();
            }

            // TODO: handle reauthorization for FindMyPhone (should be probably outside of this class)
            if (retry && (int)response.StatusCode is 421 or 450 or 500)
            {
                var error = new NiCloudApiResponseException(reason) { Code = (int)response.StatusCode, Retry = retry };
                logger.LogDebug(error, "Request failed. Retrying");

                return await Send(method, url, data, headers, false);
            }

            RaiseError(response.StatusCode, reason);
        }

        return response.Content;
    }

    private void RaiseError(HttpStatusCode statusCode, string reason)
    {
        string message = reason;
        if (Requires2sa)
        {
            throw new NiCloud2saRequiredException(); // user["apple_id"]
        }

        if (statusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
        {
            message = "Please log into https://icloud.com/ and finish setting up your iCloud service";
            var notActivatedException = new NiCloudServiceNotActivatedException(message);
            logger.LogWarning(notActivatedException, message);
            throw notActivatedException;
        }

        if (statusCode is HttpStatusCode.Forbidden)
        {
            message = $"{reason}. Please wait a few minutes then try again. " +
                "The remote servers might be trying to throttle requests.";
        }

        if (statusCode is HttpStatusCode.MisdirectedRequest or (HttpStatusCode)450 or HttpStatusCode.InternalServerError)
        {
            message = "Invalid request. Authentication might be required.";
        }

        var apiError = new NiCloudApiResponseException(message);
        logger.LogError(apiError, message);
        throw apiError;
    }

    private static bool IsJson(HttpResponseMessage response) =>
        response.Content.Headers.ContentType.MediaType is "application/json" or "text/json";

    private async Task Federate(User user)
    {
        var data = new Dictionary<string, object>()
        {
            ["rememberMe"] = true,
            ["accountName"] = user.AccountName
        };

        try
        {
            await Post($"{AUTH_ENDPOINT}/federate?isRememberMeEnabled=true", data);
        }
        catch (Exception)
        {
            logger.LogDebug("Could not log into service.");
        }
    }

    public async Task<bool> CheckSession()
    {
        if (session.HasSessionToken)
        {
            try
            {
                data = await ValidateToken();
                return true;
            }
            catch (Exception)
            {
                logger.LogDebug("Invalid authentication token, will log in from scratch.");
            }
        }

        return false;
    }

    public async Task Authenticate(User user, bool forceRefresh = false, string service = null)
    {
        var loginSuccessful = false;

        if (service != null)
        {
            var app = data.Apps?.GetValueOrDefault(service);

            if (app?.CanLaunchWithOneFactor == true)
            {
                logger.LogDebug($"Authenticating as {user.AccountName} for {service}");
                try
                {
                    await AuthenticateWithCredentialsService(user, service);
                    loginSuccessful = true;
                }
                catch (Exception)
                {
                    logger.LogDebug("Could not log into service. Attempting brand new login.");
                }
            }
        }

        if (!loginSuccessful)
        {
            logger.LogDebug($"Authenticating as {user.AccountName}");
            await Federate(user);

            var trustToken = session.TrustToken;
            var data = new Dictionary<string, object>
            {
                ["rememberMe"] = true,
                ["trustTokens"] = trustToken != null
                    ? new[] { trustToken }
                    : Array.Empty<string>(),
                ["accountName"] = user.AccountName,
                ["password"] = user.Password
            };

            var headers = Session.AuthHeaders;

            try
            {
                await Post($"{AUTH_ENDPOINT}/signin?isRememberMeEnabled=true", data, headers);
            }
            catch (Exception ex) // TODO: custom exception NiCloudApiException
            {
                throw new NiCloudFailedLoginException("Invalid email / password combination.", ex);
            }

            await AuthenticateWithToken();
        }

        logger.LogDebug("Authentication completed successfully");
    }

    public async Task<SendVerificationCodeResult> SendVerificationCode()
    {
        var result = await Get($"{AUTH_ENDPOINT}", Session.AuthHeaders);
        return await result.ReadAs<SendVerificationCodeResult>();
    }

    private async Task<NiCloudData> AuthenticateWithToken()
    {
        Dictionary<string, object> req = new()
        {
            ["dsWebAuthToken"] = session.SessionToken,
            ["accountCountryCode"] = session.AccountCountry,
            ["extended_login"] = true,
            ["trustToken"] = session.TrustToken ?? ""
        };

        try
        {
            using var response = await Post($"{SETUP_ENDPOINT}/accountLogin", req).ConfigureAwait(false);
            return data = await response.ReadAs<NiCloudData>();
        }
        catch (Exception ex)
        {
            throw new NiCloudFailedLoginException("Invalid authentication token.", ex);
        }
    }

    private async Task AuthenticateWithCredentialsService(User user, string appName)
    {
        Dictionary<string, object> req = new()
        {
            ["appName"] = appName,
            ["apple_id"] = user.AccountName,
            ["password"] = user.Password
        };

        try
        {
            await Post($"{SETUP_ENDPOINT}/accountLogin", req);
            data = await ValidateToken();
        }
        catch (Exception ex)
        {
            throw new NiCloudFailedLoginException("Invalid email/password combination.", ex);
        }
    }

    /// <summary>
    /// Checks if the current access token is still valid
    /// </summary>
    private async Task<NiCloudData> ValidateToken()
    {
        logger.LogDebug("Checking session token validity");
        try
        {
            using var response = await Post($"{SETUP_ENDPOINT}/validate", (object)null);
            logger.LogDebug("Session token is still valid");
            return await response.ReadAs<NiCloudData>();
        }
        catch (NiCloudApiResponseException)
        {
            logger.LogDebug("Invalid authentication token");
            throw;
        }
    }

    /// <summary>
    /// Returns devices trusted for two-step authentication.
    /// </summary>
    public async Task<NiCloudDevice[]> GetTrustedDevices()
    {
        using var response = await Get($"{SETUP_ENDPOINT}/listDevices");
        var devices = await response.ReadAs<NiCloudDevices>();
        return devices.Devices;
    }

    /// <summary>
    /// Requests that a verification code is sent to the given device.
    /// </summary>
    public async Task<bool> SendVerificationCode(NiCloudDevice device)
    {
        using var response = await Post($"{SETUP_ENDPOINT}/sendVerificationCode", device);
        var result = await response.ReadAs<NiCloudApiResult>();
        return result?.Success == true;
    }

    /// <summary>
    /// Verifies a verification code received on a trusted device.
    /// </summary>
    public async Task<bool> ValidateVerificationCode(NiCloudDevice device, string code)
    {
        Dictionary<string, object> data = new()
        {
            ["verificationCode"] = code,
            ["trustBrowser"] = true
        };

        try
        {
            await Post($"{SETUP_ENDPOINT}/validateVerificationCode", data);
        }
        catch (NiCloudApiResponseException ex)
        {
            if (ex.Code == WRONG_VERIFICATION_CODE)
            {
                return false;
            }
            throw;
        }

        await TrustSession();
        return !Requires2sa;
    }

    /// <summary>
    /// Request session trust to avoid user log in going forward.
    /// </summary>
    public async Task<bool> TrustSession()
    {
        try
        {
            await Get($"{AUTH_ENDPOINT}/2sv/trust", Session.AuthHeaders);
            return (await AuthenticateWithToken())
                ?.HsaTrustedBrowser == true;
        }
        catch (NiCloudApiResponseException)
        {
            logger.LogError("Session trust failed.");
            return false;
        }
    }

    const int WRONG_VERIFICATION_CODE = -21669;

    /// <summary>
    /// Verifies a verification code received via Apple's 2FA system (HSA2).
    /// </summary>
    public async Task<bool> Validate2faCode(string code)
    {
        Dictionary<string, object> data = new()
        {
            ["securityCode"] = new { code }
        };

        try
        {
            await Post($"{AUTH_ENDPOINT}/verify/trusteddevice/securitycode", data, Session.AuthHeaders);
        }
        catch (NiCloudApiResponseException error)
        {
            if (error.Code == WRONG_VERIFICATION_CODE)
            {
                logger.LogError("Code verification failed.");
                return false;
            }
            throw;
        }

        await TrustSession();
        return !Requires2sa;
    }

    public void Dispose()
    {
        client.Dispose();
    }

    /// <summary>
    /// Get webservice URL, raise an exception if not exists.
    /// </summary>
    internal string GetWebServiceUrl(string webService)
        => webservices?.TryGetValue(webService, out var serviceData) == true
            ? serviceData.Url
            : throw new NiCloudServiceNotActivatedException("Webservice not available");
}
