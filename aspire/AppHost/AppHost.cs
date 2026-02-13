#pragma warning disable ASPIREPIPELINES001

using Aspire.Hosting.Azure;

var builder = DistributedApplication.CreateBuilder(args);
var subscriptionId = builder.AddParameter("AzureSubscriptionId");

builder.AddAzureProvisioning();

// Create Azure Functions Environment with Consumption plan
var functionsEnv = builder
    .AddAzureFunctionsEnvironment("functions-env")
    .WithHostingPlan(FunctionsHostingPlan.FlexConsumption)
    .WithApplicationInsights(enabled: true);

// Create the Azure Functions App - link it to the environment
var environmentRemover = builder
    .AddAzureFunctionsProject<Projects.Hmp_Devops_Tools_EnvironmentRemover>("environment-remover", functionsEnv)
    .WithEnvironment("AZURE__SUBSCRIPTIONID", await subscriptionId.Resource.GetValueAsync(CancellationToken.None))
    .WithExternalHttpEndpoints()
    .PublishAsAzureFunctionApp();

environmentRemover.AddQueue("azure-devops-queue");

builder.Build().Run();
