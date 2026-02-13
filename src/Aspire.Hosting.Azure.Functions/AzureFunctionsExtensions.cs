// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.ApplicationModel;
using Azure.Provisioning;
using Azure.Provisioning.AppService;
using Azure.Provisioning.ApplicationInsights;
using Azure.Provisioning.Authorization;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.Resources;
using Azure.Provisioning.Storage;
using Aspire.Hosting.Azure.Functions.Annotations;
using static Aspire.Hosting.Azure.Functions.Annotations.AzureFunctionsExtensions;

namespace Aspire.Hosting.Azure;

/// <summary>
/// Provides extension methods for adding Azure Functions environments to the application model.
/// </summary>
public static partial class AzureFunctionsExtensions
{
    private const string storageResourceName = "storage";
    /// <summary>
    /// Adds an Azure Functions environment resource to the distributed application builder.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The name of the resource.</param>
    /// <returns>A resource builder for the Azure Functions environment.</returns>
    public static IResourceBuilder<AzureFunctionsEnvironmentResource> AddAzureFunctionsEnvironment(
        this IDistributedApplicationBuilder builder,
        string name)
    {
        builder.AddAzureProvisioning();

        var storage = builder.AddAzureStorage($"{name}-{storageResourceName}")
            .RunAsEmulator();
        foreach(var annotation in storage.Resource.Annotations.OfType<DefaultRoleAssignmentsAnnotation>().ToList())
        {
            // Remove default role assignments added by the Azure Storage resource, 
            // as the Function App's managed identity will be assigned specific roles with more limited permissions
            storage.Resource.Annotations.Remove(annotation);
        }

        var functionsEnvResource = new AzureFunctionsEnvironmentResource(name, storage, infra =>
        {
            var envResource = (AzureFunctionsEnvironmentResource)infra.AspireResource;
            
            // Location parameter is added automatically by the framework, create a reference to it
            var location = new ProvisioningParameter("location", typeof(string));

            var tags = new ProvisioningParameter("tags", typeof(object))
            {
                Value = new BicepDictionary<string>()
            };
            infra.Add(tags);

            // reference the storage account (required for Azure Functions)
            var storageAccount = StorageAccount.FromExisting(envResource.StorageResource.GetBicepIdentifier());
            
            infra.Add(storageAccount);

            // Create App Service Plan based on hosting plan type
            var plan = new AppServicePlan("functionsPlan")
            {
                Location = location,
                Tags = tags
            };

            // Configure SKU based on hosting plan
            switch (envResource.HostingPlan)
            {
                case FunctionsHostingPlan.Consumption:
                    plan.Sku = new AppServiceSkuDescription
                    {
                        Name = "Y1",
                        Tier = "Dynamic"
                    };
                    plan.Kind = "functionapp";
                    break;

                case FunctionsHostingPlan.ElasticPremium:
                    plan.Sku = new AppServiceSkuDescription
                    {
                        Name = "EP1",
                        Tier = "ElasticPremium"
                    };
                    plan.Kind = "elastic";
                    break;

                case FunctionsHostingPlan.FlexConsumption:
                    plan.Sku = new AppServiceSkuDescription
                    {
                        Name = "FC1",
                        Tier = "FlexConsumption"
                    };
                    plan.Kind = "functionapp";
                    break;
            }

            // For Linux Functions - set reserved flag directly
            plan.IsReserved = true;

            infra.Add(plan);

            // Optional: Application Insights
            if (envResource.EnableApplicationInsights)
            {
                var appInsights = new ApplicationInsightsComponent("funcAppInsights")
                {
                    Location = location,
                    Kind = "web",
                    ApplicationType = ApplicationInsightsApplicationType.Web,
                    Tags = tags
                };
                infra.Add(appInsights);

                // Add outputs for Application Insights
                infra.Add(new ProvisioningOutput("AZURE_APPLICATION_INSIGHTS_INSTRUMENTATIONKEY", typeof(string))
                {
                    Value = appInsights.InstrumentationKey
                });

                infra.Add(new ProvisioningOutput("AZURE_APPLICATION_INSIGHTS_CONNECTION_STRING", typeof(string))
                {
                    Value = appInsights.ConnectionString
                });
            }

            // Add outputs
            infra.Add(new ProvisioningOutput("functionAppPlanId", typeof(string))
            {
                Value = plan.Id
            });

            infra.Add(new ProvisioningOutput("storageAccountName", typeof(string))
            {
                Value = storageAccount.Name
            });

            infra.Add(new ProvisioningOutput("storageAccountId", typeof(string))
            {
                Value = storageAccount.Id
            });

            infra.Add(new ProvisioningOutput("functionAppHostName", typeof(string))
            {
                Value = BicepFunction.Interpolate($"azurewebsites.net")
            });
        });

        return builder.AddResource(functionsEnvResource)
                     .WithManifestPublishingCallback(functionsEnvResource.WriteToManifest);
    }

