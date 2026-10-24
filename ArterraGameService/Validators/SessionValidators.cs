using Arterra.Game.Schemas;
using FluentValidation;
using System.Globalization;

namespace Arterra.Game.Validators;

public sealed class CreateSessionValidator : AbstractValidator<CreateSessionRequest> {
    public const long MaximumMetadataBytes = 1024 * 1024;
    public const long MaximumChunkBytes = 16 * 1024 * 1024;

    public CreateSessionValidator() {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(128);
        RuleFor(x => x.MetadataBytes).InclusiveBetween(2, MaximumMetadataBytes);
        RuleFor(x => x.DisplayChunkBytes).InclusiveBetween(1, MaximumChunkBytes);
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
