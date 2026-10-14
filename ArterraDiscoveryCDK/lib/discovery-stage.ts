import * as cdk from 'aws-cdk-lib';
import * as ec2 from 'aws-cdk-lib/aws-ec2';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as assets from 'aws-cdk-lib/aws-s3-assets';
import { GameServiceStack } from './game-service-stack';
import { Construct } from 'constructs';
import * as path from 'node:path';
import { port, instanceType, unit } from './config';

interface DiscoveryStageProps extends cdk.StageProps {
  recordName: string;
}

export class DiscoveryStage extends cdk.Stage {
  constructor(scope: Construct, id: string, props: DiscoveryStageProps) {
    super(scope, id, props);
    const connectivity = new DiscoveryStack(this, 'Service');
    new GameServiceStack(this, 'GameService', props.recordName, connectivity.publicAddress);
  }
}

class DiscoveryStack extends cdk.Stack {
  readonly publicAddress: string;

  constructor(scope: Construct, id: string) {
    super(scope, id);

    const vpc = new ec2.Vpc(this, 'Vpc', {
      maxAzs: 1,
      natGateways: 0,
      subnetConfiguration: [{ name: 'Public', subnetType: ec2.SubnetType.PUBLIC }],
    });

    const securityGroup = new ec2.SecurityGroup(this, 'ServiceSecurityGroup', {
      vpc,
      description: 'UDP endpoint for the future introduction protocol',
    });
    securityGroup.addIngressRule(ec2.Peer.anyIpv4(), ec2.Port.udp(port), 'LiteNetLib UDP');

    const artifact = new assets.Asset(this, 'Application', {
      path: path.join(__dirname, '..', '.artifacts', 'service'),
    });

    const instance = new ec2.Instance(this, 'Instance', {
      vpc,
      vpcSubnets: { subnetType: ec2.SubnetType.PUBLIC },
      securityGroup,
      instanceType: new ec2.InstanceType(instanceType),
      machineImage: ec2.MachineImage.latestAmazonLinux2023({ cpuType: ec2.AmazonLinuxCpuType.X86_64 }),
      associatePublicIpAddress: true,
      requireImdsv2: true,
      userDataCausesReplacement: true,
      blockDevices: [{
        deviceName: '/dev/xvda',
        volume: ec2.BlockDeviceVolume.ebs(8, { encrypted: true, volumeType: ec2.EbsDeviceVolumeType.GP3 }),
      }],
      init: ec2.CloudFormationInit.fromElements(
        ec2.InitSource.fromExistingAsset('/opt/arterra/discovery', artifact),
        ec2.InitFile.fromString('/etc/systemd/system/arterra-discovery.service', unit),
        ec2.InitCommand.shellCommand('id arterra >/dev/null 2>&1 || useradd --system --no-create-home --shell /sbin/nologin arterra'),
        ec2.InitCommand.shellCommand('chmod 0755 /opt/arterra/discovery/Arterra.Discovery'),
        ec2.InitCommand.shellCommand('systemctl daemon-reload && systemctl enable --now arterra-discovery.service'),
        ec2.InitCommand.shellCommand('sleep 3 && systemctl is-active --quiet arterra-discovery.service'),
      ),
      initOptions: {
        timeout: cdk.Duration.minutes(10),
        includeUrl: true,
        includeRole: true,
        printLog: true,
        embedFingerprint: true,
      },
    });
    instance.role.addManagedPolicy(iam.ManagedPolicy.fromAwsManagedPolicyName('AmazonSSMManagedInstanceCore'));

    // The instance initially uses its assigned public IP for bootstrap, then receives its stable EIP.
    const address = new ec2.CfnEIP(this, 'PublicAddress', { domain: 'vpc' });
    new ec2.CfnEIPAssociation(this, 'AddressAssociation', {
      allocationId: address.attrAllocationId,
      instanceId: instance.instanceId,
    });

    this.publicAddress = address.ref;

    new cdk.CfnOutput(this, 'PublicIp', { value: address.ref });
    new cdk.CfnOutput(this, 'UdpPort', { value: String(port) });
    new cdk.CfnOutput(this, 'InstanceId', { value: instance.instanceId });
  }
}
