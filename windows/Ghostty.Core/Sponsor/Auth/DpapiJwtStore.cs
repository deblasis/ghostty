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
    /// <summary>
    /// Label embedded in the DPAPI entropy blob. Not a secret; bounds
    /// the envelope to this app so other user-scoped applications
    /// calling ProtectedData.Unprotect with default entropy can't read
    /// our JWT. Increment the -vN suffix if the JWT format ever
    /// changes in a backwards-incompatible way.
    /// </summary>
    public const string EntropyLabelV1 = "wintty-sponsor-jwt-v1";

    /// <summary>
    /// Default entropy bytes for DPAPI encryption. Callers building a
    /// DpapiJwtStore pass <see cref="DefaultEntropy"/>; the constant
    /// is exposed so the App wiring doesn't have to duplicate the literal.
    /// </summary>
    public static readonly byte[] DefaultEntropy =
        System.Text.Encoding.UTF8.GetBytes(EntropyLabelV1);

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

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "ProtectedData.Protect/Unprotect are trim-safe P/Invoke wrappers; no dynamic codegen.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "ProtectedData.Protect/Unprotect do not require reflection on application types.")]
    public async Task<byte[]?> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(Target))
            return null;

        try
        {
            var encrypted = await File.ReadAllBytesAsync(Target, ct).ConfigureAwait(false);
            // Note: ProtectedData.Unprotect has no async equivalent. It is a
            // fast in-memory call (microseconds for our ~1KB JWT) so the
            // sync call on the awaiting thread is fine.
            var plain = ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.CurrentUser);
            return plain;
        }
        catch (CryptographicException)
        {
            // Entropy mismatch, corrupted blob, or a different user's
            // session. Treat as "no valid token" and let the caller
            // decide whether to delete.
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "ProtectedData.Protect/Unprotect are trim-safe P/Invoke wrappers; no dynamic codegen.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "ProtectedData.Protect/Unprotect do not require reflection on application types.")]
    public async Task WriteAsync(byte[] utf8Token, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(utf8Token);

        var encrypted = ProtectedData.Protect(utf8Token, _entropy, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(Partial, encrypted, ct).ConfigureAwait(false);
        // File.Move with overwrite:true is atomic on NTFS when source and
        // target are on the same volume. LocalApplicationData lives on the
        // user profile drive, so Partial and Target are always co-located.
        File.Move(Partial, Target, overwrite: true);
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
