// Grants the Web App's managed identity read/write/delete access at the
// blob level on the storage account this Del creates — the role
// docs/AZURE_DEPLOYMENT.md section 8.2 already assigned by hand for
// Del 10, just declared instead of run as `az role assignment create`.
// "Storage Blob Data Contributor", not the account's classic access keys
// — same RBAC-based, keyless pattern as Key Vault (keyVaultRoleAssignment.bicep)
// and Service Bus (serviceBusRoleAssignment.bicep).
//
// Declared as its own module — rather than a role assignment nested
// directly under the `resource` in storageAccount.bicep — only so
// main.bicep can pass in the Web App's principalId, which doesn't exist
// yet at the point storageAccount.bicep itself runs (the Web App and the
// storage account are provisioned in parallel by main.bicep, neither
// depending on the other).

@description('Name of the storage account (created by storageAccount.bicep, same resource group).')
param storageAccountName string

@description('principalId of the identity to grant access to (the Web App\'s managed identity).')
param principalId string

// Built-in role definition GUID for "Storage Blob Data Contributor" —
// stable across all Azure tenants
// (https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/storage#storage-blob-data-contributor).
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, principalId, storageBlobDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
