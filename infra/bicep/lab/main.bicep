// One disposable lab: a private Ubuntu 22.04 VM running SQL Server 2022 Developer Edition, in its own resource
// group. Deployed by the worker. SQL Server is installed on first boot by setup-sql.sh (cloud-init).
@description('Lab ID (GUID).')
param labId string

@description('VM/computer name, max 15 characters.')
@maxLength(15)
param vmName string

param location string = resourceGroup().location
param vmSize string = 'Standard_D2s_v7'

@description('v6+ sizes expose NVMe disks only; older sizes use SCSI.')
@allowed(['SCSI', 'NVMe'])
param diskControllerType string = 'NVMe'

@description('Resource ID of the shared lab subnet (no public access).')
param subnetId string

param adminUsername string = 'labadmin'

@secure()
param adminPassword string

@secure()
@description('SQL Server sa password; used once during setup, after which sa is disabled.')
param saPassword string

@description('SQL authentication login used by the worker for health checks and experiments.')
param sqlLogin string = 'labworker'

@secure()
param sqlPassword string

@description('Tags required on every lab resource: labId, ownerId, environment, createdAt, expiresAt.')
param tags object

resource nic 'Microsoft.Network/networkInterfaces@2024-05-01' = {
  name: 'nic-${vmName}'
  location: location
  tags: tags
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          subnet: { id: subnetId }
        }
      }
    ]
  }
}

resource vm 'Microsoft.Compute/virtualMachines@2024-07-01' = {
  name: vmName
  location: location
  tags: tags
  properties: {
    hardwareProfile: { vmSize: vmSize }
    securityProfile: {
      securityType: 'TrustedLaunch'
      uefiSettings: { secureBootEnabled: true, vTpmEnabled: true }
    }
    storageProfile: {
      diskControllerType: diskControllerType
      imageReference: {
        publisher: 'Canonical'
        offer: '0001-com-ubuntu-server-jammy'
        sku: '22_04-lts-gen2'
        version: 'latest'
      }
      osDisk: {
        name: 'osdisk-${vmName}'
        createOption: 'FromImage'
        deleteOption: 'Delete'
        diskSizeGB: 64
        managedDisk: { storageAccountType: 'StandardSSD_LRS' }
      }
    }
    osProfile: {
      computerName: vmName
      adminUsername: adminUsername
      adminPassword: adminPassword
      customData: base64(replace(replace(replace(loadTextContent('setup-sql.sh'), '__SA_PASSWORD__', saPassword), '__SQL_LOGIN__', sqlLogin), '__SQL_PASSWORD__', sqlPassword))
      linuxConfiguration: {
        // SSH is unreachable (no public IP; the lab NSG denies 22). The password is kept in Key Vault for break-glass use.
        disablePasswordAuthentication: false
        provisionVMAgent: true
        // Patching is explicit and approval-gated (Milestone 9); no second automatic mechanism.
        patchSettings: { patchMode: 'ImageDefault', assessmentMode: 'ImageDefault' }
      }
    }
    networkProfile: {
      networkInterfaces: [{ id: nic.id, properties: { deleteOption: 'Delete' } }]
    }
    diagnosticsProfile: { bootDiagnostics: { enabled: true } }
  }
}

output labId string = labId
output vmResourceId string = vm.id
output privateIp string = nic.properties.ipConfigurations[0].properties.privateIPAddress
