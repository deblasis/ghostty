using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ghostty.Core.Sponsor.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ghostty.Tests.Sponsor.Auth;

public partial class OAuthTokenProviderTests
{
    // ---------------------------------------------------------------
    // Test doubles shared across partial class files. Later tasks add
    // more test methods but use the same fakes.
    // ---------------------------------------------------------------

    internal sealed class FakeStore : IJwtStore
    {
        public byte[]? Bytes;
        public int Writes; public int Reads; public int Deletes;
        public Task<byte[]?> ReadAsync(CancellationToken ct) { Reads++; return Task.FromResult(Bytes); }
        public Task WriteAsync(byte[] utf8Token, CancellationToken ct) { Writes++; Bytes = utf8Token; return Task.CompletedTask; }
        public Task DeleteAsync(CancellationToken ct) { Deletes++; Bytes = null; return Task.CompletedTask; }
    }

    internal sealed class FakeBrowser : IBrowserLauncher
    {
        public List<Uri> Opened { get; } = new();
        public void Open(Uri url) => Opened.Add(url);
    }

    internal sealed class FakeListener : ILoopbackListener
    {
        public int Port => 54321;
        public bool Started;
        public Func<CancellationToken, Task<LoopbackResult>>? Behavior;
        public void Start() { Started = true; }
        public Task<LoopbackResult> AwaitCallbackAsync(CancellationToken ct)
            => Behavior?.Invoke(ct) ?? Task.FromResult(new LoopbackResult(null, null, null));
        public void Dispose() { }
    }

