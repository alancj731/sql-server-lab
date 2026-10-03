// Shared platform resources for one environment. Nothing here is ever inside a disposable lab resource group.
@description('Environment name, e.g. dev.')
param env string
param location string

@description('Region for the Azure SQL server. May differ from location when SQL provisioning is restricted there; the private endpoint stays in the local VNet.')
param sqlLocation string = location

param tags object

@description('Short unique suffix for globally unique names.')
param suffix string

var names = {
  law: 'log-sqllab-${env}'
  appi: 'appi-sqllab-${env}'
  vnet: 'vnet-sqllab-${env}'
  nsgLabs: 'nsg-sqllab-labs-${env}'
  kv: 'kv-sqllab-${env}-${suffix}'
  storage: 'stsqllab${env}${suffix}'
  acr: 'crsqllab${env}${suffix}'
  idApp: 'id-sqllab-app-${env}'
  idMigrator: 'id-sqllab-migrator-${env}'
  sql: 'sql-sqllab-${env}-${suffix}'
  db: 'sqldb-controlplane'
  pe: 'pe-sqllab-sql-${env}'
  cae: 'cae-sqllab-${env}'
}

var appsPrefix = '10.40.0.0/23'

// Built-in role definition IDs.
var roles = {
  keyVaultSecretsOfficer: 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
  acrPull: '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  storageBlobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
}

resource law 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: names.law
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appi 'Microsoft.Insights/components@2020-02-02' = {
  name: names.appi
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: law.id
  }
}

// Lab subnet: SQL (1433) only from the application subnet; no RDP/WinRM; no inbound internet.
resource nsgLabs 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: names.nsgLabs
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'allow-sql-from-apps'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: appsPrefix
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '1433'
        }
      }
      {
        name: 'deny-remote-admin'
        properties: {
          priority: 200
          direction: 'Inbound'
          access: 'Deny'
          protocol: 'Tcp'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRanges: ['22', '3389', '5985', '5986']
        }
      }
      {
        name: 'deny-sql-from-elsewhere'
        properties: {
          priority: 210
          direction: 'Inbound'
          access: 'Deny'
          protocol: 'Tcp'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '1433'
        }
      }
      {
        name: 'deny-internet-inbound'
        properties: {
          priority: 4000
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourceAddressPrefix: 'Internet'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '*'
        }
      }
    ]
  }
}

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: names.vnet
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: ['10.40.0.0/16'] }
    subnets: [
      {
        name: 'snet-apps'
        properties: {
          addressPrefix: appsPrefix
          delegations: [{ name: 'apps', properties: { serviceName: 'Microsoft.App/environments' } }]
        }
      }
      {
        name: 'snet-private-endpoints'
        properties: {
          addressPrefix: '10.40.2.0/27'
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
      {
        name: 'snet-labs'
        properties: {
          addressPrefix: '10.40.4.0/24'
          networkSecurityGroup: { id: nsgLabs.id }
          // Lab VMs have no public IP but need outbound access for the SQL IaaS Agent extension.
          defaultOutboundAccess: true
        }
      }
    ]
  }
}

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: names.kv
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: names.storage
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource blob 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource backups 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blob
  name: 'backups'
  properties: { publicAccess: 'None' }
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: names.acr
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false }
}

resource idApp 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: names.idApp
  location: location
  tags: tags
}

resource idMigrator 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: names.idMigrator
  location: location
  tags: tags
}

resource appKvSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: kv
  name: guid(kv.id, idApp.id, roles.keyVaultSecretsOfficer)
  properties: {
    principalId: idApp.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsOfficer)
  }
}

resource appBlob 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, idApp.id, roles.storageBlobDataContributor)
  properties: {
    principalId: idApp.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataContributor)
  }
}

resource appAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr
  name: guid(acr.id, idApp.id, roles.acrPull)
  properties: {
    principalId: idApp.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull)
  }
}

resource migratorAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr
  name: guid(acr.id, idMigrator.id, roles.acrPull)
  properties: {
    principalId: idMigrator.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull)
  }
}

// Control database: Entra-only authentication, no public network access. The migrator identity is the Entra admin;
// the app identity is created as a database user with limited roles by the migration job.
resource sql 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: names.sql
  location: sqlLocation
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: idMigrator.name
      sid: idMigrator.properties.principalId
      principalType: 'Application'
      tenantId: tenant().tenantId
    }
  }
}

resource db 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sql
  name: names.db
  location: sqlLocation
  tags: tags
  sku: { name: 'Basic', tier: 'Basic' }
  properties: {
    maxSizeBytes: 2147483648
    requestedBackupStorageRedundancy: 'Local'
  }
}

resource sqlDns 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  #disable-next-line no-hardcoded-env-urls
  name: 'privatelink${environment().suffixes.sqlServerHostname}'
  location: 'global'
  tags: tags
}

resource sqlDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: sqlDns
  name: 'link-${names.vnet}'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: vnet.id }
  }
}

resource sqlPe 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: names.pe
  location: location
  tags: tags
  properties: {
    subnet: { id: '${vnet.id}/subnets/snet-private-endpoints' }
    privateLinkServiceConnections: [
      {
        name: 'sql'
        properties: {
          privateLinkServiceId: sql.id
          groupIds: ['sqlServer']
        }
      }
    ]
  }
}

resource sqlPeDns 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: sqlPe
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [{ name: 'sql', properties: { privateDnsZoneId: sqlDns.id } }]
  }
}

resource cae 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: names.cae
  location: location
  tags: tags
  properties: {
    vnetConfiguration: {
      infrastructureSubnetId: '${vnet.id}/subnets/snet-apps'
      internal: false
    }
    workloadProfiles: [{ name: 'Consumption', workloadProfileType: 'Consumption' }]
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: law.properties.customerId
        sharedKey: law.listKeys().primarySharedKey
      }
    }
  }
}

output acrName string = acr.name
output acrLoginServer string = acr.properties.loginServer
output keyVaultName string = kv.name
output keyVaultUri string = kv.properties.vaultUri
output storageAccountName string = storage.name
output appIdentityId string = idApp.id
output appIdentityName string = idApp.name
output appIdentityClientId string = idApp.properties.clientId
output appIdentityPrincipalId string = idApp.properties.principalId
output migratorIdentityId string = idMigrator.id
output migratorIdentityClientId string = idMigrator.properties.clientId
output sqlServerFqdn string = sql.properties.fullyQualifiedDomainName
output sqlDatabaseName string = db.name
output containerAppsEnvironmentId string = cae.id
output containerAppsDefaultDomain string = cae.properties.defaultDomain
output labSubnetId string = '${vnet.id}/subnets/snet-labs'
output appInsightsConnectionString string = appi.properties.ConnectionString
