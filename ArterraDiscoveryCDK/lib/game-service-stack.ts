import * as cdk from 'aws-cdk-lib';
import * as ec2 from 'aws-cdk-lib/aws-ec2';
import * as acm from 'aws-cdk-lib/aws-certificatemanager';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as eventsources from 'aws-cdk-lib/aws-lambda-event-sources';
import * as logs from 'aws-cdk-lib/aws-logs';
import * as apigateway from 'aws-cdk-lib/aws-apigatewayv2';
import * as integrations from 'aws-cdk-lib/aws-apigatewayv2-integrations';
import * as route53 from 'aws-cdk-lib/aws-route53';
import * as targets from 'aws-cdk-lib/aws-route53-targets';
import { Construct } from 'constructs';
import * as path from 'node:path';
import { domainName, hostedZoneExportName, port, unityProjectId } from './config';

export class GameServiceStack extends cdk.Stack {
  readonly sessions: dynamodb.Table;
  readonly tickets: dynamodb.Table;
  readonly address: ec2.CfnEIP;

  constructor(scope: Construct, id: string, recordName: string) {
    super(scope, id);

    // Both HTTP and EC2 use this address; placing it here keeps stack dependencies one-way.
    this.address = new ec2.CfnEIP(this, 'PublicAddress', { domain: 'vpc' });

    const identities = new dynamodb.Table(this, 'Identities', {
      partitionKey: { name: 'identityKey', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.RETAIN,
    });
    const logins = new dynamodb.Table(this, 'LoginSessions', {
      partitionKey: { name: 'tokenHash', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      timeToLiveAttribute: 'expiresAt',
      removalPolicy: cdk.RemovalPolicy.RETAIN,
    });
    const sessions = new dynamodb.Table(this, 'ActiveSessions', {
      partitionKey: { name: 'sessionId', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      timeToLiveAttribute: 'expiresAt',
      stream: dynamodb.StreamViewType.OLD_IMAGE,
      removalPolicy: cdk.RemovalPolicy.RETAIN,
    });
    sessions.addGlobalSecondaryIndex({
      indexName: 'SessionsByConnectionCount',
      partitionKey: { name: 'status', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'connectionCount', type: dynamodb.AttributeType.NUMBER },
      projectionType: dynamodb.ProjectionType.ALL,
    });
    this.sessions = sessions;
    this.tickets = new dynamodb.Table(this, 'ConnectionTickets', {
      partitionKey: { name: 'ticketHash', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      timeToLiveAttribute: 'expiresAt',
      removalPolicy: cdk.RemovalPolicy.RETAIN,
    });
    const metadata = new s3.Bucket(this, 'SessionMetadata', {
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      encryption: s3.BucketEncryption.S3_MANAGED,
      enforceSSL: true,
      removalPolicy: cdk.RemovalPolicy.RETAIN,
      lifecycleRules: [{ prefix: 'uploads/', expiration: cdk.Duration.days(1), abortIncompleteMultipartUploadAfter: cdk.Duration.days(1) }],
    });

    const code = lambda.Code.fromAsset(path.join(__dirname, '..', '.artifacts', 'game-service'));
    const httpLogs = new logs.LogGroup(this, 'HttpLogs', { retention: logs.RetentionDays.ONE_WEEK });
    const service = new lambda.Function(this, 'HttpHandler', {
      runtime: lambda.Runtime.DOTNET_8,
      architecture: lambda.Architecture.X86_64,
      handler: 'Arterra.Game',
      code,
      memorySize: 512,
      timeout: cdk.Duration.seconds(30),
      logGroup: httpLogs,
      environment: {
        IDENTITY_TABLE: identities.tableName,
        LOGIN_TABLE: logins.tableName,
        SESSION_TABLE: sessions.tableName,
        TICKET_TABLE: this.tickets.tableName,
        METADATA_BUCKET: metadata.bucketName,
        UNITY_PROJECT_ID: unityProjectId,
        CONNECTIVITY_HOST: this.address.ref,
        CONNECTIVITY_PORT: String(port),
      },
    });
    identities.grantReadWriteData(service);
    logins.grantReadWriteData(service);
    sessions.grantReadWriteData(service);
    this.tickets.grant(service, 'dynamodb:PutItem');
    metadata.grantReadWrite(service);

    // TTL and explicit session deletion both remove the immutable published metadata.
    const cleanupLogs = new logs.LogGroup(this, 'CleanupLogs', { retention: logs.RetentionDays.ONE_WEEK });
    const cleanup = new lambda.Function(this, 'MetadataCleanup', {
      runtime: lambda.Runtime.DOTNET_8,
      architecture: lambda.Architecture.X86_64,
      handler: 'Arterra.Game::Arterra.Game.Storage.MetadataCleanup::HandleAsync',
      code,
      timeout: cdk.Duration.seconds(30),
      memorySize: 256,
      logGroup: cleanupLogs,
      environment: { METADATA_BUCKET: metadata.bucketName },
    });
    metadata.grantDelete(cleanup);
    cleanup.addEventSource(new eventsources.DynamoEventSource(sessions, {
      startingPosition: lambda.StartingPosition.TRIM_HORIZON,
      batchSize: 10,
      bisectBatchOnError: true,
      filters: [lambda.FilterCriteria.filter({ eventName: lambda.FilterRule.isEqual('REMOVE') })],
    }));

    const zone = route53.HostedZone.fromHostedZoneAttributes(this, 'HostedZone', {
      hostedZoneId: cdk.Fn.importValue(hostedZoneExportName),
      zoneName: domainName,
    });
    const certificate = new acm.Certificate(this, 'Certificate', {
      domainName: recordName,
      validation: acm.CertificateValidation.fromDns(zone),
    });
    const domain = new apigateway.DomainName(this, 'ApiDomain', { domainName: recordName, certificate });
    const api = new apigateway.HttpApi(this, 'HttpApi', {
      defaultIntegration: new integrations.HttpLambdaIntegration('Management', service),
      defaultDomainMapping: { domainName: domain },
    });
    new route53.ARecord(this, 'ApiAddress', {
      zone,
      recordName,
      target: route53.RecordTarget.fromAlias(new targets.ApiGatewayv2DomainProperties(
        domain.regionalDomainName, domain.regionalHostedZoneId)),
    });

    new cdk.CfnOutput(this, 'ApiUrl', { value: `https://${recordName}` });
    new cdk.CfnOutput(this, 'DefaultApiUrl', { value: api.apiEndpoint });
  }
}
