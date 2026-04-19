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
}
