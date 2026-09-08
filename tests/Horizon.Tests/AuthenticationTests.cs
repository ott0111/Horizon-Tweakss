using System.Net;
using System.Net.Sockets;
using System.Text;
using Horizon.Core.Interfaces;
using Horizon.Core.Models;
using Horizon.Services;
using Xunit;

namespace Horizon.Tests;

public sealed class AuthenticationTests
{
    [Theory]
    [InlineData("Discord")]
    [InlineData("Google")]
    [InlineData("Epic Games")]
    public async Task UnconfiguredProviderReturnsControlledUnavailableState(string provider)
    {
        var service = Service(_ => Json(HttpStatusCode.OK, """{"data":[{"id":"discord","available":false,"message":"Discord sign-in is currently unavailable."},{"id":"google","available":false,"message":"Google sign-in is currently unavailable."},{"id":"epic","available":false,"message":"Epic Games sign-in is currently unavailable."}]}"""));
        await service.InitializeAsync();

        var exception = await Assert.ThrowsAsync<HorizonAuthenticationException>(() => service.SignInWithProviderAsync(provider));

        Assert.Equal(AuthenticationState.Unavailable, exception.State);
        Assert.Contains("currently unavailable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NetworkFailureReturnsControlledUnavailableState()
    {
        var service = new BackendAuthenticationService(new MemorySessionStore(), "http://127.0.0.1:8787", new StubHandler(_ => throw new HttpRequestException("offline")));

        var exception = await Assert.ThrowsAsync<HorizonAuthenticationException>(() => service.SignInAsync("lachlan@example.com", "a-long-password"));

        Assert.Equal(AuthenticationState.Unavailable, exception.State);
        Assert.Contains("account services are unavailable", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("", "secret")]
    [InlineData("not-an-email", "secret")]
    [InlineData("lachlan@example.com", "")]
    public async Task InvalidEmailCredentialsAreRejectedLocally(string email, string password)
    {
        var service = Service(_ => throw new InvalidOperationException("HTTP should not be called"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SignInAsync(email, password));
    }

    [Fact]
    public async Task EmailSignInStoresOpaqueSessionAndMapsAccount()
    {
        var store = new MemorySessionStore();
        var service = new BackendAuthenticationService(store, "http://127.0.0.1:8787", new StubHandler(request =>
            Json(HttpStatusCode.OK, """{"data":{"accessToken":"access","refreshToken":"refresh","accessExpiresAt":"2030-01-01T00:00:00Z","isNewAccount":false,"user":{"id":"user-id","displayName":"Lachlan","email":"lachlan@example.com","emailVerified":true,"providers":["email"],"createdAt":"2026-01-01T00:00:00Z","onboardingStep":5,"onboardingCompleted":true}}}""")));

        var result = await service.SignInAsync("lachlan@example.com", "a-long-password");

        Assert.Equal("user-id", result.Profile.AccountId);
        Assert.True(result.Profile.OnboardingCompleted);
        Assert.Equal("refresh", store.Value?.RefreshToken);
    }

    [Fact]
    public async Task CustomerFacingProgressAvoidsImplementationTerminology()
    {
        var service = new BackendAuthenticationService(new MemorySessionStore(), "http://127.0.0.1:8787", new StubHandler(_ =>
            Json(HttpStatusCode.OK, """{"data":{"accessToken":"access","refreshToken":"refresh","accessExpiresAt":"2030-01-01T00:00:00Z","isNewAccount":false,"user":{"id":"user-id","displayName":"Lachlan","email":"lachlan@example.com","emailVerified":true,"providers":["email"],"createdAt":"2026-01-01T00:00:00Z","onboardingStep":5,"onboardingCompleted":true}}}""")));
        var messages = new List<string>();
        service.StateChanged += (_, change) => messages.Add(change.Message);

        await service.SignInAsync("lachlan@example.com", "a-long-password");

        Assert.Contains("Signing you in.", messages);
        Assert.DoesNotContain(messages, message => message.Contains("server", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("API", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SessionForAnotherServiceIsRemovedWithoutSendingIt()
    {
        var store = new MemorySessionStore();
        await store.SaveAsync(new("old-access", "old-refresh", DateTimeOffset.UtcNow.AddDays(1), "http://127.0.0.1:9999/"));
        var service = new BackendAuthenticationService(store, "http://127.0.0.1:8787", new StubHandler(_ =>
            throw new InvalidOperationException("HTTP should not be called")));

        var restored = await service.TryRestoreSessionAsync();

        Assert.Null(restored);
        Assert.Null(store.Value);
    }

    [Fact]
    public async Task DesktopListenerRejectsAbsoluteRequestsForAnotherAuthority()
    {
        using var listener = new DesktopCallbackListener();
        var wait = listener.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listener.CallbackUri.Port);
        var bytes = Encoding.ASCII.GetBytes("GET http://example.com/callback?code=stolen&state=bad HTTP/1.1\r\nHost: example.com\r\n\r\n");
        await client.GetStream().WriteAsync(bytes);

        var exception = await Assert.ThrowsAsync<HorizonAuthenticationException>(() => wait);

        Assert.Contains("invalid response", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProfileUpdateUsesAuthenticatedAccountEndpoint()
    {
        HttpRequestMessage? profileRequest = null;
        var service = Service(request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/v1/account/profile", StringComparison.Ordinal) == true)
            {
                profileRequest = request;
                return Json(HttpStatusCode.OK, """{"data":{"id":"user-id","displayName":"Lachlan H","profilePictureUrl":null,"email":"lachlan@example.com","emailVerified":true,"providers":["email"],"createdAt":"2026-01-01T00:00:00Z","onboardingStep":5,"onboardingCompleted":true}}""");
            }
            return Json(HttpStatusCode.OK, """{"data":{"accessToken":"access","refreshToken":"refresh","accessExpiresAt":"2030-01-01T00:00:00Z","isNewAccount":false,"user":{"id":"user-id","displayName":"Lachlan","email":"lachlan@example.com","emailVerified":true,"providers":["email"],"createdAt":"2026-01-01T00:00:00Z","onboardingStep":5,"onboardingCompleted":true}}}""");
        });
        await service.SignInAsync("lachlan@example.com", "a-long-password");

        var updated = await service.UpdateProfileAsync("Lachlan H", null);

        Assert.Equal("Lachlan H", updated.DisplayName);
        Assert.Equal(HttpMethod.Patch, profileRequest?.Method);
        Assert.Equal("Bearer", profileRequest?.Headers.Authorization?.Scheme);
    }

    private static BackendAuthenticationService Service(Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new(new MemorySessionStore(), "http://127.0.0.1:8787", new StubHandler(response));
    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
    private sealed class MemorySessionStore : ISecureSessionStore
    {
        public StoredAuthenticationSession? Value { get; private set; }
        public Task<StoredAuthenticationSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task SaveAsync(StoredAuthenticationSession session, CancellationToken cancellationToken = default) { Value = session; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken cancellationToken = default) { Value = null; return Task.CompletedTask; }
    }
}
