using System.Text.Json.Serialization;

namespace Arterra.Game.Schemas;

[JsonConverter(typeof(JsonStringEnumConverter<ConnectionRole>))]
public enum ConnectionRole { Host, Join }

public sealed record ConnectionTicket(Guid UserId, Guid SessionId, ConnectionRole Role, DateTimeOffset ExpiresAt);
public sealed record ConnectionTicketResponse(string Ticket, DateTimeOffset ExpiresAt, NetworkEndpoint DiscoveryEndpoint);

// Both executables compile these wire schemas; no HTTP or LiteNetLib implementation is shared.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AuthenticateConnection), "authenticate")]
[JsonDerivedType(typeof(IntroductionResult), "introductionResult")]
[JsonDerivedType(typeof(ConnectionDeparted), "connectionDeparted")]
public abstract record DiscoveryClientMessage;
public sealed record AuthenticateConnection(string Ticket, NetworkEndpoint LocalEndpoint) : DiscoveryClientMessage;
public sealed record IntroductionResult(Guid IntroductionId, bool Connected) : DiscoveryClientMessage;
public sealed record ConnectionDeparted(Guid IntroductionId) : DiscoveryClientMessage;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HostReady), "hostReady")]
[JsonDerivedType(typeof(PeerIntroduction), "introduction")]
[JsonDerivedType(typeof(IntroductionFinished), "introductionFinished")]
[JsonDerivedType(typeof(DiscoveryError), "error")]
public abstract record DiscoveryServerMessage;
public sealed record HostReady(Guid SessionId) : DiscoveryServerMessage;
public sealed record PeerIntroduction(Guid SessionId, Guid IntroductionId, Guid RemoteUserId) : DiscoveryServerMessage;
public sealed record IntroductionFinished(Guid IntroductionId, bool Connected) : DiscoveryServerMessage;
public sealed record DiscoveryError(string Code) : DiscoveryServerMessage;
