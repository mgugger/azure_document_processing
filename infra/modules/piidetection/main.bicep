param location string
param piiDetectionName string

resource piiDetection 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: piiDetectionName
  location: location
  kind: 'TextAnalytics'
  sku: {
    name: 'S'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    customSubDomainName: piiDetectionName
    apiProperties: {
      //qnaRuntimeEndpoint: ''
    }
    networkAcls: {
      defaultAction: 'Allow'
    }
    publicNetworkAccess: 'Enabled'
  }
}

output piiDetectionName string = piiDetection.name
output resourceId string = piiDetection.id
output endpoint string = piiDetection.properties.endpoint
output principalId string = piiDetection.identity.principalId
