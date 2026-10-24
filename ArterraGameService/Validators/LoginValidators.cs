using Arterra.Game.Schemas;
using System.Text.Json;
using Arterra.Game.Providers;
using FluentValidation;

namespace Arterra.Game.Validators;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest> {
    public LoginRequestValidator(IEnumerable<IIdentityProvider> providers) {
        var names = providers.Select(provider => provider.Name).ToHashSet();
        RuleFor(x => x.Credential).Cascade(CascadeMode.Stop)
            .Must(value => value.ValueKind == JsonValueKind.Object)
            .WithMessage("credential must be an object.")
            .Must(value => value.EnumerateObject().Count() == 1)
            .WithMessage("credential must contain exactly one provider.")
            .Must(value => names.Contains(value.EnumerateObject().First().Name))
            .WithMessage("credential must contain a supported provider.");
    }
}

public sealed class UnityCredentialValidator : AbstractValidator<UnityCredential> {
    public UnityCredentialValidator() {
        RuleFor(x => x.AuthenticationToken).NotEmpty().MaximumLength(16 * 1024);
    }
}
