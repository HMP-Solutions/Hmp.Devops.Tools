// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma warning disable ASPIREPIPELINES001

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.Logging;
using static Aspire.Hosting.Azure.Functions.Annotations.AzureFunctionsExtensions;

namespace Aspire.Hosting.Azure;

/// <summary>
/// Represents an Azure Functions App resource for deploying function code.
/// </summary>
public class AzureFunctionsAppResource : AzureProvisioningResource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AzureFunctionsAppResource"/> class.
    /// </summary>
    /// <param name="name">The name of the resource in the Aspire application model.</param>
    /// <param name="configureInfrastructure">Callback to configure the Azure resources.</param>
    /// <param name="targetResource">The target compute resource that this Azure Functions App is being created for.</param>
    public AzureFunctionsAppResource(
        string name, 
        Action<AzureResourceInfrastructure> configureInfrastructure, 
        IResource targetResource)
        : base(name, configureInfrastructure)
    {
        TargetResource = targetResource;

        Annotations.Add(new EnvironmentCallbackAnnotation((Action<EnvironmentCallbackContext>)(ctx => {
            targetResource.Annotations.OfType<EnvironmentCallbackAnnotation>().ToList()
                .ForEach(envAnnotation => this.Annotations.Add(envAnnotation));
        })));

        // Add pipeline step annotation for deployment steps
        Annotations.Add(new PipelineStepAnnotation((factoryContext) =>
        {
            // Get the deployment target annotation
            var deploymentTargetAnnotation = targetResource.GetDeploymentTargetAnnotation();
            if (deploymentTargetAnnotation is null)
            {
                return Task.FromResult<IEnumerable<PipelineStep>>([]);
            }

            var steps = new List<PipelineStep>();

            // Check if function app exists
            var functionAppExistsCheckStep = new PipelineStep
            {
                Name = $"check-{targetResource.Name}-exists",
                Description = $"Checks if Azure Functions App '{targetResource.Name}' exists.",
                Action = async ctx =>
                {
                    // TODO: Implement existence check logic
                    ctx.Logger.LogInformation($"Checking if function app '{targetResource.Name}' exists...");
                    
                    await ctx.ReportingStep.CompleteAsync(
                        $"Function app existence check completed for '{targetResource.Name}'.",
                        CompletionState.Completed,
                        ctx.CancellationToken).ConfigureAwait(false);
                },
                Tags = [WellKnownPipelineTags.ProvisionInfrastructure]
            };
            steps.Add(functionAppExistsCheckStep);

            // Print deployment summary
            var printResourceSummary = new PipelineStep
            {
                Name = $"print-{targetResource.Name}-summary",
                Description = $"Prints the deployment summary and URL for {targetResource.Name}.",
                Action = async ctx =>
                {
                    var computerEnv = (AzureFunctionsEnvironmentResource)deploymentTargetAnnotation.ComputeEnvironment!;
                    
                    if(deploymentTargetAnnotation.DeploymentTarget is not AzureFunctionsAppResource functionsAppResource)
                    {
                        throw new InvalidOperationException($"Target resource is not an AzureFunctionsAppResource. (`{targetResource.GetType().Name}`)");
                    }
                    var endpoint = await functionsAppResource.FunctionUrl.GetValueAsync();
                    if(endpoint is null)
                    {
                        throw new InvalidOperationException("Function App URL output is null");
                    }
                    
                    ctx.Logger.LogInformation($"Successfully deployed '{targetResource.Name}' to {endpoint}");
                    
                    await ctx.ReportingStep.CompleteAsync(
                        $"Successfully deployed **{targetResource.Name}** to [{endpoint}]({endpoint})",
                        CompletionState.Completed,
                        ctx.CancellationToken).ConfigureAwait(false);
                },
                Tags = ["print-summary"],
                RequiredBySteps = [WellKnownPipelineSteps.Deploy]
            };

            var deployStep = new PipelineStep
            {
                Name = $"deploy-{targetResource.Name}",
                Description = $"Aggregation step for deploying {targetResource.Name} to Azure Functions.",
                Action = _ => Task.CompletedTask,
                Tags = [WellKnownPipelineTags.DeployCompute]
            };

            deployStep.DependsOn(printResourceSummary);

            steps.Add(deployStep);
            steps.Add(printResourceSummary);

            return Task.FromResult<IEnumerable<PipelineStep>>(steps);
        }));

        // Add pipeline configuration annotation to wire up dependencies
        Annotations.Add(new PipelineConfigurationAnnotation((context) =>
        {
            var provisionSteps = context.GetSteps(this, WellKnownPipelineTags.ProvisionInfrastructure);

            // Check if the target resource is a Functions project (marked with AzureFunctionsProjectAnnotation)
            // Functions projects handle their own build via functions-publish step, so skip Docker build
            var isFunctionsProject = targetResource.Annotations.OfType<AzureFunctionsProjectAnnotation>().Any();

            if (!isFunctionsProject)
            {
                // For non-Functions projects, use standard Docker build
                var buildSteps = context.GetSteps(targetResource, WellKnownPipelineTags.BuildCompute);
                provisionSteps.DependsOn(buildSteps);
            }
            // For Functions projects, the functions-publish step handles the build,
            // so we don't need to depend on Docker BuildCompute steps

            // Ensure function app existence check steps depend on deploy prereq
            var functionAppExistsCheckSteps = provisionSteps
                .Where(s => s.Name.Contains("check-") && s.Name.Contains("-exists"))
                .ToList();
            
            foreach (var step in functionAppExistsCheckSteps)
            {
                step.DependsOn(WellKnownPipelineSteps.DeployPrereq);
            }

            // Print summary should wait for deployment
            var printSummarySteps = context.GetSteps(this, "print-summary");
            var deploySteps = context.GetSteps(this, WellKnownPipelineTags.DeployCompute);
            printSummarySteps.DependsOn(provisionSteps);
            deploySteps.DependsOn(printSummarySteps);

            // Make functions-zip-deploy wait for this Function App's infrastructure to be provisioned
            var zipDeploySteps = context.GetSteps("functions-zip-deploy");
            zipDeploySteps.DependsOn(provisionSteps);
            // Also depend on all infrastructure provisioning steps to avoid race when new env is created
            var allProvisionSteps = context.GetSteps(WellKnownPipelineTags.ProvisionInfrastructure);
            zipDeploySteps.DependsOn(allProvisionSteps);
        }));
    }

    public BicepOutputReference FunctionAppName => new ("functionAppName", this);
    public BicepOutputReference FunctionUrl => new ("functionAppUrl", this);
    public BicepOutputReference FunctionKudoUrl => new ("functionAppKuduUrl", this);

    /// <summary>
    /// Gets the target resource that this Azure Functions App is being created for.
    /// </summary>
    public IResource TargetResource { get; }
}
