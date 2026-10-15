using Arterra.Game.Schemas;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Arterra.Game.Providers;

public sealed class UnityIdentityProvider : IIdentityProvider {
    public const string Issuer = "https://player-auth.services.api.unity.com";
    public string Name => "unity";

    private readonly string projectId;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> keys;
    private readonly JsonWebTokenHandler handler = new() { MaximumTokenSizeInBytes = 16 * 1024 };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public UnityIdentityProvider(HttpClient http, string projectId) {
        this.projectId = projectId;
        keys = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{Issuer}/.well-known/jwks.json", new JwksRetriever(), new HttpDocumentRetriever(http)) {
            AutomaticRefreshInterval = TimeSpan.FromHours(8),
        };
    }

    public async Task<ExternalIdentity> ValidateAsync(JsonElement credential, CancellationToken cancellationToken) {
        UnityCredential? input;
        try { input = credential.Deserialize<UnityCredential>(Json); }
        catch (JsonException) { throw new ArgumentException("Invalid Unity credential."); }

        if (string.IsNullOrWhiteSpace(input?.AuthenticationToken) || input.AuthenticationToken.Length > 16 * 1024)
            throw new ArgumentException("A Unity authenticationToken is required.");

        var configuration = await keys.GetConfigurationAsync(cancellationToken);
        var validation = await handler.ValidateTokenAsync(input.AuthenticationToken, new TokenValidationParameters {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = $"upid:{projectId}",
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
        });

        if (!validation.IsValid) {
            if (validation.Exception is SecurityTokenSignatureKeyNotFoundException) keys.RequestRefresh();
            throw new UnauthorizedAccessException();
        }

        var token = (JsonWebToken)validation.SecurityToken;
        if (!token.TryGetClaim("project_id", out var project) || project.Value != projectId
            || !token.TryGetClaim("token_type", out var type) || type.Value != "authentication"
            || string.IsNullOrWhiteSpace(token.Subject))
            throw new UnauthorizedAccessException();

        return new ExternalIdentity(Issuer, token.Subject, new DateTimeOffset(token.ValidTo, TimeSpan.Zero));
    }

    private sealed class JwksRetriever : IConfigurationRetriever<OpenIdConnectConfiguration> {
        public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
            string address, IDocumentRetriever retriever, CancellationToken cancel) {
            var document = await retriever.GetDocumentAsync(address, cancel);
            var configuration = new OpenIdConnectConfiguration { Issuer = UnityIdentityProvider.Issuer };
            foreach (var key in new JsonWebKeySet(document).GetSigningKeys()) configuration.SigningKeys.Add(key);
            return configuration;
        }
    }
}
