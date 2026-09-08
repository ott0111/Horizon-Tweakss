using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Horizon.Core.Interfaces;
using Horizon.Core.Models;

namespace Horizon.Services;

public sealed class BackendAuthenticationService : IAuthenticationService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ISecureSessionStore _sessionStore;
    private readonly HttpClient _http;
    private readonly Uri _apiBase;
    private readonly List<AuthenticationProviderAvailability> _providers =
    [
        new("Discord", false, "Checking sign-in availability..."),
        new("Google", false, "Checking sign-in availability..."),
        new("Epic Games", false, "Checking sign-in availability...")
    ];
    private StoredAuthenticationSession? _session;

    public BackendAuthenticationService(ISecureSessionStore sessionStore)
        : this(sessionStore, Environment.GetEnvironmentVariable("HORIZON_API_BASE_URL"), null) { }

    internal BackendAuthenticationService(ISecureSessionStore sessionStore, string? apiBaseUrl, HttpMessageHandler? handler)
    {
        _sessionStore = sessionStore;
        _apiBase = ValidateApiBase(apiBaseUrl);
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = _apiBase;
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public event EventHandler<AuthenticationStateChanged>? StateChanged;
    public IReadOnlyList<AuthenticationProviderAvailability> Providers => _providers;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Reading the protected desktop session is local and should never wait on
        // provider discovery. Bound the startup probe so an offline API cannot make
        // Horizon look frozen for the HttpClient's full request timeout.
        _session = await _sessionStore.LoadAsync(cancellationToken);
        using var availabilityTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        availabilityTimeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var response = await SendAsync<ProviderStatus[]>(HttpMethod.Get, "v1/auth/providers", null, false, availabilityTimeout.Token);
            _providers.Clear();
            foreach (var item in response)
            {
                var name = item.Id switch { "epic" => "Epic Games", "discord" => "Discord", _ => "Google" };
                _providers.Add(new(name, item.Available, item.Message ?? (item.Available ? "Available" : $"{name} sign-in is currently unavailable.")));
            }
        }
        catch (HorizonAuthenticationException)
        {
            MarkProvidersUnavailable();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            MarkProvidersUnavailable();
        }
    }

    public async Task<AuthenticationResult?> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        _session ??= await _sessionStore.LoadAsync(cancellationToken);
        if (_session is null) return null;
        if (!SameApi(_session.ApiBaseUrl))
        {
            await _sessionStore.ClearAsync(cancellationToken);
            _session = null;
            return null;
        }
        try
        {
            return await RefreshAsync(cancellationToken);
        }
        catch (HorizonAuthenticationException exception) when (exception.State != AuthenticationState.Unavailable)
        {
            await _sessionStore.ClearAsync(cancellationToken);
            _session = null;
            return null;
        }
    }

    public async Task<AuthenticationResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        ValidateEmail(email);
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Enter your password.", nameof(password));
        Raise(AuthenticationState.SigningIn, "Signing you in.");
        var result = await SendAsync<SessionResponse>(HttpMethod.Post, "v1/auth/login",
            new { email = email.Trim(), password, deviceName = Environment.MachineName }, false, cancellationToken);
        return await SaveSessionAsync(result, cancellationToken);
    }

    public async Task<AuthenticationResult> CreateAccountAsync(string displayName, string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Enter a display name.", nameof(displayName));
        ValidateEmail(email);
        if (password.Length < 10) throw new ArgumentException("Use at least 10 characters for your password.", nameof(password));
        Raise(AuthenticationState.SigningIn, "Creating your Horizon account.");
        var result = await SendAsync<SessionResponse>(HttpMethod.Post, "v1/auth/register",
            new { displayName = displayName.Trim(), email = email.Trim(), password, deviceName = Environment.MachineName }, false, cancellationToken);
        return await SaveSessionAsync(result, cancellationToken);
    }

    public Task<AuthenticationResult> SignInWithProviderAsync(string provider, CancellationToken cancellationToken = default) =>
        AuthenticateWithProviderAsync(provider, false, cancellationToken);

    public Task<AuthenticationResult> LinkProviderAsync(string provider, CancellationToken cancellationToken = default) =>
        AuthenticateWithProviderAsync(provider, true, cancellationToken);

    private async Task<AuthenticationResult> AuthenticateWithProviderAsync(string provider, bool link, CancellationToken cancellationToken)
    {
        var id = ProviderId(provider);
        var availability = _providers.FirstOrDefault(item => item.Name.Equals(provider, StringComparison.OrdinalIgnoreCase));
        if (availability is { IsConfigured: false }) throw new HorizonAuthenticationException(availability.Message, AuthenticationState.Unavailable);

        using var listener = new DesktopCallbackListener();
        var state = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var startPath = link ? $"v1/account/link/{id}/start" : $"v1/auth/oauth/{id}/start";
        var start = await SendAsync<OAuthStartResponse>(HttpMethod.Post, startPath,
            new { desktopRedirectUri = listener.CallbackUri.ToString(), clientState = state }, link, cancellationToken);
        Raise(AuthenticationState.OpeningProvider, $"Opening {provider} in your browser.");
        try { Process.Start(new ProcessStartInfo(start.AuthorizationUrl) { UseShellExecute = true }); }
        catch (Exception exception) { throw new HorizonAuthenticationException("Horizon couldn't open your default browser.", AuthenticationState.Error, exception); }
        Raise(AuthenticationState.WaitingForCallback, $"Waiting for you to finish with {provider}.");
        var callback = await listener.WaitAsync(TimeSpan.FromMinutes(5), cancellationToken);
        if (!string.Equals(callback.State, state, StringComparison.Ordinal))
            throw new HorizonAuthenticationException("Sign-in couldn't be verified. Please try again.", AuthenticationState.Error);
        if (!string.IsNullOrWhiteSpace(callback.Error))
            throw new HorizonAuthenticationException(callback.ErrorDescription ?? "Sign-in was cancelled.",
                callback.Error == "CANCELLED" ? AuthenticationState.Cancelled : AuthenticationState.Error);
        if (string.IsNullOrWhiteSpace(callback.Code))
            throw new HorizonAuthenticationException("Horizon couldn't finish sign-in. Please try again.", AuthenticationState.Error);
        Raise(AuthenticationState.SigningIn, "Finishing sign-in.");
        var result = await SendAsync<SessionResponse>(HttpMethod.Post, "v1/auth/desktop/exchange",
            new { code = callback.Code, redirectUri = listener.CallbackUri.ToString(), deviceName = Environment.MachineName }, false, cancellationToken);
        return await SaveSessionAsync(result, cancellationToken);
    }

    public async Task<PasswordResetRequestResult> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        ValidateEmail(email);
        var result = await SendAsync<PasswordResetResponse>(HttpMethod.Post, "v1/auth/forgot-password", new { email = email.Trim() }, false, cancellationToken);
        return new(result.Accepted, result.DevelopmentResetToken);
    }

    public Task ResetPasswordAsync(string token, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Enter the reset code from your email.", nameof(token));
        if (password.Length < 10) throw new ArgumentException("Use at least 10 characters for your password.", nameof(password));
        return SendWithoutResultAsync(HttpMethod.Post, "v1/auth/reset-password", new { token = token.Trim(), password }, false, cancellationToken);
    }

    public Task VerifyEmailAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Enter your verification code.", nameof(token));
        return SendWithoutResultAsync(HttpMethod.Post, "v1/auth/verify-email", new { token = token.Trim() }, false, cancellationToken);
    }

    public async Task<UserProfile> UpdateProfileAsync(string displayName, string? profilePictureUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Enter a display name.", nameof(displayName));
        var user = await SendAsync<UserResponse>(HttpMethod.Patch, "v1/account/profile",
            new { displayName = displayName.Trim(), profilePictureUrl }, true, cancellationToken);
        return Map(user);
    }

    public async Task<UserProfile> UpdateOnboardingAsync(int step, bool completed, CancellationToken cancellationToken = default)
    {
        var user = await SendAsync<UserResponse>(HttpMethod.Put, "v1/account/onboarding", new { step, completed }, true, cancellationToken);
        return Map(user);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_session is not null) await SendWithoutResultAsync(HttpMethod.Post, "v1/auth/logout", new { refreshToken = _session.RefreshToken }, false, cancellationToken);
        }
        catch (HorizonAuthenticationException) { }
        finally { _session = null; await _sessionStore.ClearAsync(cancellationToken); }
    }

    private async Task<AuthenticationResult> RefreshAsync(CancellationToken cancellationToken)
    {
        var result = await SendAsync<SessionResponse>(HttpMethod.Post, "v1/auth/refresh",
            new { refreshToken = _session!.RefreshToken, deviceName = Environment.MachineName }, false, cancellationToken);
        return await SaveSessionAsync(result, cancellationToken);
    }

    private async Task<AuthenticationResult> SaveSessionAsync(SessionResponse response, CancellationToken cancellationToken)
    {
        _session = new(response.AccessToken, response.RefreshToken, response.AccessExpiresAt, _apiBase.ToString());
        await _sessionStore.SaveAsync(_session, cancellationToken);
        return new(Map(response.User), response.IsNewAccount);
    }

    private async Task SendWithoutResultAsync(HttpMethod method, string path, object body, bool authenticated, CancellationToken cancellationToken) =>
        _ = await SendAsync<JsonElement>(method, path, body, authenticated, cancellationToken);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, bool authenticated, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        if (authenticated)
        {
            if (_session is null) throw new HorizonAuthenticationException("Sign in to continue.", AuthenticationState.Error);
            request.Headers.Authorization = new("Bearer", _session.AccessToken);
        }
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode && response.StatusCode == HttpStatusCode.NoContent) return default!;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var envelope = await JsonSerializer.DeserializeAsync<ApiEnvelope<T>>(stream, Json, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var code = envelope?.Error?.Code;
                var state = response.StatusCode == HttpStatusCode.ServiceUnavailable || code == "PROVIDER_UNAVAILABLE"
                    ? AuthenticationState.Unavailable : code == "CANCELLED" ? AuthenticationState.Cancelled : AuthenticationState.Error;
                throw new HorizonAuthenticationException(envelope?.Error?.Message ?? "Horizon couldn't complete that request.", state);
            }
            return envelope is { Data: not null } ? envelope.Data : throw new HorizonAuthenticationException("Horizon received an unexpected response. Please try again.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HorizonAuthenticationException("Sign-in took too long. Check your connection and try again.", AuthenticationState.Unavailable);
        }
        catch (HttpRequestException exception)
        {
            Trace.TraceError($"Horizon account connection failed for {_apiBase}: {exception}");
            throw new HorizonAuthenticationException("Horizon account services are unavailable. Check your connection and try again.", AuthenticationState.Unavailable, exception);
        }
        catch (JsonException exception)
        {
            Trace.TraceError($"Horizon account response could not be read: {exception}");
            throw new HorizonAuthenticationException("Horizon received an unexpected response. Please try again.", AuthenticationState.Error, exception);
        }
    }

    private static UserProfile Map(UserResponse user) => new()
    {
        AccountId = user.Id, DisplayName = user.DisplayName, Email = user.Email, EmailVerified = user.EmailVerified,
        AuthenticationProviders = user.Providers.ToList(), AccountCreatedAt = user.CreatedAt, OnboardingStep = user.OnboardingStep,
        OnboardingCompleted = user.OnboardingCompleted, ProviderAvatarUrl = user.ProfilePictureUrl, DeviceName = Environment.MachineName
    };

    private void Raise(AuthenticationState state, string message) => StateChanged?.Invoke(this, new(state, message));
    private void MarkProvidersUnavailable()
    {
        _providers.Clear();
        foreach (var name in new[] { "Discord", "Google", "Epic Games" })
            _providers.Add(new(name, false, $"{name} sign-in is currently unavailable."));
    }
    private bool SameApi(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri == _apiBase;
    private static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !System.Net.Mail.MailAddress.TryCreate(email.Trim(), out _))
            throw new ArgumentException("Enter a valid email address.", nameof(email));
    }
    private static string ProviderId(string provider) => provider.Trim().ToLowerInvariant() switch
    {
        "google" => "google", "discord" => "discord", "epic games" or "epic" => "epic",
        _ => throw new HorizonAuthenticationException("That sign-in option is not supported.", AuthenticationState.Error)
    };
    private static Uri ValidateApiBase(string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "http://127.0.0.1:8787/" : value.Trim();
        if (!Uri.TryCreate(value.EndsWith('/') ? value : value + "/", UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip))))
            throw new InvalidOperationException("HORIZON_API_BASE_URL must use HTTPS, or HTTP on a loopback address for local development.");
        return uri;
    }

    private sealed record ApiEnvelope<T>(T? Data, ApiError? Error);
    private sealed record ApiError(string Code, string Message);
    private sealed record ProviderStatus(string Id, bool Available, string? Message);
    private sealed record OAuthStartResponse(string AuthorizationUrl, int ExpiresInSeconds);
    private sealed record PasswordResetResponse(bool Accepted, string? DevelopmentResetToken);
    private sealed record SessionResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt, UserResponse User, bool IsNewAccount);
    private sealed record UserResponse(string Id, string DisplayName, string? ProfilePictureUrl, string? Email, bool EmailVerified,
        string[] Providers, DateTimeOffset CreatedAt, int OnboardingStep, bool OnboardingCompleted);
}

