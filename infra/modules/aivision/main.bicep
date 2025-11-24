
param location string
param aiVisionName string

resource aiVision 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
	name: aiVisionName
	location: location
	kind: 'ComputerVision'
	sku: {
		name: 'S1'
	}
	identity: {
		type: 'SystemAssigned'
	}
	properties: {
		customSubDomainName: aiVisionName
		networkAcls: {
			defaultAction: 'Allow'
		}
		publicNetworkAccess: 'Enabled'
	}
}

output aiVisionName string = aiVision.name
output resourceId string = aiVision.id
output endpoint string = aiVision.properties.endpoint
output principalId string = aiVision.identity.principalId
