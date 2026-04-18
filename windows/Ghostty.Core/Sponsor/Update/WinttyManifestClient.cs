using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ghostty.Core.Sponsor.Auth;

namespace Ghostty.Core.Sponsor.Update;

/// <summary>
/// Velopack-free HTTP client for the api.wintty.io manifest endpoint.
/// Handles bearer auth injection, HTTP-error taxonomy, and JSON
/// parsing. The shell's <c>WinttyUpdateSource</c> delegates its
/// Velopack <c>IUpdateSource</c> manifest path here and maps the
/// result to <c>VelopackAssetFeed</c>. This type lives in Core so
/// Ghostty.Tests can exercise it without a WinAppSDK project reference.
/// </summary>
public sealed class WinttyManifestClient
{
    private readonly HttpClient _client;
    private readonly ISponsorTokenProvider _tokens;
    private readonly string _channel;
    private readonly Uri _apiBase;

    public WinttyManifestClient(
        HttpClient client,
        ISponsorTokenProvider tokens,
        string channel,
        Uri apiBase)
    {
        _client = client;
        _tokens = tokens;
        _channel = channel;
        _apiBase = apiBase;
    }

    /// <summary>
    /// Fetches and parses the release manifest from
    /// <c>{apiBase}/manifest/{channel}</c>. Throws
    /// <see cref="UpdateCheckException"/> on any auth, network, or
    /// parse failure.
    /// </summary>
    public async Task<IReadOnlyList<VelopackReleaseEntry>> FetchManifestAsync(
        CancellationToken ct)
    {
        var token = await _tokens.GetTokenAsync(ct).ConfigureAwait(false)
            ?? throw new UpdateCheckException(UpdateErrorKind.NoToken, "no JWT available");

        // Uri ctor with relative path: new Uri(base, "/manifest/channel") correctly
        // replaces any path in apiBase. The leading slash is intentional.
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(_apiBase, $"/manifest/{_channel}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateCheckException(UpdateErrorKind.Offline, ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // SendAsync timed out internally (HttpClient.Timeout exceeded)
            // rather than the caller cancelling via ct.
            throw new UpdateCheckException(UpdateErrorKind.Offline, "request timed out", ex);
        }

        using (response)
        {
            var status = (int)response.StatusCode;

            if (status == 401)
            {
                _tokens.Invalidate();
                throw new UpdateCheckException(UpdateErrorKind.AuthExpired, "401 Unauthorized");
            }

            if (status == 403)
                throw new UpdateCheckException(UpdateErrorKind.NotEntitled, "403 Forbidden");

            if (status >= 500 || !response.IsSuccessStatusCode)
                throw new UpdateCheckException(UpdateErrorKind.ServerError, $"HTTP {status}");

            string json = await response.Content
                .ReadAsStringAsync(ct)
                .ConfigureAwait(false);

            try
            {
                var entries = JsonSerializer.Deserialize<List<VelopackReleaseEntry>>(json);
                if (entries is null || entries.Count == 0)
                    throw new UpdateCheckException(
                        UpdateErrorKind.ManifestInvalid, "manifest was empty or null");
                return entries;
            }
            catch (JsonException ex)
            {
                throw new UpdateCheckException(
                    UpdateErrorKind.ManifestInvalid, ex.Message, ex);
            }
        }
    }
}
