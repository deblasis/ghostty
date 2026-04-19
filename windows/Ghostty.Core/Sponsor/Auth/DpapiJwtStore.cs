using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Ghostty.Core.Sponsor.Auth;

/// <summary>
/// DPAPI-backed <see cref="IJwtStore"/>. Persists under
/// <c>{root}/auth.bin</c> using <see cref="ProtectedData"/> with
/// <see cref="DataProtectionScope.CurrentUser"/> and an app-specific
/// entropy blob so the envelope is bound to the current user and this
/// app's identity. Atomic writes via <c>.partial</c> + <c>File.Move</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DpapiJwtStore : IJwtStore
{
    private const string FileName = "auth.bin";
    private const string PartialSuffix = ".partial";

    private readonly string _root;
    private readonly byte[] _entropy;

    public DpapiJwtStore(string root, byte[] entropy)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentNullException.ThrowIfNull(entropy);
        _root = root;
        _entropy = entropy;

        Directory.CreateDirectory(_root);
    }

    private string Target  => Path.Combine(_root, FileName);
    private string Partial => Path.Combine(_root, FileName + PartialSuffix);

    public Task<byte[]?> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(Target))
            return Task.FromResult<byte[]?>(null);

        try
        {
            var encrypted = File.ReadAllBytes(Target);
#pragma warning disable IL2026, IL3050
            // ProtectedData is Windows-only and trim-safe in practice.
            var plain = ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.CurrentUser);
#pragma warning restore IL2026, IL3050
            return Task.FromResult<byte[]?>(plain);
        }
        catch (CryptographicException)
        {
            // Entropy mismatch, corrupted blob, or a different user's
            // session. Treat as "no valid token" and let the caller
            // decide whether to delete.
            return Task.FromResult<byte[]?>(null);
        }
        catch (IOException)
        {
            return Task.FromResult<byte[]?>(null);
        }
    }

    public Task WriteAsync(byte[] utf8Token, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(utf8Token);

#pragma warning disable IL2026, IL3050
        // ProtectedData is Windows-only and trim-safe in practice.
        var encrypted = ProtectedData.Protect(utf8Token, _entropy, DataProtectionScope.CurrentUser);
#pragma warning restore IL2026, IL3050
        File.WriteAllBytes(Partial, encrypted);
        // File.Move with overwrite:true is atomic on NTFS.
        File.Move(Partial, Target, overwrite: true);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(CancellationToken ct)
    {
        try
        {
            if (File.Exists(Target))
                File.Delete(Target);
            if (File.Exists(Partial))
                File.Delete(Partial);
        }
        catch (IOException)
        {
            // Sign-out must not fail on file locks. Logged by caller.
        }
        catch (UnauthorizedAccessException)
        {
            // Same reasoning.
        }
        return Task.CompletedTask;
    }
}
