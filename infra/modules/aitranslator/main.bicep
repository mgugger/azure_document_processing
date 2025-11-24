param location string
param aiTranslatorName string

resource aiTranslator 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: aiTranslatorName
  location: location
  kind: 'TextTranslation'
  sku: {
    name: 'S1'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    customSubDomainName: aiTranslatorName
    networkAcls: {
      defaultAction: 'Allow'
    }
    publicNetworkAccess: 'Enabled'
  }
}

output aiTranslatorName string = aiTranslator.name
output resourceId string = aiTranslator.id
output endpoint string = aiTranslator.properties.endpoint
output principalId string = aiTranslator.identity.principalId
