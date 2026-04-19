using System;

namespace Ghostty.Core.Sponsor.Auth;

/// <summary>
/// Auth-layer exception. <see cref="OAuthTokenProvider"/> catches these
/// and maps to <c>ISponsorTokenProvider</c> state transitions
/// (TokenInvalidated, silent-skip, silent-reschedule) per the design
/// spec's error taxonomy bridge.
/// </summary>
public sealed class AuthException : Exception
{
    public AuthErrorKind Kind { get; }

    public AuthException(AuthErrorKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    public AuthException(AuthErrorKind kind, string message, Exception innerException)
        : base(message, innerException)
    {
        Kind = kind;
    }
}
