# Arterra Discovery Service

.NET 8 / LiteNetLib 2.1.4 process on EC2, UDP port 9050. It authenticates hosting and joining connections, publishes live-host presence, and introduces peers for direct game connections. **There is no relay, tunneling, or relay fallback.** Unity integration is the next step; the Unity game has not been changed by this implementation.

HTTP login, session creation, metadata and ticket issuance live in `../ArterraGameService`. The EC2 executable has no ASP.NET dependency. It compiles the shared connection schemas and ticket-store source by links to that sibling project; it does not load or start the Lambda executable. Transport/polling code is in `Transport/`, and live-session persistence is in `Storage/`.

## Hosting

1. Log in through GameService, create a session, upload metadata and complete it.
2. Request `POST /sessions/{id}/host` with the owner login token. This requires a ready, unexpired session. An already-active host returns 409.
3. The response is `{ ticket, expiresAt, discoveryEndpoint: { host, port } }`. Connect a LiteNetLib `NetManager` to that endpoint, using `NetDataWriter.Put(1)` as the entire connection request data (protocol version 1).
4. After `PeerConnectedEvent` for DiscoveryService, send the `authenticate` message below. This uses the connected channel so the return UDP path is established before consuming a ticket.
5. Wait for `hostReady`. The server has atomically claimed the ready session, recorded the observed public IP/port, and made it discoverable.
6. Keep this discovery connection alive while hosting. LiteNetLib's ping/timeout machinery supplies liveness; no application heartbeat packet is required. The manager must continue running and polling even when gameplay is idle.

Use the **same UDP socket / NetManager** for discovery and game traffic. The local endpoint supplied in `authenticate` is that socket's reachable LAN IPv4 address and bound port, not the REST client's address or an arbitrary game port. Public endpoints come from received UDP peers, never from the JSON request. IPv4 is supported in this first implementation.

## Joining and introductions

1. `POST /sessions/{id}/join` with a login token returns `{ session, connection }`. `connection` has the same ticket response shape as `/host`; `session` has the current discovery view and metadata download URLs.
2. Connect to DiscoveryService and authenticate as above, from the same socket that will connect to the host.
3. DiscoveryService verifies the ticket, checks the host's current database lease, and sends an `introduction` announcement to both peers. Each announcement carries the other user's internal user GUID, the session ID, and the same random introduction ID.
4. It invokes LiteNetLib's `NatPunchModule.NatIntroduce`, passing both peers' local and observed public endpoints. The NAT token is `introductionId.ToString("N")` (32 hexadecimal characters, no hyphens). No login token or connection ticket is used as the NAT token.
5. Clients enable `NatPunchEnabled`, initialize a NAT listener, and poll `NatPunchModule.PollEvents()` as well as `NetManager.PollEvents()`. On `NatIntroductionSuccess`, correlate the token with the authenticated introduction announcement. NAT packets can beat the reliable announcement, so temporarily retain matching candidates until that announcement arrives. Expire pending candidates/introductions after the attempt window.
6. The joining client initiates the direct game connection to a successful candidate endpoint, supplying the introduction ID for the host to match to its pending introduction. The host accepts only a known, pending introduction for that session. A direct connection request may also beat the reliable announcement; retain it briefly within the attempt window until the announcement arrives, and reject it if no matching authorization arrives. A NAT success callback is an endpoint candidate, not proof that the game connection has completed. Trying another candidate remains the client's responsibility if the first direct connection fails.
7. Once the direct connection is established, the joining client sends `introductionResult` with `connected: true`. It can send `false` to abandon the attempt. DiscoveryService notifies the host, then disconnects only the joining client's discovery connection with `introductionFinished` as additional data. The host's discovery connection and both peers' direct game connection remain running.

Introductions repeat once a second for up to 10 seconds because NAT messages are unreliable. Without a successful result, the attempt finishes with `connected: false`. There is no alternate relay route. A fresh attempt requires another `/join` ticket. Established game connection authentication/handling belongs to the Unity integration; this service supplies the authorized user/session association and introduction ID.

DiscoveryService only sends NAT introductions; it ignores incoming unconnected NAT requests/responses. Clients use the existing LiteNetLib implementation, rather than a custom hole-punch protocol.

## Wire schemas

