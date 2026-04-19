using Ghostty.Core.Sponsor.Auth;
using Xunit;

namespace Ghostty.Tests.Sponsor.Auth;

public class AuthExceptionTests
{
    [Fact]
    public void Ctor_PreservesKindAndMessage()
    {
        var ex = new AuthException(AuthErrorKind.Unauthorized, "revoked jti");

        Assert.Equal(AuthErrorKind.Unauthorized, ex.Kind);
        Assert.Equal("revoked jti", ex.Message);
    }

    [Fact]
    public void Ctor_WithInner_PreservesInner()
    {
        var inner = new System.Net.Http.HttpRequestException("dns fail");

        var ex = new AuthException(AuthErrorKind.Network, "transient", inner);

        Assert.Same(inner, ex.InnerException);
    }
}
