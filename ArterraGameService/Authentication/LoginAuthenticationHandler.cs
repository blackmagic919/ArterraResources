using Arterra.Game.Handlers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Arterra.Game.Authentication;

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

        var user = await login.AuthenticateAsync(header[7..], Context.RequestAborted);
        if (user == null) return AuthenticateResult.Fail("Invalid or expired login token.");

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString())], SchemeName);
        if (user.DisplayName != null) identity.AddClaim(new Claim(ClaimTypes.Name, user.DisplayName));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
