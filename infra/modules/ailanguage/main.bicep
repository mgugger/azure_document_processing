param location string
param aiLanguageName string

resource aiLanguage 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: aiLanguageName
  location: location
  kind: 'TextAnalytics'
  sku: {
    name: 'F0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    customSubDomainName: aiLanguageName
    networkAcls: {
      defaultAction: 'Allow'
    }
    publicNetworkAccess: 'Enabled'
  }
}

output aiLanguageName string = aiLanguage.name
output resourceId string = aiLanguage.id
output endpoint string = 'https://${aiLanguage.properties.customSubDomainName}.cognitiveservices.azure.com/'
output principalId string = aiLanguage.identity.principalId
