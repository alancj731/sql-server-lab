// Container Apps Job that applies control-database migrations from inside the VNet as the migrator identity
// (the SQL Entra admin), then grants the app identity a least-privileged database user.
param env string
param location string
param containerAppsEnvironmentId string
param acrLoginServer string
param workerImage string
param migratorIdentityId string
param migratorIdentityClientId string
param appIdentityName string
param appIdentityClientId string
param sqlServerFqdn string
param sqlDatabaseName string

resource job 'Microsoft.App/jobs@2024-03-01' = {
  name: 'caj-sqllab-migrate-${env}'
  location: location
  tags: { app: 'sql-server-lab', environment: env, component: 'migrate' }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${migratorIdentityId}': {} }
  }
  properties: {
    environmentId: containerAppsEnvironmentId
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 900
      replicaRetryLimit: 1
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      registries: [{ server: acrLoginServer, identity: migratorIdentityId }]
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: workerImage
          args: ['--migrate']
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
            { name: 'ControlDb__Provider', value: 'SqlServer' }
            {
              name: 'ControlDb__ConnectionString'
              value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${migratorIdentityClientId};Encrypt=True;Connect Timeout=60'
            }
            { name: 'Migrate__AppIdentityName', value: appIdentityName }
            { name: 'Migrate__AppIdentityClientId', value: appIdentityClientId }
          ]
        }
      ]
    }
  }
}

output jobName string = job.name
