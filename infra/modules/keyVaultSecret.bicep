// Writes one secret into an *existing* Key Vault. Used only for the new
// `Redis--ConnectionString` secret this Del introduces (see main.bicep) —
// deliberately NOT reused to write `Jwt--SigningKey` or
// `ConnectionStrings--AtlasDb`, which already exist from Del 8/9 and stay
// exactly as they are; re-declaring either of those here would let a
// stray parameter default silently overwrite a real secret on the next
// deployment, which is a risk this module avoids by only ever being
// called once, for the one secret Del 19 actually adds.

@description('Name of the existing Key Vault.')
param keyVaultName string

@description('Secret name. Key Vault secret names may only contain letters, digits and hyphens — use \'--\' where the app config key has a \':\' (see docs/AZURE_DEPLOYMENT.md section 3).')
param secretName string

@description('Secret value.')
@secure()
param secretValue string

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource secret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: secretName
  properties: {
    value: secretValue
  }
}