Authoritative message types: `../ArterraGameService/Schemas/ConnectionSchemas.cs`. Connected messages are UTF-8 JSON, camelCase, sent as raw bytes using `DeliveryMethod.ReliableOrdered` on channel 0. Do not wrap JSON in `NetDataWriter.Put(string)`; that adds a length prefix. Connection request data is the separate 32-bit protocol version described above. The maximum client message size is 1024 bytes.

Client authentication:

```json
{"type":"authenticate","ticket":"<64 hexadecimal characters>","localEndpoint":{"host":"192.168.1.10","port":9051}}
```

Server hosting acknowledgement:

```json
{"type":"hostReady","sessionId":"<guid>"}
```

Server introduction announcement (to both peers):

```json
{"type":"introduction","sessionId":"<guid>","introductionId":"<guid>","remoteUserId":"<guid>"}
```

Client attempt completion:

```json
{"type":"introductionResult","introductionId":"<guid>","connected":true}
```

Server completion (connected message to host; disconnect additional data to joining client):

```json
{"type":"introductionFinished","introductionId":"<guid>","connected":true}
```

Rejection/disconnect additional data:

```json
{"type":"error","code":"invalidTicket"}
```

Error codes: `protocolMismatch`, `invalidMessage`, `invalidEndpoint`, `invalidTicket`, `sessionNotReady`, `hostUnavailable`, `timeout`, `hostLeaseExpired`, `sessionUnavailable`, `serviceUnavailable`. Read additional data in `PeerDisconnectedEvent`, including rejected connections. Ordinary disconnects may have no additional data. Logs contain timestamps and session/user/attempt IDs, never bearer tickets.

## Tickets and presence

GameService creates random 32-byte single-use tickets, with a 60-second lifetime and explicit host/join role, user ID, and session ID. DynamoDB stores only a SHA-256 hash of each ticket. EC2 atomically consumes it with a conditional delete and `ALL_OLD`; concurrent reuse has exactly one winner. Expiration is checked in that operation, independent of TTL cleanup. A lost authentication response may consume a ticket; obtain a new ticket to retry.

The reusable login credential stays on HTTPS. The short-lived, single-use ticket is sent over LiteNetLib UDP; this protocol does not add encryption to LiteNetLib.

Host authentication lasts for the connection lifetime, independently of login/ticket expiry. Unauthenticated connections have a five-second deadline; an authentication operation gets a 15-second connection deadline and AWS calls have a 10-second timeout. LiteNetLib detects lost connections after 10 seconds without packets. These are separate from database expiry.

While connected, the server renews the host's database expiry to 90 seconds every 30 seconds. On observed disconnect or graceful shutdown it changes the session back to ready, removes its public endpoint, and preserves the existing expiry. The owner can obtain a new host ticket and reconnect before that expiry. Every connection has a lease ID, and renewal/release requires it: a late operation from the old connection cannot hide or renew its replacement. Expired/deleted sessions cannot be resurrected.

A process crash or database outage may prevent immediate release. Discovery then filters the record out at its last recorded expiry; DynamoDB TTL later removes it and triggers metadata cleanup. Renewal errors retry every five seconds within the remaining lease. A rejected renewal closes hosting. After a crash, recreate a session if its old lease cannot be recovered before expiry. Discovery listings are eventually consistent; join checks use a consistent read and require a locally connected host.

## Deployment and operations

Required environment: `AWS_REGION`, `SESSION_TABLE`, `TICKET_TABLE`; optional `ARTERRA_UDP_PORT` (default 9050). CDK supplies these and grants the instance only session GetItem/UpdateItem and ticket DeleteItem access, alongside its existing bootstrap/SSM permissions. The instance does not access identity tables, login tokens, or S3 metadata.

GameService owns the Elastic IP and DynamoDB resources. The EC2 stack depends on GameService and attaches its Elastic IP. Migrating from the scaffold changes the old public IP if that original EC2 stack already deployed. API host/join responses provide the new endpoint.

Build both sibling services with `bash scripts/build-service.sh` from `ArterraDiscoveryCDK`. The EC2 package remains a self-contained Linux x64 executable. SIGTERM releases host sessions before stopping UDP; systemd gives shutdown 30 seconds. Inspect logs with `journalctl -u arterra-discovery -f`.

Local verification used actual loopback LiteNetLib peers and DynamoDB Local: single-use ticket races/expiry, ownership, a complete introduction and direct connection, renewals beyond ticket expiry, disconnect/reconnect lease isolation, no-relay timeout, and session deletion. Real router/NAT compatibility and deployed AWS behavior still require playtesting.