    internal sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        public List<HttpRequestMessage> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Respond(request));
        }
    }

    // Build helper. Returns all the fakes so individual tests can drive them.
    internal static (OAuthTokenProvider provider, FakeStore store, FakeBrowser browser, FakeListener listener, FakeHandler handler, FakeTime time) Build(string? envOverride = null)
    {
        var handler = new FakeHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.wintty.io") };
        var auth = new WinttyAuthClient(http, new Uri("https://api.wintty.io"));
        var store = new FakeStore();
        var browser = new FakeBrowser();
        var listener = new FakeListener();
        var time = new FakeTime();
        var provider = new OAuthTokenProvider(
            auth, store, browser, listener,
            new Uri("https://api.wintty.io"),
            NullLogger<OAuthTokenProvider>.Instance,
            time,
            envVarLookup: _ => envOverride);
        return (provider, store, browser, listener, handler, time);
    }

    // JWT helper: build a test JWT expiring `secondsFromNow` in the future.
    internal static string MakeJwt(FakeTime time, int secondsFromNow)
    {
        static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var exp = time.Now.AddSeconds(secondsFromNow).ToUnixTimeSeconds();
        var header = B64(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        var body   = B64(Encoding.UTF8.GetBytes($$"""{"sub":"u","exp":{{exp}},"jti":"j"}"""));
        var sig    = B64(new byte[] { 1, 2, 3 });
        return $"{header}.{body}.{sig}";
    }

    // ---------------------------------------------------------------
    // Task 8 tests
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetTokenAsync_WhenNoCache_ReturnsNull()
    {
        var (provider, _, _, _, _, _) = Build();

        var token = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Null(token);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var (provider, _, _, _, _, _) = Build();

        provider.Dispose();
        provider.Dispose();
    }

    [Fact]
    public void Invalidate_ClearsCache_FiresTokenInvalidated()
    {
        // Minimal skeleton behavior for Task 8: Invalidate synchronously
        // clears the cached token and fires TokenInvalidated. Reactive
        // refresh behavior lands in Task 11 - this test will be refined
        // or superseded then.
        var (provider, _, _, _, _, _) = Build();

        var fired = 0;
        provider.TokenInvalidated += (_, _) => fired++;

        provider.Invalidate();

        Assert.Equal(1, fired);
    }

    // ---------------------------------------------------------------
    // Task 9 tests: InitializeAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task InitializeAsync_WithEnvVar_LoadsVerbatimSkipsStore()
    {
        var time = new FakeTime { Now = DateTimeOffset.UtcNow };
        var jwt = MakeJwt(time, secondsFromNow: 3600);
        var (provider, store, _, _, _, _) = Build(envOverride: jwt);

        await provider.InitializeAsync(CancellationToken.None);

        Assert.Equal(jwt, await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(0, store.Reads);
    }

    [Fact]
    public async Task InitializeAsync_WithNoStoredBlob_StaysNoToken()
    {
        var (provider, store, _, _, _, _) = Build();
        store.Bytes = null;

        await provider.InitializeAsync(CancellationToken.None);

        Assert.Null(await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(1, store.Reads);
    }

    [Fact]
    public async Task InitializeAsync_WithValidStoredBlob_LoadsIntoCache()
    {
        var (provider, store, _, _, _, time) = Build();
        var jwt = MakeJwt(time, secondsFromNow: 3600);
        store.Bytes = Encoding.UTF8.GetBytes(jwt);

        await provider.InitializeAsync(CancellationToken.None);

        Assert.Equal(jwt, await provider.GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task InitializeAsync_WithExpiredButRefreshableBlob_RefreshesAndReplaces()
    {
        var (provider, store, _, _, handler, time) = Build();
        var oldJwt = MakeJwt(time, secondsFromNow: -3600);
        store.Bytes = Encoding.UTF8.GetBytes(oldJwt);
        var newJwt = MakeJwt(time, secondsFromNow: 3600);
        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"token":"{{newJwt}}"}""",
                Encoding.UTF8, "application/json"),
        };

        await provider.InitializeAsync(CancellationToken.None);

        Assert.Equal(newJwt, await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(1, store.Writes);
        Assert.Equal(newJwt, Encoding.UTF8.GetString(store.Bytes!));
    }

    [Fact]
    public async Task InitializeAsync_WithStaleBlobPast24h_DeletesAndStaysNoToken()
    {
        var (provider, store, _, _, _, time) = Build();
        var stale = MakeJwt(time, secondsFromNow: -48 * 3600);
        store.Bytes = Encoding.UTF8.GetBytes(stale);

        await provider.InitializeAsync(CancellationToken.None);

        Assert.Null(await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(1, store.Deletes);
    }

    [Fact]
    public async Task InitializeAsync_WithMalformedBlob_DeletesAndStaysNoToken()
    {
        var (provider, store, _, _, _, _) = Build();
        store.Bytes = Encoding.UTF8.GetBytes("not-a-jwt");

        await provider.InitializeAsync(CancellationToken.None);

        Assert.Null(await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(1, store.Deletes);
    }

    [Fact]
    public async Task InitializeAsync_RefreshFailsUnauthorized_DeletesAndStaysNoToken()
    {
        var (provider, store, _, _, handler, time) = Build();
        var expired = MakeJwt(time, secondsFromNow: -60);
        store.Bytes = Encoding.UTF8.GetBytes(expired);
        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        await provider.InitializeAsync(CancellationToken.None);

        Assert.Null(await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(1, store.Deletes);
    }

    // ---------------------------------------------------------------
    // Task 10 tests: SignInAsync
    // ---------------------------------------------------------------

    private static string? GetQueryParam(Uri url, string key)
    {
        var q = url.Query.TrimStart('?');
        foreach (var pair in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx < 0) continue;
            if (Uri.UnescapeDataString(pair[..idx]) == key)
                return Uri.UnescapeDataString(pair[(idx + 1)..]);
        }
        return null;
    }

    [Fact]
    public async Task SignInAsync_HappyPath_EchoesNoncePersists()
    {
        var (provider, store, browser, listener, _, time) = Build();
        var jwt = MakeJwt(time, secondsFromNow: 3600);
        listener.Behavior = _ =>
        {
            var opened = browser.Opened[^1];
            var nonce = GetQueryParam(opened, "nonce")!;
            return Task.FromResult(new LoopbackResult(jwt, nonce, null));
        };

        var result = await provider.SignInAsync(CancellationToken.None);

        Assert.True(result);
        Assert.Equal(jwt, await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(1, store.Writes);
        Assert.Single(browser.Opened);
        Assert.Equal("api.wintty.io", browser.Opened[0].Host);
        Assert.Equal("/auth/github/start", browser.Opened[0].AbsolutePath);
    }

    [Fact]
    public async Task SignInAsync_NonceMismatch_ReturnsFalse()
    {
        var (provider, _, _, listener, _, time) = Build();
        var jwt = MakeJwt(time, secondsFromNow: 3600);
        listener.Behavior = _ => Task.FromResult(new LoopbackResult(jwt, "wrong-nonce", null));

        var result = await provider.SignInAsync(CancellationToken.None);

        Assert.False(result);
        Assert.Null(await provider.GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SignInAsync_ListenerCanceled_ReturnsFalse()
    {
        var (provider, _, _, listener, _, _) = Build();
        listener.Behavior = ct => Task.FromException<LoopbackResult>(new OperationCanceledException(ct));

        var result = await provider.SignInAsync(CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task SignInAsync_OAuthErrorQueryParam_ReturnsFalse()
    {
        var (provider, _, _, listener, _, _) = Build();
        listener.Behavior = _ => Task.FromResult(new LoopbackResult(null, null, "access_denied"));

        var result = await provider.SignInAsync(CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task SignInAsync_ReturnedJwtPreExpired_ReturnsFalseDoesNotPersist()
    {
        var (provider, store, browser, listener, _, time) = Build();
        var expired = MakeJwt(time, secondsFromNow: -10);
        listener.Behavior = _ =>
        {
            var nonce = GetQueryParam(browser.Opened[^1], "nonce")!;
            return Task.FromResult(new LoopbackResult(expired, nonce, null));
        };

        var result = await provider.SignInAsync(CancellationToken.None);

        Assert.False(result);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task SignInAsync_TokenMissing_ReturnsFalse()
    {
        var (provider, _, browser, listener, _, _) = Build();
        listener.Behavior = _ =>
        {
            var nonce = GetQueryParam(browser.Opened[^1], "nonce")!;
            return Task.FromResult(new LoopbackResult(null, nonce, null));
        };

        var result = await provider.SignInAsync(CancellationToken.None);

        Assert.False(result);
    }
}
