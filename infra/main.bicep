param aiVisionName string = uniqueString(resourceGroup().id, 'aivision')
param piiDetectionName string = uniqueString(resourceGroup().id, 'piidetection')
param location string = resourceGroup().location
param storageAccountName string = uniqueString(resourceGroup().id, 'storage')
param functionAppName string = uniqueString(resourceGroup().id, 'funcapp')
param documentIntelligenceName string = uniqueString(resourceGroup().id, 'docintelligence')
param appServicePlanName string = '${functionAppName}-plan'
param aiLanguageName string = uniqueString(resourceGroup().id, 'ailanguage')
param aiTranslatorName string = uniqueString(resourceGroup().id, 'aitranslator')
param gptVisionAccountName string = uniqueString(resourceGroup().id, 'gptvision')
param gptVisionDeploymentName string = 'gpt-4-vision'

module ailanguage './modules/ailanguage/main.bicep' = {
  name: 'aiLanguageModule'
  params: {
    location: location
    aiLanguageName: aiLanguageName
  }
}

module aitranslator './modules/aitranslator/main.bicep' = {
  name: 'aiTranslatorModule'
  params: {
    location: location
    aiTranslatorName: aiTranslatorName
  }
}

module storage './modules/storage/main.bicep' = {
  name: 'storageModule'
  params: {
    location: location
    storageAccountName: storageAccountName
  }
}

module appinsights './modules/appinsights/main.bicep' = {
  name: 'appInsightsModule'
  params: {
    location: location
    name: '${functionAppName}-ai'
  }
}

module appserviceplan './modules/appserviceplan/main.bicep' = {
  name: 'appserviceplanModule'
  params: {
    appServicePlanName: appServicePlanName
    location: location
  }
}

module function_inbound_process './modules/function/main.bicep' = {
  name: 'functionInboundModule'
  params: {
    location: location
    functionAppName: '${functionAppName}-inbound-process'
    aiVisionEndpoint: aivision.outputs.endpoint
    aiLanguageEndpoint: ailanguage.outputs.endpoint
    aiTranslatorEndpoint: aitranslator.outputs.endpoint
    storageAccountName: storageAccountName
    appServicePlanId: appserviceplan.outputs.id
    appInsightsInstrumentationKey: appinsights.outputs.instrumentationKey
    documentIntelligenceEndpoint: documentintelligence.outputs.endpoint
    piiDetectionEndpoint: piidetection.outputs.endpoint
    piiDetectionName: piidetection.outputs.piiDetectionName
    appInsightsConnectionString: appinsights.outputs.connectionString
    gptVisionDeploymentName: gptvision.outputs.deploymentName
    gptVisionEndpoint: gptvision.outputs.endpoint
    tags: {
        'azd-service-name': 'function_inbound_process'
    }
  }
}

module documentintelligence './modules/documentintelligence/main.bicep' = {
  name: 'documentIntelligenceModule'
  params: {
    location: location
    documentIntelligenceName: documentIntelligenceName
  }
}

module aivision './modules/aivision/main.bicep' = {
  name: 'aiVisionModule'
  params: {
    location: location
    aiVisionName: aiVisionName
  }
}

module piidetection './modules/piidetection/main.bicep' = {
  name: 'piiDetectionModule'
  params: {
    location: location
    piiDetectionName: piiDetectionName
  }
}

module gptvision './modules/gptvision/main.bicep' = {
  name: 'gptVisionModule'
  params: {
    location: location
    accountName: gptVisionAccountName
    deploymentName: gptVisionDeploymentName
  }
}

param logicAppName string = uniqueString(resourceGroup().id, 'logicapp')
module logicapp './modules/logicapp/main.bicep' = {
  name: 'logicAppModule'
  params: {
    location: location
    logicAppName: logicAppName
  }
}

// Azure AI Search module
param aiSearchServiceName string = uniqueString(resourceGroup().id, 'aisearch')
module aisearch './modules/aisearch/main.bicep' = {
  name: 'aiSearchModule'
  params: {
    location: location
    searchServiceName: aiSearchServiceName
    storageAccountName: storage.outputs.storageAccountName
    outputContainerName: 'output'
  }
}

output logicAppName string = logicapp.outputs.logicAppName
output logicAppId string = logicapp.outputs.logicAppId
output logicAppPrincipalId string = logicapp.outputs.principalId
output piiDetectionName string = piidetection.outputs.piiDetectionName
output piiDetectionPrincipalId string = piidetection.outputs.principalId
output piiDetectionEndpoint string = piidetection.outputs.endpoint
output storageAccountName string = storage.outputs.storageAccountName
output storageAccountId string = storage.outputs.storageAccountId
output blobContainerName string = storage.outputs.blobContainerName
output documentIntelligenceName string = documentintelligence.outputs.documentIntelligenceName
output functionInboundProcess string = function_inbound_process.outputs.functionAppName
output functionInboundProcessPrincipalId string = function_inbound_process.outputs.principalId
output documentIntelligencePrincipalId string = documentintelligence.outputs.principalId
output aiVisionName string = aivision.outputs.aiVisionName
output aiVisionPrincipalId string = aivision.outputs.principalId
output aiVisionEndpoint string = aivision.outputs.endpoint
output aiLanguageName string = ailanguage.outputs.aiLanguageName
output aiLanguagePrincipalId string = ailanguage.outputs.principalId
output aiLanguageEndpoint string = ailanguage.outputs.endpoint
output aiTranslatorName string = aitranslator.outputs.aiTranslatorName
output aiTranslatorPrincipalId string = aitranslator.outputs.principalId
output aiTranslatorEndpoint string = aitranslator.outputs.endpoint
output gptVisionEndpoint string = gptvision.outputs.endpoint
output gptVisionDeploymentName string = gptvision.outputs.deploymentName
output gptVisionAccountName string = gptvision.outputs.accountName
output aiSearchServiceName string = aisearch.outputs.searchServiceName
output aiSearchOutputDataSourceName string = aisearch.outputs.processedOutputDataSourceName
output aiSearchOutputIndexName string = aisearch.outputs.processedOutputIndexName
output aiSearchOutputIndexerName string = aisearch.outputs.processedOutputIndexerName
output aiSearchOutputContainerName string = aisearch.outputs.processedOutputContainerName
output aiSearchServicePrincipalId string = aisearch.outputs.searchServicePrincipalId
