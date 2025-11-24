param location string
param documentIntelligenceName string

resource documentIntelligence 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: documentIntelligenceName
  location: location
  kind: 'FormRecognizer'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    customSubDomainName: documentIntelligenceName
    apiProperties: {
      qnaRuntimeEndpoint: ''
    }
    networkAcls: {
      defaultAction: 'Allow'
    }
    publicNetworkAccess: 'Enabled'
  }
}

output documentIntelligenceName string = documentIntelligence.name
output resourceId string = documentIntelligence.id
output endpoint string = documentIntelligence.properties.endpoint
output principalId string = documentIntelligence.identity.principalId
