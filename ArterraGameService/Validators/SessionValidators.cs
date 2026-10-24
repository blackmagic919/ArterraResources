using Arterra.Game.Schemas;
using FluentValidation;
using System.Globalization;

namespace Arterra.Game.Validators;

public sealed class CreateSessionValidator : AbstractValidator<CreateSessionRequest> {
    public const long MaximumPreviewInfoBytes = 4 * 1024 * 1024;
    public const long MaximumDetailedInfoBytes = 32 * 1024 * 1024;

    public CreateSessionValidator() {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(128);
        RuleFor(x => x.PreviewInfoBytes).InclusiveBetween(0, MaximumPreviewInfoBytes);
        RuleFor(x => x.DetailedInfoBytes).InclusiveBetween(0, MaximumDetailedInfoBytes);
        RuleFor(x => x.MaxConnections).GreaterThanOrEqualTo(1);
    }
}

public sealed class SessionQueryValidator : AbstractValidator<SessionQuery> {
    public SessionQueryValidator() {
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
        RuleFor(x => x.Cursor).Cascade(CascadeMode.Stop)
            .Matches(@"\A[0-9a-fA-F]{32}:[0-9]{1,10}\z")
            .Must(cursor => int.TryParse(cursor.AsSpan(33), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .WithMessage("Invalid session cursor.")
            .When(x => x.Cursor != null);
    }
}
