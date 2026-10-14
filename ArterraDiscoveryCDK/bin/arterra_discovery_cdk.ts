#!/usr/bin/env node
import * as cdk from 'aws-cdk-lib';
import { ArterraDiscoveryCdkStack } from '../lib/arterra_discovery_cdk-stack';

import { deploymentAccount, pipelineRegion } from '../lib/config';

const app = new cdk.App();
new ArterraDiscoveryCdkStack(app, 'ArterraDiscoveryCdkStack', {
  env: { account: deploymentAccount, region: pipelineRegion },
});
