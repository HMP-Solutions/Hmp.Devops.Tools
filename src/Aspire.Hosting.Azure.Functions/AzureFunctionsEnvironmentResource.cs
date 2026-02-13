// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma warning disable ASPIREPIPELINES001

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Azure.Provisioning.AppService;
using Azure.Provisioning.Primitives;
using Microsoft.Extensions.Logging;
using static Aspire.Hosting.Azure.Functions.Annotations.AzureFunctionsExtensions;

namespace Aspire.Hosting.Azure;

/// <summary>
/// Represents an Azure Functions Environment resource for hosting Azure Functions.
/// </summary>
public class AzureFunctionsEnvironmentResource :
    AzureProvisioningResource,
    IAzureComputeEnvironmentResource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AzureFunctionsEnvironmentResource"/> class.
    /// </summary>
    /// <param name="name">The name of the Azure Functions Environment.</param>
    /// <param name="configureInfrastructure">The callback to configure the Azure infrastructure for this resource.</param>
    public AzureFunctionsEnvironmentResource(string name, IResourceBuilder<AzureStorageResource> storageResourceBuilder, Action<AzureResourceInfrastructure> configureInfrastructure)
        : base(name, configureInfrastructure)
    {
        StorageResource = storageResourceBuilder.Resource;
        storageResourceBuilder.WithParentRelationship(this);
        // Add pipeline step annotation to create steps and expand deployment target steps
        Annotations.Add(new PipelineStepAnnotation(async (factoryContext) =>
        {
            var model = factoryContext.PipelineContext.Model;
            var steps = new List<PipelineStep>();

            // 1. Validation Step
            var validateStep = new PipelineStep
            {
                Name = $"validate-{name}",
                Description = $"Validates Azure Functions environment configuration for {name}.",
                Action = ctx => ValidateConfigurationAsync(ctx),
                Tags = ["validate"],
                DependsOnSteps = [WellKnownPipelineSteps.PublishPrereq]
            };
            steps.Add(validateStep);

            // 2. Expand deployment targets for compute resources
            foreach (var computeResource in model.GetComputeResources())
            {
                var deploymentTarget = computeResource.GetDeploymentTargetAnnotation(this)?.DeploymentTarget;

                if (deploymentTarget != null && deploymentTarget.TryGetAnnotationsOfType<PipelineStepAnnotation>(out var annotations))
                {
                    // Resolve the deployment target's PipelineStepAnnotation and expand its steps
                    foreach (var annotation in annotations)
                    {
                        var childFactoryContext = new PipelineStepFactoryContext
                        {
                            PipelineContext = factoryContext.PipelineContext,
                            Resource = deploymentTarget
                        };

                        var deploymentTargetSteps = await annotation.CreateStepsAsync(childFactoryContext).ConfigureAwait(false);

                        foreach (var step in deploymentTargetSteps)
                        {
                            // Ensure the step is associated with the deployment target resource
                            step.Resource ??= deploymentTarget;
                        }

                        steps.AddRange(deploymentTargetSteps);
                    }
                }
            }

            return steps;
        }));

        // Add pipeline configuration annotation to wire up dependencies
        Annotations.Add(new PipelineConfigurationAnnotation(context =>
        {
            // Wire up build step dependencies
            foreach (var computeResource in context.Model.GetComputeResources())
            {
                var deploymentTarget = computeResource.GetDeploymentTargetAnnotation(this)?.DeploymentTarget;

                if (deploymentTarget is null)
                {
                    continue;
                }

                // Execute the PipelineConfigurationAnnotation callbacks on the deployment target
                if (deploymentTarget.TryGetAnnotationsOfType<PipelineConfigurationAnnotation>(out var annotations))
                {
                    foreach (var annotation in annotations)
                    {
                        annotation.Callback(context);
                    }
                }
            }

            // This ensures that resources that have to be built before deployments are handled
            // For Functions projects, skip Docker builds - they use direct ZIP deployment via functions-publish
            foreach (var computeResource in context.Model.GetBuildResources())
            {
                // Check if this is a Functions project (marked with AzureFunctionsProjectAnnotation)
                var isFunctionsProject = computeResource is ProjectResource projectResource &&
                    projectResource.Annotations.OfType<AzureFunctionsProjectAnnotation>().Any();

                if (!isFunctionsProject)
                {
                    // Only create Docker build dependencies for non-Functions projects
                    context.GetSteps(computeResource, WellKnownPipelineTags.BuildCompute)
                            .RequiredBy(WellKnownPipelineSteps.Deploy)
                            .DependsOn(WellKnownPipelineSteps.DeployPrereq);
                }
                // Functions projects are handled by functions-publish step, so skip BuildCompute
            }

            // Note: Dashboard support intentionally not included for Azure Functions environments
            // Functions apps are not designed to host the Aspire dashboard
        }));
    }

    private Task ValidateConfigurationAsync(PipelineStepContext context)
    {
        // TODO: Add validation logic for Azure Functions configuration
        // - Verify storage account configuration
        // - Validate hosting plan SKU
        // - Check required settings

        context.Logger.LogInformation("Azure Functions environment configuration validated successfully.");

        return context.ReportingStep.CompleteAsync(
            $"Azure Functions environment '{Name}' configuration validated successfully.",
            CompletionState.Completed,
            context.CancellationToken);
    }

    /// <summary>
    /// Gets the name of the Functions hosting plan.
    /// </summary>
    public BicepOutputReference PlanIdOutputReference => new("functionAppPlanId", this);

    /// <summary>
    /// Gets the name of the storage account.
    /// </summary>
    public BicepOutputReference StorageAccountNameReference => new("storageAccountName", this);

    /// <summary>
    /// Gets the storage account resource ID for RBAC role assignments.
    /// </summary>
    public BicepOutputReference StorageAccountIdReference => new("storageAccountId", this);

    /// <summary>
    /// Gets the Azure Storage resource associated with this Functions environment, used for hosting function app state and triggers.
    /// </summary>
    public AzureStorageResource StorageResource { get; init; }

    /// <summary>
    /// Gets the Functions app host name suffix.
    /// </summary>
    internal BicepOutputReference FunctionAppHostNameReference => new("functionAppHostName", this);

    /// <summary>
    /// Gets or sets a value indicating whether Application Insights telemetry should be enabled.
    /// </summary>
    internal bool EnableApplicationInsights { get; set; } = true;

    /// <summary>
    /// Gets the Application Insights Instrumentation Key.
    /// </summary>
    public BicepOutputReference AppInsightsInstrumentationKeyReference =>
        new("AZURE_APPLICATION_INSIGHTS_INSTRUMENTATIONKEY", this);

    /// <summary>
    /// Gets the Application Insights Connection String.
    /// </summary>
    public BicepOutputReference AppInsightsConnectionStringReference =>
        new("AZURE_APPLICATION_INSIGHTS_CONNECTION_STRING", this);

    /// <summary>
    /// Gets or sets the hosting plan SKU (Consumption, ElasticPremium, or FlexConsumption).
    /// </summary>
    internal FunctionsHostingPlan HostingPlan { get; set; } = FunctionsHostingPlan.Consumption;

    /// <inheritdoc/>
    public override ProvisionableResource AddAsExistingResource(AzureResourceInfrastructure infra)
    {
        var bicepIdentifier = this.GetBicepIdentifier();
        var resources = infra.GetProvisionableResources();

        // Check if an AppServicePlan (used for Functions hosting) with the same identifier already exists
        var existingPlan = resources.OfType<AppServicePlan>()
            .SingleOrDefault(plan => plan.BicepIdentifier == bicepIdentifier);

        if (existingPlan is not null)
        {
            return existingPlan;
        }

        // Create and add new resource if it doesn't exist
        var plan = AppServicePlan.FromExisting(bicepIdentifier);

        if (!TryApplyExistingResourceAnnotation(this, infra, plan))
        {
            plan.Name = PlanIdOutputReference.AsProvisioningParameter(infra);
        }

        infra.Add(plan);
        return plan;
    }

    ReferenceExpression IComputeEnvironmentResource.GetHostAddressExpression(EndpointReference endpointReference)
    {
        var resource = endpointReference.Resource;
        // Azure Functions use the format: {functionAppName}.azurewebsites.net
        return ReferenceExpression.Create($"{resource.Name.ToLowerInvariant()}.azurewebsites.net");
    }
}

/// <summary>
/// Represents the hosting plan options for Azure Functions.
/// </summary>
public enum FunctionsHostingPlan
{
    /// <summary>
    /// Consumption plan (Y1) - Pay per execution with cold start.
    /// </summary>
    Consumption,

    /// <summary>
    /// Elastic Premium plan (EP1/EP2/EP3) - Pre-warmed workers with VNet integration.
    /// </summary>
    ElasticPremium,

    /// <summary>
    /// Flex Consumption plan (FC1) - New hybrid model (Preview).
    /// </summary>
    FlexConsumption
}
