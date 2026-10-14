# Arterra Game Service

.NET 8 management service hosted by Lambda behind an API Gateway HTTP API. This project has no LiteNetLib dependency and does not run sockets, NAT introductions, tunnels, or gameplay. `../ArterraDiscoveryService` remains the EC2 connectivity process.

## Identity and login

`POST /login` accepts exactly one provider credential:

```json
{
  "credential": {
    "unity": { "authenticationToken": "<AuthenticationService.Instance.AccessToken>" }
  }
}
```

The Unity provider validates RS256 signatures using Unity's cached JWKS, issuer, project audience, `project_id`, `token_type`, and lifetime. `UNITY_PROJECT_ID` is populated by CDK from `lib/config.ts`. It currently matches the Arterra Unity project. The provider credential is not stored.

`Identities` maps the verified `(issuer, subject)` to an internal GUID. A conditional DynamoDB update makes concurrent first logins resolve to the same ID. New providers implement `IIdentityProvider` and register with DI; provider-specific credential validation remains separate from user mapping. Account linking is not implemented and identities are never linked based on email.

Response:

```json
{
  "userId": "<guid>",
  "sessionToken": "<opaque bearer credential>",
  "expiresAt": "<UTC timestamp>"
}
```

Login tokens are 32 random bytes represented as hexadecimal. Only their SHA-256 hashes are stored in `LoginSessions`. They last up to 30 minutes, capped by the provider token's remaining lifetime. Every authenticated request checks expiration directly; DynamoDB TTL is only cleanup. There is no refresh endpoint: log in again with a current provider credential.

Send `Authorization: Bearer <sessionToken>` on authenticated operations. A login token is distinct from a multiplayer session ID.

## Session API

| Method | Route | Authentication | Behavior |
| --- | --- | --- | --- |
| POST | `/login` | Unity credential | Issue our login token |
| POST | `/sessions` | Login token | Allocate pending session and signed upload URLs |
| POST | `/sessions/{id}/complete` | Owner | Verify and publish uploaded metadata; session becomes ready |
| GET | `/sessions?limit=20&cursor=<guid>` | Public | List active, unexpired sessions |
| GET | `/sessions/{id}` | Public | Get an active session and signed metadata download URLs |
| POST | `/sessions/{id}/join` | Login token | Authorize retrieval of current connection details |
| DELETE | `/sessions/{id}` | Owner | Remove the session; metadata cleanup follows asynchronously |
| GET | `/health` | Public | Process health |

### Create and upload

1. Log in.
2. `POST /sessions` with the name and **exact byte lengths** of the two files:

```json
{
  "name": "My world",
  "metadataBytes": 1234,
  "displayChunkBytes": 45678
}
```

3. The response supplies `sessionId`, the pending session's `expiresAt`, `uploadsExpireAt`, and `metadata`/`displayChunk` upload targets. Each target contains `url`, `contentType`, and `contentLength`.
4. PUT each raw file to its returned S3 URL. Send the specified `Content-Type` and exact `Content-Length`. Those headers are signed; do not use chunked transfer. Upload URLs expire after 10 minutes. If they expire, create a new pending session.
5. Call `POST /sessions/{id}/complete` with the owner login token and no body.

Metadata must be a JSON object (the config and any additional key/value data), at most 1 MiB. The compressed display chunk is a separate opaque binary object, at most 16 MiB; this service does not decompress or define the game's compression format. Limits are in `SessionService`.

Completion reads and validates the metadata, checks both object sizes, then copies the exact verified S3 objects using ETag conditions into server-only `sessions/{id}/` keys. Upload URLs only address `uploads/{id}/`, so later uploads cannot overwrite published metadata. Concurrent completion/deletion is excluded while publication is running. A normal failed publication returns to the uploading state for retry. If Lambda is forcibly terminated during publication, that pending record expires; create a replacement session.

The one-hour pending lifetime covers uploading and waiting for the host connection. Completion is idempotent once ready. Missing/changed uploads return 409; invalid metadata returns 400. Neither uploading nor ready sessions appear in discovery.

### Connectivity boundary

`ISessionPresence.RenewAsync` is the trusted boundary for the future EC2 component. It requires the authenticated owner and the **observed UDP endpoint**. Its DynamoDB implementation changes ready sessions to active and renews active sessions to a 90-second expiry. It cannot resurrect expired sessions. There is deliberately no public REST endpoint that marks a host alive.

The current EC2 placeholder **does not call this interface yet**, so this release can create and complete sessions but they will not become discoverable until connectivity is implemented. No UDP tickets, actual join, membership reservation, heartbeats, or NAT introductions are implemented. `/join` currently returns authorized connection details only.

Listings contain the host endpoint, EC2 connectivity endpoint, and short-lived download URLs for the config and chunk. The initial implementation uses a paginated DynamoDB scan with an active/expiry filter. A page may be empty and still have `nextCursor`; continue until the cursor is null. Listing is eventually consistent, so `/join` re-reads the record consistently and checks expiry.

The session row stores its owner, name, state, expiration, observed host IP/port when active, and S3 bucket/prefix. Data is isolated between Alpha/Beta/Prod. Cross-region global-table replication and connectivity routing are future changes, not enabled here.

## Storage cleanup

DynamoDB TTL eventually removes expired records; APIs filter expiry immediately. A separate `MetadataCleanup` Lambda entry point, packaged in the same artifact, handles removed session records and deletes their two published S3 objects. Explicit session deletion uses the same path. Abandoned upload objects expire through an S3 lifecycle rule after one day. Buckets are private, and clients never receive AWS credentials.

## Development and deployment

From `ArterraDiscoveryCDK`, `bash scripts/build-service.sh` builds both the EC2 artifact and this Lambda artifact. See that project's README for the pipeline and DNS setup. The Lambda uses managed .NET 8 with executable handler `Arterra.Game`; the cleanup function uses its separate class/method handler.

For local HTTP development, the same executable uses Kestrel. Supply `UNITY_PROJECT_ID`, `IDENTITY_TABLE`, `LOGIN_TABLE`, `SESSION_TABLE`, `METADATA_BUCKET`, `CONNECTIVITY_HOST`, `CONNECTIVITY_PORT`, and an AWS region/credential profile for your development resources. Do not point local mutation tests at production resources.
