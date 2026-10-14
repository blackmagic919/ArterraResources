import * as cdk from 'aws-cdk-lib';
import * as codebuild from 'aws-cdk-lib/aws-codebuild';
import * as connections from 'aws-cdk-lib/aws-codestarconnections';
import * as route53 from 'aws-cdk-lib/aws-route53';
import * as pipelines from 'aws-cdk-lib/pipelines';
import { Construct } from 'constructs';
import { DiscoveryStage } from './discovery-stage';

import { deploymentAccount, pipelineRegion, productionRegions, sourceRepository, sourceBranch, pipelineName, domainName, serviceDomainName, hostedZoneExportName } from './config';

export class ArterraDiscoveryCdkStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const zone = new route53.PublicHostedZone(this, 'HostedZone', { zoneName: domainName });
    zone.applyRemovalPolicy(cdk.RemovalPolicy.RETAIN);

    // Service stages import the ID after this pipeline stack creates the shared zone.
    new cdk.CfnOutput(this, 'HostedZoneId', {
      value: zone.hostedZoneId,
      exportName: hostedZoneExportName,
    });
    new cdk.CfnOutput(this, 'NameServers', {
      value: cdk.Fn.join(', ', zone.hostedZoneNameServers!),
    });

    // An existing authorized connection can be configured in cdk.json context.
    const configuredConnection: string | undefined = this.node.tryGetContext('githubConnectionArn');
    const connectionArn = configuredConnection ?? new connections.CfnConnection(this, 'GitHubConnection', {
      connectionName: 'arterra-resources',
      providerType: 'GitHub',
    }).attrConnectionArn;

    const pipeline = new pipelines.CodePipeline(this, 'Pipeline', {
      pipelineName,
      selfMutation: true,
      synth: new pipelines.CodeBuildStep('BuildAndSynth', {
        input: pipelines.CodePipelineSource.connection(sourceRepository, sourceBranch, {
          connectionArn,
          triggerOnPush: true,
        }),
        buildEnvironment: { buildImage: codebuild.LinuxBuildImage.STANDARD_7_0 },
        partialBuildSpec: codebuild.BuildSpec.fromObject({
          phases: { install: { 'runtime-versions': { nodejs: 22, dotnet: '8.0' } } },
        }),
        commands: [
          'cd ArterraDiscoveryCDK',
          'npm ci',
          'npm run build',
          'bash scripts/build-service.sh',
          'npx cdk synth',
        ],
        primaryOutputDirectory: 'ArterraDiscoveryCDK/cdk.out',
      }),
    });

    pipeline.addStage(new DiscoveryStage(this, 'Alpha', {
      recordName: `alpha.${serviceDomainName}`,
      env: { account: deploymentAccount, region: pipelineRegion },
    }));

    pipeline.addStage(new DiscoveryStage(this, 'Beta', {
      recordName: `beta.${serviceDomainName}`,
      env: { account: deploymentAccount, region: pipelineRegion },
    }));

    const production = pipeline.addWave('Prod', {
      pre: [new pipelines.ManualApprovalStep('ApproveProduction', {
        comment: 'Approve the alpha/beta artifact for deployment to production.',
      })],
    });

    for (const region of productionRegions) {
      production.addStage(new DiscoveryStage(this, `Prod-${region}`, {
        recordName: serviceDomainName,
        env: { account: deploymentAccount, region },
      }));
    }

    new cdk.CfnOutput(this, 'GitHubConnectionArn', { value: connectionArn });
  }
}
