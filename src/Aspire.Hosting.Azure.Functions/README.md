# Aspire.Hosting.Azure.Functions

Custom Aspire hosting library for deploying .NET Azure Functions to native Azure Functions hosting plans.

## Why This Library?

Aspire's built-in Azure deployment options are:
- **Container Apps** - Requires always-on container registry (cost overhead)
- **App Service** - Requires Premium/Basic tier, not optimized for Functions

This library enables **native Azure Functions deployment** with:
- ✅ Consumption plan (pay-per-execution)
- ✅ Elastic Premium plan (pre-warmed instances)
  - Untested
- ✅ Flex Consumption plan (hybrid model)
  - Untested
- ✅ Automatic storage account provisioning
- ✅ Application Insights integration
- ✅ Full Bicep infrastructure generation

## Usage

### 1. Add Project Reference

In your AppHost project (.csproj):

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\Aspire.Hosting.Azure.Functions\Aspire.Hosting.Azure.Functions.csproj" IsAspireProjectResource="false" />
</ItemGroup>
```

**Important**: 

- Set `IsAspireProjectResource="false"` to exclude the library from Aspire resource generation

### 2. Add Using Directive

In your AppHost.cs:

```csharp
using Aspire.Hosting.Azure;
```

### 3. Create Azure Functions Environment

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureProvisioning();

// Create Functions hosting environment
var functionsEnv = builder
    .AddAzureFunctionsEnvironment("functions-env")
    .WithHostingPlan(FunctionsHostingPlan.Consumption)  // or ElasticPremium, FlexConsumption
    .WithApplicationInsights(enabled: true);
```

### 4. Link Project to Environment and Deploy

```csharp
var environmentRemover = builder
    .AddProject<Projects.Hmp_Devops_Tools_EnvironmentRemover>("environment-remover", functionsEnv)
    .WithExternalHttpEndpoints()
    .PublishAsAzureFunctionApp();                // Configures as Functions deployment target

builder.Build().Run();
```

## Deployment Pipeline

The library implements a **two-phase deployment strategy**:

### Publish (aspire publish)

- Runs `dotnet publish` for each Functions project
- Creates deployment packages (.zip files)
- Stores artifacts in `aspire-output/{project-name}/`
- Generates Bicep infrastructure templates

### Deploy (aspire deploy)

Everything that publish does, plus:

- Provisions Azure infrastructure via Bicep templates
- Retrieves Kudu publishing credentials via Azure CLI
- Uploads deployment package via Kudu `/api/zipdeploy`
- Reports deployment URLs and status
- Requires `az login` to Azure subscription first

## Features

### Hosting Plans

- **Consumption (Y1)** - Default, pay per execution, cold start
- **Elastic Premium (EP1-3)** - Pre-warmed workers, VNet support
- **Flex Consumption (FC1)** - New hybrid model (Preview)

### Automatic Infrastructure

When you deploy, the library automatically creates:

1. **App Service Plan** - Functions hosting plan with selected SKU
2. **Storage Account** - Required for Functions runtime (triggers, state, bindings)
3. **Function App** - The actual Functions app resource
4. **Application Insights** - Optional telemetry and monitoring

### Required App Settings

Automatically configured:
- `AzureWebJobsStorage` - That is the main key, the subkeys are deployed if used.
- `FUNCTIONS_EXTENSION_VERSION` - Runtime version (~4)
- `FUNCTIONS_WORKER_RUNTIME` - Runtime type (dotnet-isolated)
- `WEBSITE_CONTENTAZUREFILECONNECTIONSTRING` - Content storage
- `WEBSITE_CONTENTSHARE` - Content share name
- `APPINSIGHTS_INSTRUMENTATIONKEY` - Application Insights key (if enabled)
- `APPLICATIONINSIGHTS_CONNECTION_STRING` - Application Insights connection (if enabled)

## Architecture

### Components

1. **AzureFunctionsEnvironmentResource**
   - Environment resource implementing `IAzureComputeEnvironmentResource`
   - Manages shared infrastructure (plan, storage, App Insights)
   - Pipeline integration for build coordination

2. **AzureFunctionsAppResource**
   - Individual Function App deployment target
   - Handles app provisioning and deployment
   - Links to Functions project resources

3. **AzureFunctionsExtensions**
   - Builder pattern extension methods
   - Bicep infrastructure generation
   - Configuration helpers

## Limitations

- **No Aspire Dashboard** - Functions apps don't host the Aspire dashboard
- **Linux Only** - Currently configured for Linux Functions