using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ghostty.Core.Clipboard;
using Ghostty.Tests.Clipboard.Fakes;
using Xunit;

namespace Ghostty.Tests.Clipboard;

/// <summary>
/// Pure-logic tests for ClipboardService. Uses real Ghostty.Core types
/// with hand-written fake backend and confirmer; no mocking framework,
/// no source-stubs.
/// </summary>
public sealed class ClipboardServiceTests
{
    private static (ClipboardService Service, FakeClipboardBackend Backend, FakeClipboardConfirmer Confirmer) Make()
    {
        var backend = new FakeClipboardBackend();
        var confirmer = new FakeClipboardConfirmer();
        return (new ClipboardService(backend, confirmer), backend, confirmer);
    }

    // Read path

    [Fact]
    public async Task HandleReadAsync_Standard_BackendHasText_ReturnsText()
    {
        var (svc, backend, _) = Make();
        backend.StoredText = "hello";

        var result = await svc.HandleReadAsync(ClipboardKind.Standard);

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task HandleReadAsync_Standard_BackendEmpty_ReturnsNull()
    {
        var (svc, backend, _) = Make();
        backend.StoredText = null;

        var result = await svc.HandleReadAsync(ClipboardKind.Standard);

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleReadAsync_Selection_AlwaysReturnsNull()
    {
        var (svc, backend, _) = Make();
        backend.StoredText = "this should never be returned";

        var result = await svc.HandleReadAsync(ClipboardKind.Selection);

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleReadAsync_BackendThrows_ReturnsNull()
    {
        var (svc, backend, _) = Make();
        backend.OnRead = () => throw new InvalidOperationException("clipboard locked");

        var result = await svc.HandleReadAsync(ClipboardKind.Standard);

        Assert.Null(result);
    }
}