    /// <summary>
    /// Adds an Azure Functions project as a deployment target for Azure Functions.
    /// This registers the project to skip Docker container builds and use direct Functions deployment.
    /// </summary>
    /// <typeparam name="T">The project type.</typeparam>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name.</param>
    /// <returns>A resource builder for the Functions project.</returns>
    public static IResourceBuilder<ProjectResource> AddAzureFunctionsProject<T>(
        this IDistributedApplicationBuilder builder,
        string name,
        IResourceBuilder<AzureFunctionsEnvironmentResource> environmentResourceBuilder)
        where T : IProjectMetadata, new()
    {
        builder.AddAzureProvisioning();

        // Use the standard AddProject flow but mark it as a Functions project
        var projectResource = builder.AddProject<T>(name);

         // Add launch profile annotations like regular projects do.
        // This ensures proper VS integration and port handling.
        var appHostDefaultLaunchProfileName = builder.Configuration["AppHost:DefaultLaunchProfileName"]
            ?? builder.Configuration["DOTNET_LAUNCH_PROFILE"];
        if (!string.IsNullOrEmpty(appHostDefaultLaunchProfileName))
        {
            projectResource.WithAnnotation(new DefaultLaunchProfileAnnotation(appHostDefaultLaunchProfileName));
        }

        projectResource.WithEnvironment(context =>
        {
            ApplyAzureFunctionsConfiguration(environmentResourceBuilder.Resource.StorageResource, context.EnvironmentVariables, "AzureWebJobsStorage");
        });


        // Add an annotation to mark this as a Functions project (skip Docker build)
        projectResource.Resource.Annotations.Add(new AzureFunctionsProjectAnnotation());
        projectResource.WithComputeEnvironment(environmentResourceBuilder);
        return projectResource;
    }

    private static class AzureStorageEmulatorConnectionString
    {
        // Use defaults from https://learn.microsoft.com/azure/storage/common/storage-configure-connection-string#connect-to-the-emulator-account-using-the-shortcut
        private const string ConnectionStringHeader = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;";

