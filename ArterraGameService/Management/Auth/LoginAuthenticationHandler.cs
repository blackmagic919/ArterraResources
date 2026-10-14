using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Arterra.Game.Management.Auth;

public sealed class LoginAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    LoginService login) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder) {

    public const string SchemeName = "ArterraLogin";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync() {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var userId = await login.AuthenticateAsync(header[7..], Context.RequestAborted);
        if (userId == null) return AuthenticateResult.Fail("Invalid or expired login token.");

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
