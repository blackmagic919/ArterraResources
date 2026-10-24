using Arterra.Game.Schemas;
using System.Net;
using System.Net.Sockets;
using FluentValidation;

namespace Arterra.Game.Validators;

public sealed class DiscoveryMessageValidator : AbstractValidator<DiscoveryClientMessage> {
    public DiscoveryMessageValidator() {
        RuleFor(x => x).SetInheritanceValidator(validators => {
            validators.Add(new AuthenticateConnectionValidator());
            validators.Add(new IntroductionResultValidator());
            validators.Add(new ConnectionDepartedValidator());
        });
    }
}

public sealed class AuthenticateConnectionValidator : AbstractValidator<AuthenticateConnection> {
    public AuthenticateConnectionValidator() {
        RuleFor(x => x.Ticket).NotEmpty().Length(64).Matches("\\A[0-9a-fA-F]+\\z");
        RuleFor(x => x.LocalEndpoint).NotNull().SetValidator(new LocalEndpointValidator());
    }
}

public sealed class LocalEndpointValidator : AbstractValidator<NetworkEndpoint> {
    public LocalEndpointValidator() {
        RuleFor(x => x.Port).InclusiveBetween(1, 65535);
        RuleFor(x => x.Host).Must(host => IPAddress.TryParse(host, out var address)
            && address.AddressFamily == AddressFamily.InterNetwork
            && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.Broadcast)
            && address.GetAddressBytes()[0] < 224)
            .WithMessage("A unicast IPv4 address is required.");
    }
}

public sealed class IntroductionResultValidator : AbstractValidator<IntroductionResult> {
    public IntroductionResultValidator() {
        RuleFor(x => x.IntroductionId).NotEmpty();
    }
}

public sealed class ConnectionDepartedValidator : AbstractValidator<ConnectionDeparted> {
    public ConnectionDepartedValidator() {
        RuleFor(x => x.IntroductionId).NotEmpty();
    }
}
