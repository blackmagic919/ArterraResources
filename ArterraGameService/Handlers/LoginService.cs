using Arterra.Game.Providers;
using Arterra.Game.Schemas;
using Arterra.Game.Storage;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;

namespace Arterra.Game.Handlers;

public sealed class LoginService(
    IEnumerable<IIdentityProvider> providers,
    IIdentityStore identities,
    ILoginTokenStore tokens,
    TimeProvider clock,
    IValidator<LoginRequest> validator) {

    private readonly Dictionary<string, IIdentityProvider> providers = providers.ToDictionary(p => p.Name);
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken) {
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        var credential = request.Credential.EnumerateObject().Single();
        var provider = providers[credential.Name];

        var identity = await provider.ValidateAsync(credential.Value, cancellationToken);
        var now = clock.GetUtcNow();
        var expiresAt = now + TokenLifetime;
        if (identity.ExpiresAt < expiresAt) expiresAt = identity.ExpiresAt;
        if (expiresAt <= now) throw new UnauthorizedAccessException();

        var userId = await identities.GetOrCreateUserAsync(identity, cancellationToken);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await tokens.SaveAsync(Hash(token), new AuthenticatedUser(userId, identity.DisplayName), expiresAt, cancellationToken);
        return new LoginResponse(userId, token, expiresAt);
    }

    public Task<AuthenticatedUser?> AuthenticateAsync(string token, CancellationToken cancellationToken) {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit)) return Task.FromResult<AuthenticatedUser?>(null);
        return tokens.FindUserAsync(Hash(token), clock.GetUtcNow(), cancellationToken);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
