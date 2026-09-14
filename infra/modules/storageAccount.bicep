// Blob Storage account for ticket attachments (Del 10). Settings mirror
// docs/AZURE_DEPLOYMENT.md section 8.1 exactly: Standard_LRS (cheapest,
// no geo-replication needed for test-file attachments), StorageV2,
// public blob access disabled at the account level so the container's own
// PublicAccessType.None (set in code, see DependencyInjection.cs) can't be
// undermined by an account-level setting, and TLS 1.2 minimum.
//
// The `attachments` container itself is deliberately NOT created here —
// AddInfrastructure already creates it on startup (CreateIfNotExists), the
// same "the app owns its own schema/resources" idea `dotnet ef database
// update` follows for SQL tables. Provisioning it twice would just be two
// sources of truth for one container.

@description('Globally unique storage account name (lowercase letters/digits only, 3-24 chars).')
param name string

@description('Azure region for the storage account.')
param location string

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: name
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

output name string = storageAccount.name
output id string = storageAccount.id
output blobEndpoint string = storageAccount.properties.primaryEndpoints.blob
