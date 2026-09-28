// Grants a principal (here: the Web App's system-assigned managed
// identity) read access to secrets in an *existing* Key Vault — the exact
// role docs/AZURE_DEPLOYMENT.md section 3 already assigned by hand for
// Del 8 ("Key Vault Secrets User", RBAC model), just declared instead of
// run as `az role assignment create`.
//
// The vault itself is declared `existing`, never created here (it's
// Peter's own pre-existing vault, shared with resources outside this
// project) — deployed scoped to whichever resource group it actually
// lives in (see the `scope:` on this module's call in main.bicep).
//
// This module only covers the RBAC model. If your vault still uses the
// older access-policy model (`az keyvault show --query
// properties.enableRbacAuthorization` returns "false" — see section 3),
// use `az keyvault set-policy` by hand instead, same as the manual
// fallback docs/AZURE_DEPLOYMENT.md already documents; Bicep's access
// policy syntax for `existing` vaults needs the *whole* policy list
// replaced in one call, which risks clobbering unrelated policies other
// resources in the vault already depend on — a real risk for a shared
// vault, and exactly the kind of blast radius this module avoids by only
// supporting the additive, per-principal RBAC model.

@description('Name of the existing Key Vault.')
param keyVaultName string

@description('principalId of the identity to grant access to (e.g. the Web App\'s managed identity).')
param principalId string

// Built-in role definition GUID for "Key Vault Secrets User" — stable
// across all Azure tenants (https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/security#key-vault-secrets-user).
// Worth a quick cross-check against that page before a real deployment,
// same "verify rather than trust a remembered value" habit as the NuGet
// package versions in docs/AZURE_DEPLOYMENT.md section 8.
var keyVaultSecretsUserRoleId = '4633458b-denna-ska-bytas-ut'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, principalId, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
