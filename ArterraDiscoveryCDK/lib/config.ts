export const deploymentAccount = '918734736030';
export const pipelineRegion = 'us-east-1';
export const productionRegions = ['us-east-1'] as const;

export const sourceRepository = 'blackmagic919/ArterraResources';
export const sourceBranch = 'main';
export const pipelineName = 'ArterraDiscovery';

export const domainName = 'playarterra.com';
export const serviceDomainName = `api.${domainName}`;
export const hostedZoneExportName = `${pipelineName}-HostedZoneId`;

export const unityProjectId = '4662aa5d-0aa4-4010-9862-bd76e184f4c9';

export const port = 9050;
export const instanceType = 't3.micro';

export const unit = (sessionTable: string, ticketTable: string, region: string) => `[Unit]
Description=Arterra NAT introduction service (LiteNetLib)
Wants=network-online.target
After=network-online.target

[Service]
Type=simple
User=arterra
Group=arterra
WorkingDirectory=/opt/arterra/discovery
ExecStart=/opt/arterra/discovery/Arterra.Discovery
Environment=ARTERRA_UDP_PORT=${port}
Environment=AWS_REGION=${region}
Environment=SESSION_TABLE=${sessionTable}
Environment=TICKET_TABLE=${ticketTable}
Environment=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
Restart=on-failure
RestartSec=5
KillSignal=SIGTERM
TimeoutStopSec=30
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
    `;
