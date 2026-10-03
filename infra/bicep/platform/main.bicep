// Shared platform for one environment: resource group, core resources, and the lab-operator role.
// The budget alert is created by deploy.sh (not supported on every subscription offer).
targetScope = 'subscription'

@description('Environment name (lowercase letters/digits, 2-8 chars).')
@minLength(2)
@maxLength(8)
param env string = 'dev'

param location string = 'eastus2'

@description('Azure SQL region; defaults to location. deploy.sh picks another region when SQL provisioning is restricted.')
param sqlLocation string = location

var tags = {
  app: 'sql-server-lab'
  environment: env
  component: 'platform'
}
var suffix = take(uniqueString(subscription().id, env), 6)

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-sqllab-platform-${env}'
  location: location
  tags: tags
}

module core '../modules/core.bicep' = {
  scope: rg
  name: 'core'
  params: {
    env: env
    location: location
    sqlLocation: sqlLocation
    tags: tags
    suffix: suffix
  }
}

// Least privilege for the worker: only what creating, starting, deallocating, and deleting a lab requires.
// Resource-group naming (rg-sqllab-*) and required tags are enforced server-side before any call.
resource labOperator 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: guid(subscription().id, 'sqllab-lab-operator', env)
  properties: {
    roleName: 'SQL Lab Operator (${env})'
    description: 'Creates, starts, deallocates, and deletes SQL Server lab VMs in rg-sqllab-* resource groups.'
    type: 'CustomRole'
    assignableScopes: [subscription().id]
    permissions: [
      {
        actions: [
          'Microsoft.Resources/subscriptions/resourceGroups/read'
          'Microsoft.Resources/subscriptions/resourceGroups/write'
          'Microsoft.Resources/subscriptions/resourceGroups/delete'
          'Microsoft.Resources/subscriptions/resourceGroups/resources/read'
          'Microsoft.Resources/deployments/*'
          'Microsoft.Resources/tags/*'
          'Microsoft.Compute/virtualMachines/*'
          'Microsoft.Compute/disks/*'
          'Microsoft.Network/networkInterfaces/*'
          'Microsoft.Network/virtualNetworks/read'
          'Microsoft.Network/virtualNetworks/subnets/read'
          'Microsoft.Network/virtualNetworks/subnets/join/action'
          'Microsoft.Network/networkSecurityGroups/join/action'
        ]
        notActions: []
      }
    ]
  }
}

resource labOperatorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, 'sqllab-lab-operator-assignment', env)
  properties: {
    principalId: core.outputs.appIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: labOperator.id
  }
}

output resourceGroupName string = rg.name
output acrName string = core.outputs.acrName
output acrLoginServer string = core.outputs.acrLoginServer
output keyVaultName string = core.outputs.keyVaultName
output keyVaultUri string = core.outputs.keyVaultUri
output appIdentityId string = core.outputs.appIdentityId
output appIdentityName string = core.outputs.appIdentityName
output appIdentityClientId string = core.outputs.appIdentityClientId
output migratorIdentityId string = core.outputs.migratorIdentityId
output migratorIdentityClientId string = core.outputs.migratorIdentityClientId
output sqlServerFqdn string = core.outputs.sqlServerFqdn
output sqlDatabaseName string = core.outputs.sqlDatabaseName
output containerAppsEnvironmentId string = core.outputs.containerAppsEnvironmentId
output containerAppsDefaultDomain string = core.outputs.containerAppsDefaultDomain
output labSubnetId string = core.outputs.labSubnetId
output appInsightsConnectionString string = core.outputs.appInsightsConnectionString
