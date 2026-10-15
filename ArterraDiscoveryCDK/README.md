# Arterra discovery infrastructure

CDK template for account `918734736030`. The pipeline lives in `us-east-1` and watches every push to `blackmagic919/ArterraResources`, branch `main`.

| Deployment | Regions | Promotion |
| --- | --- | --- |
| Alpha | us-east-1 | Automatic |
| Beta | us-east-1 | Automatic after Alpha |
| Prod wave | us-east-1 | One manual approval after Beta |

Each deployment creates a public-subnet VPC, one `t3.micro` EC2 instance, an Elastic IP, and UDP ingress on port `9050`. There are three instances in total. There are no NAT gateways or SSH ingress rules; administration uses Systems Manager Session Manager.

CDK constants and the systemd unit are defined in `lib/config.ts`. The .NET source lives in the sibling `../ArterraDiscoveryService/` directory. Build scripts, the release lock, downloaded dependency cache, and deployment artifacts remain under `ArterraDiscoveryCDK/`.

The sibling `../ArterraGameService/` contains the Lambda HTTP/auth/session service. Each environment has an API Gateway HTTP API, an ACM certificate, a management Lambda, a metadata-cleanup Lambda, four DynamoDB tables, and a private S3 metadata bucket. The API Lambda is outside the VPC and requires no NAT gateway. See [the management API](../ArterraGameService/README.md) for requests, defaults, and the connectivity boundary.

## Service scope

The background .NET service authenticates single-use hosting/joining tickets, tracks connected hosts, renews session presence in DynamoDB, and introduces peers using LiteNetLib for direct game connections. No relay fallback is implemented. Auth, session management, ticket issuance, discovery queries, and metadata access run separately in the Game Service Lambda. See [the UDP protocol](../ArterraDiscoveryService/README.md) for client integration. Unity integration and real NAT playtesting remain to be done. Global-table replication is not configured for this single-region deployment.

`litenetlib-release.json` pins release `2.1.4`, its GitHub release ID, and the source archive SHA-256. The build downloads that tag's archive directly from GitHub's codeload endpoint and verifies its SHA-256 before extracting it into ignored `.cache/` storage. Normal builds do not call the release API or need a GitHub token. The release ID records which release was reviewed; the archive checksum enforces the pinned content. No upstream source is committed. To change versions, query GitHub's release API for the desired tag, verify that it is a stable published release, and update all lock fields together after reviewing and hashing its archive.

The Linux x64 self-contained .NET 8 application is built once in the pipeline synth step and published as a CDK asset for all stages. EC2 runs it with `systemd` as an unprivileged user. CDK's bootstrap and pipeline artifact buckets are infrastructure storage, independent of the session metadata bucket.

## Initial setup

Prerequisites: Node.js 22, npm, .NET SDK 8 or newer with .NET 8 targeting support, Python 3, AWS CLI credentials for the target account, and permission to authorize a GitHub connection. The repository must retain the sibling `ArterraDiscoveryCDK/`, `ArterraDiscoveryService/`, and `ArterraGameService/` directories because the pipeline builds from that subdirectory.

From this directory:

```sh
npm ci
bash scripts/build-service.sh
npm run build
npx cdk synth
```

Bootstrap the shared account/region environment once using your normal AWS credentials/profile:

```sh
npx cdk bootstrap aws://918734736030/us-east-1
npx cdk deploy ArterraDiscoveryCdkStack
```

By default, the stack creates a GitHub CodeConnections connection. CloudFormation-created connections initially require authorization: open the AWS Developer Tools **Connections** console in `us-east-1`, select `arterra-resources`, and complete its GitHub authorization with access to `blackmagic919/ArterraResources`. The stack outputs the connection ARN. The first pipeline execution may fail until this step is complete; release a new pipeline change after authorization.

Alternatively, put an existing authorized connection ARN in the `context` object of `cdk.json` under `githubConnectionArn`. Commit this setting so both local deployment and the pipeline's self-mutation synth use the same connection. Do not configure it only through a temporary local `-c` argument.

Commit the template and `package-lock.json` to `main`. Subsequent pushes trigger the self-updating pipeline. Alpha and Beta proceed automatically when deployment succeeds; Prod waits for approval. Deployment success checks that the process stays active through its startup check; it does not replace a deployed multiplayer connectivity test.

## DNS setup

The pipeline stack creates a retained public hosted zone for `playarterra.com`. Each Game Service deployment manages an alias A record pointing to API Gateway:

| Deployment | Hostname |
| --- | --- |
| Alpha | `alpha.api.playarterra.com` |
| Beta | `beta.api.playarterra.com` |
| Prod | `api.playarterra.com` |

After the pipeline stack creates the hosted zone, copy the four nameservers from its `NameServers` output into the domain's nameserver settings at GoDaddy. Keep registration at GoDaddy. If the domain already serves other records (such as email), copy those records into this hosted zone before switching nameservers. Do this before the first Game Service deployment completes: ACM DNS validation waits for these authoritative nameservers. Prod's API alias is created after its approval and deployment.

The service stacks import the shared zone ID from the pipeline stack in `us-east-1`. Adding regions later will also require revisiting zone-ID sharing and regional routing. ACM provides HTTPS certificates for API Gateway; Caddy is not installed. GameService owns the Elastic IP and EC2 attaches it for UDP; the management API returns it as the connectivity endpoint. The EC2 stack now depends on GameService so its tables and instance permissions are available before startup. Moving the address from the old scaffold replaces its IP if already deployed. No API hostname points to EC2.

## Operations

Each Game Service stack outputs `ApiUrl` and `DefaultApiUrl`. Lambda logs are in CloudWatch with one-week retention. The cleanup function logs separately. Tables and metadata buckets are retained on stack deletion. The pipeline builds two independent artifacts; EC2 runs only the connectivity service, with session GetItem/UpdateItem and ticket DeleteItem permissions. It cannot read login tokens, identities, or metadata.

Each service stack outputs `PublicIp`, `UdpPort`, and `InstanceId`. Connect through Session Manager and inspect:

```sh
sudo systemctl status arterra-discovery
sudo journalctl -u arterra-discovery -f
sudo cat /var/log/cfn-init.log
sudo cat /var/log/cfn-init-cmd.log
```

Application asset changes replace the instance and reassign its Elastic IP. With one instance per deployment, updates can cause a brief interruption. There is no cross-region application state or routing yet. EC2, public IPv4 addresses, build, pipeline, and storage resources incur normal AWS charges once deployed.

The template itself does not deploy anything until you run the deployment commands.