        public static ReferenceExpression Create(EndpointReference? blobEndpoint = null, EndpointReference? queueEndpoint = null, EndpointReference? tableEndpoint = null)
        {
            var builder = new ReferenceExpressionBuilder();
            builder.AppendLiteral(ConnectionStringHeader);

            if (blobEndpoint is not null)
            {
                AppendEndpointExpression(builder, "BlobEndpoint", blobEndpoint);
            }
            if (queueEndpoint is not null)
            {
                AppendEndpointExpression(builder, "QueueEndpoint", queueEndpoint);
            }
            if (tableEndpoint is not null)
            {
                AppendEndpointExpression(builder, "TableEndpoint", tableEndpoint);
            }

            return builder.Build();

            static void AppendEndpointExpression(ReferenceExpressionBuilder builder, string key, EndpointReference endpoint)
            {
                builder.Append($"{key}={endpoint.Property(EndpointProperty.Scheme)}://{endpoint.Property(EndpointProperty.IPV4Host)}:{endpoint.Property(EndpointProperty.Port)}/devstoreaccount1;");
            }
        }
    }
    private static void ApplyAzureFunctionsConfiguration(AzureStorageResource storage, IDictionary<string, object> target, string connectionName)
    {

        const string BlobsConnectionKeyPrefix = "Aspire__Azure__Storage__Blobs";
        const string QueuesConnectionKeyPrefix = "Aspire__Azure__Storage__Queues";
        const string TablesConnectionKeyPrefix = "Aspire__Azure__Data__Tables";

        if (storage.IsEmulator)
        {
            EndpointReference EmulatorBlobEndpoint = new(storage, "blob");
            EndpointReference EmulatorQueueEndpoint = new(storage, "queue");
            EndpointReference EmulatorTableEndpoint = new(storage, "table");
            ReferenceExpression GetEmulatorConnectionString()
            {
                return storage.IsEmulator ? AzureStorageEmulatorConnectionString.Create(blobEndpoint: EmulatorBlobEndpoint, queueEndpoint: EmulatorQueueEndpoint, tableEndpoint: EmulatorTableEndpoint)
                  : throw new InvalidOperationException("The Azure Storage resource is not running in the local emulator.");
            }
            // Injected to support Azure Functions listener initialization.
            var connectionString = GetEmulatorConnectionString();
            target[connectionName] = connectionString;
            // Injected to support Aspire client integration for Azure Storage.
            target[$"{BlobsConnectionKeyPrefix}__{connectionName}__ConnectionString"] = connectionString;
            target[$"{QueuesConnectionKeyPrefix}__{connectionName}__ConnectionString"] = connectionString;
            target[$"{TablesConnectionKeyPrefix}__{connectionName}__ConnectionString"] = connectionString;
        }
    }

    /// <summary>
    /// Configures the hosting plan for the Azure Functions environment.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="hostingPlan">The hosting plan to use.</param>
    /// <returns>The resource builder.</returns>
    public static IResourceBuilder<AzureFunctionsEnvironmentResource> WithHostingPlan(
        this IResourceBuilder<AzureFunctionsEnvironmentResource> builder,
        FunctionsHostingPlan hostingPlan)
    {
        builder.Resource.HostingPlan = hostingPlan;
        return builder;
    }

    /// <summary>
    /// Configures whether Application Insights should be enabled for the Azure Functions environment.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="enabled">Whether to enable Application Insights.</param>
    /// <returns>The resource builder.</returns>
    public static IResourceBuilder<AzureFunctionsEnvironmentResource> WithApplicationInsights(
        this IResourceBuilder<AzureFunctionsEnvironmentResource> builder,
        bool enabled = true)
    {
        builder.Resource.EnableApplicationInsights = enabled;
        return builder;
    }

    /// <summary>
    /// Publishes a project resource as an Azure Function App.
    /// </summary>
    /// <typeparam name="T">The type of the project resource.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="configureApp">Optional callback to configure the Function App infrastructure.</param>
    /// <returns>The resource builder.</returns>
    public static IResourceBuilder<T> PublishAsAzureFunctionApp<T>(
        this IResourceBuilder<T> builder,
        Action<AzureResourceInfrastructure, WebSite>? configureApp = null)
        where T : ProjectResource, IResource
    {
        builder.ApplicationBuilder.AddAzureProvisioning();

        var azureFunctionAppResource = builder.PublishAsAzureFunctionAppInternal((resource, environment, infrastructure, app) =>
        {
            configureApp?.Invoke(infrastructure, app);
        });
        azureFunctionAppResource.AddAzureFunctionsPublish(builder);
        azureFunctionAppResource.AddAzureFunctionsZipDeploy(builder);

        return builder;
    }

