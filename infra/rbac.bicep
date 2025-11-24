param aiVisionName string
param aiLanguageName string
param aiTranslatorName string
param functionInboundProcessPrincipalId string
param documentIntelligencePrincipalId string
param storageAccountName string
param documentIntelligenceName string
param piiDetectionName string
param logicAppPrincipalId string
param storageBlobDataContributorRoleId string = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
param storageAccountDataOwnerRoleId string = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
param storageAccountTableDataContributorRoleId string = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
param storageAccountContributorRoleId string = '17d1049b-9a84-46fb-8f53-869881c3d3ab'
param storageQueueMessageProcessorRoleId string = '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
param cognitiveServicesContributorRoleId string = '25fbc0a9-bd7c-42a3-aa1a-3b75d497ee68'
param cognitiveServicesUserRoleId string = 'a97b65f3-24c7-4388-baec-2e87135dc908'
param storageBlobDataReaderRoleId string = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'
param gptVisionAccountName string
param currentUserObjectId string

resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' existing = {
  name: storageAccountName
}

resource documentIntelligence 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: documentIntelligenceName
}

resource piiDetection 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: piiDetectionName
}

resource aiVision 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: aiVisionName
}

resource aiLanguage 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: aiLanguageName
}

resource aiTranslator 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: aiTranslatorName
}

resource gptvision 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: gptVisionAccountName 
}

// Cognitive Services User role for AI Language
resource ailanguage_user_rbac_inbound 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(aiLanguage.id, functionInboundProcessPrincipalId, cognitiveServicesUserRoleId)
  scope: aiLanguage
  properties: {
    principalId: functionInboundProcessPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalType: 'ServicePrincipal'
  }
}

// Cognitive Services User role for AI Translator
resource aitranslator_user_rbac_inbound 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(aiTranslator.id, functionInboundProcessPrincipalId, cognitiveServicesUserRoleId)
  scope: aiTranslator
  properties: {
    principalId: functionInboundProcessPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalType: 'ServicePrincipal'
  }
}

@description('Assign Storage Blob Data Reader role to AI Vision on the storage account')
resource storage_blob_reader_aivision 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, aiVision.id, storageBlobDataReaderRoleId)
  scope: storageAccount
  properties: {
    principalId: aiVision.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataReaderRoleId)
    principalType: 'ServicePrincipal'
  }
}

// Cognitive Services User role for AI Vision
resource aivision_user_rbac_inbound 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(aiVision.id, functionInboundProcessPrincipalId, cognitiveServicesUserRoleId)
  scope: aiVision
  properties: {
    principalId: functionInboundProcessPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalType: 'ServicePrincipal'
  }
}

// Cognitive Services User role for PII Detection
resource piidetection_user_rbac_inbound 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(piiDetection.id, functionInboundProcessPrincipalId, cognitiveServicesUserRoleId)
  scope: piiDetection
  properties: {
    principalId: functionInboundProcessPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalType: 'ServicePrincipal'
  }
}

// Storage Account Contributor role
resource storage_rbac_inbound 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionInboundProcessPrincipalId, storageAccountDataOwnerRoleId)
  scope: storageAccount
  properties: {
    principalId: functionInboundProcessPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageAccountDataOwnerRoleId)
    principalType: 'ServicePrincipal'
  }
}

// Storage Account Contributor role
resource storage_contributor_rbac_inbound 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionInboundProcessPrincipalId, storageAccountContributorRoleId)
  scope: storageAccount
  properties: {
    principalId: functionInboundProcessPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageAccountContributorRoleId)
    principalType: 'ServicePrincipal'
  }
}

resource storage_blob_contributor_logicapp 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, logicAppPrincipalId, storageBlobDataContributorRoleId)
  scope: storageAccount
  properties: {
    principalId: logicAppPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalType: 'ServicePrincipal'
  }
}

resource storage_table_contributor_rbac_inbound 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionInboundProcessPrincipalId, storageAccountTableDataContributorRoleId)
  scope: storageAccount
  properties: {
    principalId: functionInboundProcessPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageAccountTableDataContributorRoleId)
    principalType: 'ServicePrincipal'
  }
}

// Storage Queue Data Contributor role (for queue access)
resource queue_rbac_inbound 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionInboundProcessPrincipalId, storageQueueMessageProcessorRoleId)
  scope: storageAccount
  properties: {
    principalId: functionInboundProcessPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageQueueMessageProcessorRoleId)
    principalType: 'ServicePrincipal'
  }
}

// Cognitive Services User role for Document Intelligence
resource docintelligence_rbac_inbound 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(documentIntelligence.id, functionInboundProcessPrincipalId, cognitiveServicesContributorRoleId)
  scope: documentIntelligence
  properties: {
    principalId: functionInboundProcessPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesContributorRoleId)
    principalType: 'ServicePrincipal'
  }
}

resource docintelligence_user_rbac_inbound 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(documentIntelligence.id, functionInboundProcessPrincipalId, cognitiveServicesUserRoleId)
  scope: documentIntelligence
  properties: {
    principalId: functionInboundProcessPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalType: 'ServicePrincipal'
  }
}

resource storage_blob_reader_docintelligence 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, documentIntelligencePrincipalId, storageBlobDataReaderRoleId)
  scope: storageAccount
  properties: {
    principalId: documentIntelligencePrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataReaderRoleId)
    principalType: 'ServicePrincipal'
  }
}

// Cognitive Services User role for GPT-4 Vision
resource gptvision_user_rbac_inbound 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(gptvision.id, functionInboundProcessPrincipalId, cognitiveServicesUserRoleId)
  scope: gptvision
  properties: {
    principalId: functionInboundProcessPrincipalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalType: 'ServicePrincipal'
  }
}

// Grant the signed-in user Storage Blob Data Contributor on the storage account
resource storage_blob_contributor_current_user 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, currentUserObjectId, storageBlobDataContributorRoleId)
  scope: storageAccount
  properties: {
    principalId: currentUserObjectId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalType: 'User'
  }
}
