param location string = resourceGroup().location
param accountName string = 'gptvision${uniqueString(resourceGroup().id)}'
param deploymentName string = 'gpt-4o'

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

resource gpt4vision 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
	name: '${openai.name}/${deploymentName}'
	properties: {
		model: {
			name: 'gpt-4o'
			format: 'OpenAI'
			version: '2024-11-20'
		}
	}
	sku: {
		name: 'GlobalStandard'
		capacity: 1
	}
}

output endpoint string = openai.properties.endpoint
output deploymentName string = gpt4vision.name
output accountName string = openai.name