    private static AzureFunctionsAppResource PublishAsAzureFunctionAppInternal<T>(
        this IResourceBuilder<T> builder,
        Action<T, AzureFunctionsEnvironmentResource, AzureResourceInfrastructure, WebSite> configure)
        where T : ProjectResource, IResource
    {
        // Get or create the Functions environment
        var environment = builder.ApplicationBuilder.Resources
            .OfType<AzureFunctionsEnvironmentResource>()
            .FirstOrDefault();

        if (environment == null)
        {
            throw new InvalidOperationException(
                "No Azure Functions environment found. Call AddAzureFunctionsEnvironment() first.");
        }

        // Create the deployment target
        var functionsAppResource = new AzureFunctionsAppResource(
            builder.Resource.Name + "-functionapp",
            infra => ConfigureFunctionApp(builder.Resource, infra, configure, builder.ApplicationBuilder.ExecutionContext),
            builder.Resource);
        functionsAppResource.Annotations.Add(new FunctionAppToHostAnnotation(environment));

        // Add deployment target annotation (links project to Functions app)
        var deploymentTargetAnnotation = new DeploymentTargetAnnotation(functionsAppResource)
        {
            ComputeEnvironment = environment
        };

        builder.WithAnnotation(deploymentTargetAnnotation);

        return functionsAppResource;
    }

