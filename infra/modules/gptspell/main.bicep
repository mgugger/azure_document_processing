param location string = resourceGroup().location
param accountName string = 'gptspell${uniqueString(resourceGroup().id)}'
param deploymentName string = 'gpt5-mini'
param modelName string = 'gpt-4o-mini'
param modelVersion string = '2024-07-18'
param modelFormat string = 'OpenAI'
param skuCapacity int = 2

resource openai 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: accountName
  location: location
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    publicNetworkAccess: 'Enabled'
    customSubDomainName: accountName
  }
}

resource spellDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  name: deploymentName
  parent: openai
  properties: {
    model: {
      name: modelName
      format: modelFormat
      version: modelVersion
    }
  }
  sku: {
    name: 'GlobalStandard'
    capacity: skuCapacity
  }
}

output endpoint string = openai.properties.endpoint
output deploymentName string = '${openai.name}/${spellDeployment.name}'
output accountName string = openai.name
