using Azure.Identity;
using Azure.ResourceManager;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Hmp.Devops.Tools.EnvironmentRemover.Interfaces;
using Hmp.Devops.Tools.EnvironmentRemover.Services;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();
    builder.AddServiceDefaults(openTelemetry =>
    {
        openTelemetry
            .UseFunctionsWorkerDefaults();
        // TODO: add azure monitor support, but that requires additional parameters
        // openTelemetry
        //     .UseAzureMonitorExporter();
    });

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddScoped<ArmClient>(serviceProvider =>
{
    var log = serviceProvider.GetRequiredService<ILogger<Program>>();
    // Get subscription ID from environment variable
    string subscriptionId = serviceProvider.GetRequiredService<IConfiguration>().GetValue<string>("AZURE_SUBSCRIPTION_ID");

    if (string.IsNullOrEmpty(subscriptionId))
    {
        log.LogError("Configuration AZURE:SUBSCRIPTIONID variable is not set.");
        throw new System.InvalidOperationException("AZURE:SUBSCRIPTIONID environment variable is not set.");
    }

    // Use DefaultAzureCredential which will use the managed identity in Azure
    var credential = new DefaultAzureCredential();
    var armClient = new ArmClient(credential, subscriptionId);
    return armClient;
});

// Register the payload parser service
builder.Services.AddScoped<IPullRequestPayloadParser, AzureDevOpsPayloadParser>();

// Register the resource group manager service
builder.Services.AddScoped<IResourceGroupManager, ResourceGroupManager>();

builder.Build().Run();