    private static void ConfigureFunctionApp<T>(
        T resource,
        AzureResourceInfrastructure infra,
        Action<T, AzureFunctionsEnvironmentResource, AzureResourceInfrastructure, WebSite> configure,
        DistributedApplicationExecutionContext executionContext)
        where T : IResource
    {
        var aspireFunctionApp = infra.AspireResource as AzureFunctionsAppResource ?? 
            throw new InvalidOperationException("AzureFunctionsAppResource is required for function app configuration.");
        var environment = aspireFunctionApp.Annotations.OfType<FunctionAppToHostAnnotation>().Single().Environment;
        // Get references to environment resources
        var planIdParam = environment.PlanIdOutputReference.AsProvisioningParameter(infra);
        var storageAccountName = environment.StorageAccountNameReference.AsProvisioningParameter(infra);
        var storageAccountId = environment.StorageAccountIdReference.AsProvisioningParameter(infra);
        // Create reference to existing storage account for role assignment scope
        var storageAccount = StorageAccount.FromExisting(environment.StorageResource.GetBicepIdentifier());
        // Detect .NET version from the project's target framework
        var dotnetVersion = "10.0"; // Default to 10.0
        if (resource is ProjectResource projectResource)
        {
            var projectMetadata = projectResource.GetProjectMetadata();
            var projectPath = projectMetadata.ProjectPath;

            // Parse the project file to extract TargetFramework
            try
            {
                var projectXml = System.Xml.Linq.XDocument.Load(projectPath);
                var targetFramework = projectXml.Descendants("TargetFramework").FirstOrDefault()?.Value;

                if (!string.IsNullOrEmpty(targetFramework))
                {
                    // Extract version from "net10.0", "net8.0", etc.
                    var versionMatch = System.Text.RegularExpressions.Regex.Match(targetFramework, @"net(\d+)\.(\d+)");
                    if (versionMatch.Success)
                    {
                        dotnetVersion = $"{versionMatch.Groups[1].Value}.{versionMatch.Groups[2].Value}";
                    }
                }
            }
            catch
            {
                // If parsing fails, use default version
            }
        }

        var blobService = new BlobService("blobService")
        {
            Parent = storageAccount
        };
        infra.Add(blobService);

        var deploymentContainer = new BlobContainer("deployments")
        {
            Parent = blobService,
            PublicAccess = StoragePublicAccessType.None
        };
        infra.Add(deploymentContainer);

        // Create the Function App (WebSite resource with functionapp kind)
        var functionApp = new WebSite(Infrastructure.NormalizeBicepIdentifier(resource.Name))
        {
            Name = BicepFunction.Take(BicepFunction.Concat(resource.Name.ToLowerInvariant(), BicepFunction.GetUniqueString(BicepFunction.GetResourceGroup().Id)), 45),
            Kind = "functionapp,linux"
        };

        // Set the App Service Plan (required for Functions hosting)
        functionApp.AppServicePlanId = planIdParam;

        // Enable System-Assigned Managed Identity for secure Azure Storage access
        functionApp.Identity = new ManagedServiceIdentity { ManagedServiceIdentityType = ManagedServiceIdentityType.SystemAssigned };

        // Configure app settings with identity-based storage authentication
        // Using __accountName suffix enables managed identity authentication (no keys required)
        var appSettings = new List<AppServiceNameValuePair>
        {
            new() { Name = "AzureWebJobsStorage__accountName", Value = storageAccountName },
            new() { Name = "FUNCTIONS_EXTENSION_VERSION", Value = "~4" },
        };
        // FUNCTIONS_WORKER_RUNTIME is not used in FlexConsumption - runtime is configured in functionAppConfig instead
        if (environment.HostingPlan != FunctionsHostingPlan.FlexConsumption)
        {
            appSettings.Add(new AppServiceNameValuePair { Name = "FUNCTIONS_WORKER_RUNTIME", Value = "dotnet-isolated" });
        }

        // Add Application Insights if enabled
        if (environment.EnableApplicationInsights)
        {
            var appInsightsKeyParam = environment.AppInsightsInstrumentationKeyReference.AsProvisioningParameter(infra);
            var appInsightsConnectionParam = environment.AppInsightsConnectionStringReference.AsProvisioningParameter(infra);

            appSettings.Add(new AppServiceNameValuePair { Name = "APPINSIGHTS_INSTRUMENTATIONKEY", Value = appInsightsKeyParam });
            appSettings.Add(new AppServiceNameValuePair { Name = "APPLICATIONINSIGHTS_CONNECTION_STRING", Value = appInsightsConnectionParam });
        }

        // Configure Function App settings
        functionApp.SiteConfig = new SiteConfigProperties
        {
            AppSettings = [.. appSettings]
        };

        var envSettings = new Dictionary<string, object>();
        // Gather environment settings from target resource
        var envCallbackContext = new EnvironmentCallbackContext(executionContext, envSettings);
        resource.Annotations.OfType<EnvironmentCallbackAnnotation>().ToList()
            .ForEach(async envAnnotation =>
            {

                await envAnnotation.Callback(envCallbackContext);

            });
        // Here we would apply the settings to the function app
        // For demonstration, we just log them
        foreach (var kvp in envSettings)
        {
            if (kvp.Value is null)
            {
                continue;
            }
            var strValue = kvp.Value.ToString();
            if (strValue is null)
            {
                continue;
            }

            functionApp.SiteConfig.AppSettings.Add(new AppServiceNameValuePair
            {
                Name = kvp.Key,
                Value = new BicepValue<string>(strValue)
            });
        }

        // For FlexConsumption plans, functionAppConfig is required and LinuxFxVersion must NOT be set
        if (environment.HostingPlan == FunctionsHostingPlan.FlexConsumption)
        {
            // Configure functionAppConfig for FlexConsumption using strongly-typed objects
            functionApp.FunctionAppConfig = new FunctionAppConfig
            {
                DeploymentStorage = new FunctionAppStorage
                {
                    StorageType = FunctionAppStorageType.BlobContainer,
                    Value = new BicepValue<Uri>(BicepFunction.Concat(storageAccount.PrimaryEndpoints.BlobUri, deploymentContainer.Name).Compile()),
                    Authentication = new FunctionAppStorageAuthentication
                    {
                        AuthenticationType = FunctionAppStorageAccountAuthenticationType.SystemAssignedIdentity
                    }
                },
                ScaleAndConcurrency = new FunctionAppScaleAndConcurrency
                {
                    MaximumInstanceCount = 100,
                    InstanceMemoryMB = 512
                },
                Runtime = new FunctionAppRuntime
                {
                    Name = FunctionAppRuntimeName.DotnetIsolated,
                    Version = dotnetVersion
                }
            };
        }
        else
        {
            // For Consumption/Premium/Dedicated plans, use LinuxFxVersion
            functionApp.SiteConfig.LinuxFxVersion = $"DOTNET-ISOLATED|{dotnetVersion}";
        }

        infra.Add(functionApp);


        storageAccount.Name = storageAccountName;
        infra.Add(storageAccount);

        // Create RBAC role assignment granting Storage Blob Data Contributor to the Function App's managed identity
        // The AssignRole method creates a role assignment scoped to the storage account
        var roleAssignment = storageAccount.CreateRoleAssignment(StorageBuiltInRole.StorageBlobDataContributor, RoleManagementPrincipalType.ServicePrincipal, functionApp.Identity!.PrincipalId);
        roleAssignment.Name = BicepFunction.CreateGuid(storageAccount.Name, functionApp.Name, "storageBlobDataContributorRole");
        infra.Add(roleAssignment);

        foreach(var annotation in resource.Annotations.OfType<AzureFunctionStorageAssignmentAnnotation>())
        {
            // Add additional role assignment for the storage component referenced in the annotation
            RoleAssignment additionalRoleAssignment = annotation.StorageComponent switch
            {
                AzureQueueStorageQueueResource queueStorage => storageAccount.CreateRoleAssignment(StorageBuiltInRole.StorageQueueDataContributor, RoleManagementPrincipalType.ServicePrincipal, functionApp.Identity!.PrincipalId),
                AzureBlobStorageResource blobStorage => storageAccount.CreateRoleAssignment(StorageBuiltInRole.StorageBlobDataContributor, RoleManagementPrincipalType.ServicePrincipal, functionApp.Identity!.PrincipalId),
                _ => throw new InvalidOperationException("Unsupported storage component type in AzureFunctionStorageAssignmentAnnotation.")
            };

            var assignmentNameSuffix = annotation.StorageComponent switch
            {
                AzureQueueStorageQueueResource => "storageQueueDataContributorRole",
                AzureBlobStorageResource => "storageBlobDataContributorRole",
                _ => "storageDataContributorRole"
            };
            
            additionalRoleAssignment.Name = BicepFunction.CreateGuid(storageAccount.Name, functionApp.Name, "additional", assignmentNameSuffix);
            infra.Add(additionalRoleAssignment);

            var config = annotation.StorageComponent switch
            {
                AzureQueueStorageQueueResource queueStorage => ("AzureWebJobsStorage__queueServiceUri", storageAccount.PrimaryEndpoints.QueueUri),
                AzureBlobStorageResource blobStorage => ("AzureWebJobsStorage__blobServiceUri", storageAccount.PrimaryEndpoints.BlobUri),
                _ => throw new InvalidOperationException("Unsupported storage component type in AzureFunctionStorageAssignmentAnnotation")
            };
            functionApp.SiteConfig.AppSettings.Add(new AppServiceNameValuePair
            {
                Name = config.Item1,
                Value = config.Item2
            });
        }

        // Export Function App URLs for use in deployment
        var hostSuffixParam = environment.FunctionAppHostNameReference.AsProvisioningParameter(infra);
        infra.Add(new ProvisioningOutput("functionAppUrl", typeof(string))
        {
            Value = BicepFunction.Interpolate($"https://{functionApp.Name}.{hostSuffixParam}")
        });
        infra.Add(new ProvisioningOutput("functionAppKuduUrl", typeof(string))
        {
            Value = BicepFunction.Interpolate($"https://{functionApp.Name}.scm.{hostSuffixParam}")
        });

        infra.Add(new ProvisioningOutput("functionAppName", typeof(string))
        {
            Value = functionApp.Name
        });

        // Allow custom configuration
        configure?.Invoke(resource, environment, infra, functionApp);
    }

    public static IResourceBuilder<T> AddQueue<T>(this IResourceBuilder<T> projectBuilder, string queueName)
        where T : ProjectResource, IResource
    {
        var deploymentTarget = projectBuilder.Resource.Annotations.OfType<DeploymentTargetAnnotation>().Single();
        var functionAppResource = deploymentTarget.DeploymentTarget as AzureFunctionsAppResource
            ?? throw new InvalidOperationException("DeploymentTargetAnnotation must reference an AzureFunctionsAppResource.");
        var storage = functionAppResource.Annotations.OfType<FunctionAppToHostAnnotation>().Single().Environment.StorageResource;
        var queue = projectBuilder.ApplicationBuilder.CreateResourceBuilder(storage)
            .AddQueue(queueName);
        projectBuilder.WithAnnotation(new AzureFunctionStorageAssignmentAnnotation(queue.Resource));
        return projectBuilder;
    }
}