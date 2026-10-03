// API (serves the SPA, REST, SignalR) and worker container apps. No secrets are passed: both authenticate with
// the app managed identity; Entra values are public identifiers.
param env string
param location string
param containerAppsEnvironmentId string
param acrLoginServer string
param apiImage string
param workerImage string
param appIdentityId string
param appIdentityClientId string
param sqlServerFqdn string
param sqlDatabaseName string
param keyVaultUri string
param labSubnetId string
param vmSize string
param tenantId string
param apiClientId string
param spaClientId string
param maxActiveLabsPerUser int = 2

@secure()
@description('Application Insights connection string (contains an ingestion key).')
param appInsightsConnectionString string

var tags = { app: 'sql-server-lab', environment: env }

var shared = [
  { name: 'ControlDb__Provider', value: 'SqlServer' }
  {
    name: 'ControlDb__ConnectionString'
    value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${appIdentityClientId};Encrypt=True;Connect Timeout=60'
  }
  { name: 'Infrastructure__Mode', value: 'Azure' }
  { name: 'Azure__SubscriptionId', value: subscription().subscriptionId }
  { name: 'Azure__Location', value: location }
  { name: 'Azure__Environment', value: env }
  { name: 'Azure__LabSubnetId', value: labSubnetId }
  { name: 'Azure__VmSize', value: vmSize }
  { name: 'Azure__KeyVaultUri', value: keyVaultUri }
  { name: 'Azure__ManagedIdentityClientId', value: appIdentityClientId }
  { name: 'AZURE_CLIENT_ID', value: appIdentityClientId }
  { name: 'LabLimits__MaxActiveLabsPerUser', value: string(maxActiveLabsPerUser) }
  // Lab VMs join the shared regional VNet, so this deployment offers only its own region.
  { name: 'LabLimits__AllowedRegions__0', value: location }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'appi-connection' }
]

var identity = {
  type: 'UserAssigned'
  userAssignedIdentities: { '${appIdentityId}': {} }
}

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-sqllab-api-${env}'
  location: location
  tags: union(tags, { component: 'api' })
  identity: identity
  properties: {
    environmentId: containerAppsEnvironmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        // SignalR negotiates and then reconnects to the same replica.
        stickySessions: { affinity: 'sticky' }
      }
      registries: [{ server: acrLoginServer, identity: appIdentityId }]
      secrets: [{ name: 'appi-connection', value: appInsightsConnectionString }]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(shared, [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'Authentication__Mode', value: 'Entra' }
            { name: 'Authentication__TenantId', value: tenantId }
            { name: 'Authentication__Authority', value: '${environment().authentication.loginEndpoint}${tenantId}/v2.0' }
            { name: 'Authentication__Audience', value: apiClientId }
            { name: 'Authentication__SpaClientId', value: spaClientId }
            { name: 'Authentication__ApiScope', value: 'api://${apiClientId}/access_as_user' }
          ])
          probes: [
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 30 }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 }, periodSeconds: 15, failureThreshold: 6 }
          ]
        }
      ]
      // Lean profile: scale to zero when idle (a few seconds of cold start).
      scale: {
        minReplicas: 0
        maxReplicas: 2
        rules: [{ name: 'http', http: { metadata: { concurrentRequests: '50' } } }]
      }
    }
  }
}

resource worker 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-sqllab-worker-${env}'
  location: location
  tags: union(tags, { component: 'worker' })
  identity: identity
  properties: {
    environmentId: containerAppsEnvironmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      registries: [{ server: acrLoginServer, identity: appIdentityId }]
      secrets: [{ name: 'appi-connection', value: appInsightsConnectionString }]
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: workerImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: concat(shared, [{ name: 'DOTNET_ENVIRONMENT', value: 'Production' }])
        }
      ]
      // Exactly one replica: it must stay on to expire labs and resume jobs.
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

output apiFqdn string = api.properties.configuration.ingress.fqdn
output workerName string = worker.name
