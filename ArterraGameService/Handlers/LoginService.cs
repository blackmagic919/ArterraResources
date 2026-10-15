using Arterra.Game.Providers;
using Arterra.Game.Schemas;
using Arterra.Game.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Arterra.Game.Handlers;

public sealed class LoginService(
    IEnumerable<IIdentityProvider> providers,
    IIdentityStore identities,
    ILoginTokenStore tokens,
    TimeProvider clock) {

    private readonly Dictionary<string, IIdentityProvider> providers = providers.ToDictionary(p => p.Name);
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken) {
        if (request.Credential.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("credential must contain exactly one provider.");

        var choices = request.Credential.EnumerateObject().ToArray();
        if (choices.Length != 1 || !providers.TryGetValue(choices[0].Name, out var provider))
            throw new ArgumentException("credential must contain exactly one supported provider.");

        var identity = await provider.ValidateAsync(choices[0].Value, cancellationToken);
        var now = clock.GetUtcNow();
        var expiresAt = now + TokenLifetime;
        if (identity.ExpiresAt < expiresAt) expiresAt = identity.ExpiresAt;
        if (expiresAt <= now) throw new UnauthorizedAccessException();

        var userId = await identities.GetOrCreateUserAsync(identity, cancellationToken);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await tokens.SaveAsync(Hash(token), userId, expiresAt, cancellationToken);
        return new LoginResponse(userId, token, expiresAt);
    }

    public Task<Guid?> AuthenticateAsync(string token, CancellationToken cancellationToken) {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit)) return Task.FromResult<Guid?>(null);
        return tokens.FindUserAsync(Hash(token), clock.GetUtcNow(), cancellationToken);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