internal sealed class DesktopCallbackListener : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    public DesktopCallbackListener()
    {
        _listener.Start(1);
        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        CallbackUri = new($"http://127.0.0.1:{endpoint.Port}/callback");
    }
    public Uri CallbackUri { get; }

    public async Task<DesktopCallback> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(timeoutSource.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            var requestLine = await reader.ReadLineAsync(timeoutSource.Token);
            if (string.IsNullOrWhiteSpace(requestLine)) throw new HorizonAuthenticationException("Sign-in returned an empty response. Please try again.");
            string? line;
            do { line = await reader.ReadLineAsync(timeoutSource.Token); } while (!string.IsNullOrEmpty(line));
            var parts = requestLine.Split(' ');
            if (parts.Length < 3 || !string.Equals(parts[0], "GET", StringComparison.Ordinal) ||
                !Uri.TryCreate(CallbackUri, parts[1], out var requestUri) ||
                requestUri.Scheme != CallbackUri.Scheme || requestUri.Host != CallbackUri.Host ||
                requestUri.Port != CallbackUri.Port || requestUri.AbsolutePath != "/callback")
                throw new HorizonAuthenticationException("Sign-in returned an invalid response. Please try again.");
            var query = ParseQuery(requestUri.Query);
            var ok = query.ContainsKey("code") && !query.ContainsKey("error");
            var html = ok
                ? "<!doctype html><title>Horizon</title><style>body{background:#050505;color:#f5f5f5;font:16px Inter,Segoe UI,sans-serif;display:grid;place-items:center;height:100vh;margin:0}div{text-align:center}p{color:#a3a3a3}</style><div><h1>Signed in to Horizon</h1><p>You can close this window and return to the app.</p></div>"
                : "<!doctype html><title>Horizon</title><style>body{background:#050505;color:#f5f5f5;font:16px Inter,Segoe UI,sans-serif;display:grid;place-items:center;height:100vh;margin:0}div{text-align:center}p{color:#a3a3a3}</style><div><h1>Sign-in was not completed</h1><p>Return to Horizon to try again.</p></div>";
            var bytes = Encoding.UTF8.GetBytes(html);
            var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, timeoutSource.Token); await stream.WriteAsync(bytes, timeoutSource.Token);
            return new(query.GetValueOrDefault("code"), query.GetValueOrDefault("state"), query.GetValueOrDefault("error"), query.GetValueOrDefault("error_description"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HorizonAuthenticationException("Provider sign-in timed out. Try again when you're ready.", AuthenticationState.Cancelled);
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=', 2);
                var key = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
                var value = Uri.UnescapeDataString((pair.Length > 1 ? pair[1] : string.Empty).Replace('+', ' '));
                if (!values.TryAdd(key, value))
                    throw new HorizonAuthenticationException("Sign-in returned an invalid response. Please try again.");
            }
            return values;
        }
        catch (UriFormatException exception)
        {
            throw new HorizonAuthenticationException("Sign-in returned an invalid response. Please try again.", AuthenticationState.Error, exception);
        }
    }
    public void Dispose() => _listener.Stop();
}

internal sealed record DesktopCallback(string? Code, string? State, string? Error, string? ErrorDescription);
