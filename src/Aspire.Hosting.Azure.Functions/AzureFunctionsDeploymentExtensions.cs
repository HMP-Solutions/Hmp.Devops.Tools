// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma warning disable ASPIREPIPELINES001
#pragma warning disable ASPIREPIPELINES004

using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Aspire.Hosting.Azure.KnownSteps;

namespace Aspire.Hosting.Azure;

/// <summary>
/// Provides extension methods for adding Azure Functions deployment to the pipeline.
/// </summary>
public static class AzureFunctionsDeploymentExtensions
{
    /// <summary>
    /// Attempts to find the Azure CLI executable path.
    /// </summary>
    private static string? FindAzCliPath()
    {
        // Try common Azure CLI paths on Windows
        var commonPaths = new[]
        {
            "az",  // Default if in PATH
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft SDKs", "Azure", "CLI2", "wbin", "az.cmd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft SDKs", "Azure", "CLI2", "bin", "az"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python311", "Scripts", "az.cmd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python310", "Scripts", "az.cmd"),
        };

        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
    /// <summary>
    /// Adds Azure Functions publish step to generate deployment artifacts.
    /// Runs during 'aspire publish' to build Functions projects.
    /// </summary>
    /// <param name="pipeline">The distributed application pipeline.</param>
    /// <returns>The pipeline for chaining.</returns>
    public static IResourceBuilder<T> AddAzureFunctionsPublish<T>(
        this AzureFunctionsAppResource azureFunctionsAppResource,
        IResourceBuilder<T> builder)
        where T : IResource
    {
        var pipeline = builder.ApplicationBuilder.Pipeline;
        pipeline.AddStep($"{AzureFunctionsPublish}-{azureFunctionsAppResource.Name}", async context =>
        {
            var functionsEnvironment = azureFunctionsAppResource.ConfigureInfrastructure;
            var resource = azureFunctionsAppResource.TargetResource;
            if (resource is not ProjectResource projectResource)
            {
                return;
            }

            await PublishFunctionsProjectAsync(
                context,
                projectResource,
                context.CancellationToken).ConfigureAwait(false);
        }, dependsOn: WellKnownPipelineSteps.PublishPrereq,
           requiredBy: $"{AzureFunctionsZipDeploy}-{azureFunctionsAppResource.Name}");

        return builder;
    }

    /// <summary>
    /// Adds Azure Functions zip deployment step to upload code to provisioned Function Apps.
    /// Runs during 'aspire deploy' after infrastructure is provisioned.
    /// </summary>
    /// <param name="pipeline">The distributed application pipeline.</param>
    /// <returns>The pipeline for chaining.</returns>
    public static IResourceBuilder<T> AddAzureFunctionsZipDeploy<T>(
        this AzureFunctionsAppResource azureFunctionsAppResource,
        IResourceBuilder<T> builder)
        where T : IResource
    {
        var pipeline = builder.ApplicationBuilder.Pipeline;
        pipeline.AddStep($"{AzureFunctionsZipDeploy}-{azureFunctionsAppResource.Name}", async context =>
        {
            var azureFunctionsEnvironments = context.Model.Resources.OfType<AzureFunctionsEnvironmentResource>();
            if (!azureFunctionsEnvironments.Any())
            {
                return;
            }

            foreach (var functionsEnvironment in azureFunctionsEnvironments)
            {
                // Loop through ALL resources, not just compute resources
                foreach (var resource in context.Model.Resources)
                {
                    var annotation = resource.GetDeploymentTargetAnnotation();
                    if (annotation != null &&
                        annotation.ComputeEnvironment == functionsEnvironment &&
                        annotation.DeploymentTarget is AzureFunctionsAppResource functionsApp)
                    {
                        if (resource is not ProjectResource projectResource)
                        {
                            continue;
                        }

                        await DeployFunctionsProjectAsync(
                            context,
                            projectResource,
                            functionsApp,
                            functionsEnvironment,
                            context.CancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }, dependsOn: new List<string> { $"{AzureFunctionsPublish}-{azureFunctionsAppResource.Name}", $"provision-{azureFunctionsAppResource.Name}" }, requiredBy: WellKnownPipelineSteps.Deploy);

        return builder;
    }

    private static async Task PublishFunctionsProjectAsync(
        PipelineStepContext context,
        ProjectResource projectResource,
        CancellationToken cancellationToken)
    {
        var projectMetadata = projectResource.GetProjectMetadata();
        var projectPath = projectMetadata.ProjectPath;

        // Get output service to place artifacts in aspire-output folder
        var outputService = context.Services.GetRequiredService<IPipelineOutputService>();
        var outputDir = outputService.GetOutputDirectory(projectResource);
        var publishDir = Path.Combine(outputDir, "publish");
        var zipPath = Path.Combine(outputDir, $"{projectResource.Name}.zip");

        // Clean up old artifacts for idempotent builds
        // Publish the project
        var cleaningTaskDesc = await context.ReportingStep.CreateTaskAsync(
            $"Cleaning up old artifacts for {projectResource.Name} Functions project",
            cancellationToken).ConfigureAwait(false);
        await using (cleaningTaskDesc.ConfigureAwait(false))
        {
            try
            {
                if (Directory.Exists(publishDir))
                {
                    Directory.Delete(publishDir, recursive: true);
                }
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

                await cleaningTaskDesc.CompleteAsync(
                    $"Cleaned up old artifacts",
                    CompletionState.Completed,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await cleaningTaskDesc.CompleteAsync(
                    $"Cleanup failed: {ex.Message}",
                    CompletionState.CompletedWithError,
                    cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        // Publish the project
        var publishTaskDesc = await context.ReportingStep.CreateTaskAsync(
            $"Publishing {projectResource.Name} Functions project",
            cancellationToken).ConfigureAwait(false);

        await using (publishTaskDesc.ConfigureAwait(false))
        {
            try
            {
                Directory.CreateDirectory(publishDir);
                var publishProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"publish \"{projectPath}\" -c Release -o \"{publishDir}\" --self-contained true --runtime linux-x64",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                })!;

                using (publishProcess)
                {
                    await publishProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                    if (publishProcess.ExitCode != 0)
                    {
                        var error = await publishProcess.StandardError.ReadToEndAsync(cancellationToken)
                            .ConfigureAwait(false);
                        await publishTaskDesc.CompleteAsync(
                            $"Publish failed: {error}",
                            CompletionState.CompletedWithError,
                            cancellationToken).ConfigureAwait(false);
                        return;
                    }
                }

                await publishTaskDesc.CompleteAsync(
                    $"Published to {publishDir}",
                    CompletionState.Completed,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await publishTaskDesc.CompleteAsync(
                    $"Publish failed: {ex.Message}",
                    CompletionState.CompletedWithError,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        // Create deployment package (zip)
        var packageTaskDesc = await context.ReportingStep.CreateTaskAsync(
            $"Creating deployment package for {projectResource.Name}",
            cancellationToken).ConfigureAwait(false);

        await using (packageTaskDesc.ConfigureAwait(false))
        {
            try
            {
                ZipFile.CreateFromDirectory(publishDir, zipPath, System.IO.Compression.CompressionLevel.Optimal, false);

                await packageTaskDesc.CompleteAsync(
                    $"Package created at {projectResource.Name}/{projectResource.Name}.zip",
                    CompletionState.Completed,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await packageTaskDesc.CompleteAsync(
                    $"Packaging failed: {ex.Message}",
                    CompletionState.CompletedWithError,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task DeployFunctionsProjectAsync(
        PipelineStepContext context,
        ProjectResource projectResource,
        AzureFunctionsAppResource functionsApp,
        AzureFunctionsEnvironmentResource environment,
        CancellationToken cancellationToken)
    {
        var outputService = context.Services.GetRequiredService<IPipelineOutputService>();
        var outputDir = outputService.GetOutputDirectory(projectResource);
        var zipPath = Path.Combine(outputDir, $"{projectResource.Name}.zip");

        // Verify ZIP file exists
        if (!File.Exists(zipPath))
        {
            var errorTask = await context.ReportingStep.CreateTaskAsync(
                $"Deployment package not found for {projectResource.Name}",
                cancellationToken).ConfigureAwait(false);
            await using (errorTask.ConfigureAwait(false))
            {
                await errorTask.CompleteAsync(
                    $"ZIP file not found at {zipPath}. Run 'aspire publish' first.",
                    CompletionState.CompletedWithError,
                    cancellationToken).ConfigureAwait(false);
            }
            return;
        }
        var functionAppName = await functionsApp.FunctionAppName.GetValueAsync();
        if(functionAppName is null){
            throw new InvalidOperationException("Function App name output is null");
        }

        // FlexConsumption uses blob storage deployment, not Kudu
        if (environment.HostingPlan == FunctionsHostingPlan.FlexConsumption)
        {
            await DeployToFlexConsumptionAsync(context, projectResource, functionAppName, zipPath, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await DeployToKuduAsync(context, projectResource, functionAppName, zipPath, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task DeployToKuduAsync(
        PipelineStepContext context,
        ProjectResource projectResource,
        string functionAppName,
        string zipPath,
        CancellationToken cancellationToken)
    {
        // Step 1: Get Kudu publishing credentials
        var credentialsTask = await context.ReportingStep.CreateTaskAsync(
            "Retrieving Kudu publishing credentials",
            cancellationToken).ConfigureAwait(false);

        string? kuduUrl = null;
        string? publishingUsername = null;
        string? publishingPassword = null;

        await using (credentialsTask.ConfigureAwait(false))
        {
            try
            {
                // Use Azure CLI to get publishing profile (contains credentials)
                // First, find the resource group that contains this function app
                var azcliPath = FindAzCliPath() ?? "az";

                // Query to find the function app and its resource group
                var findAppProcess = new ProcessStartInfo
                {
                    FileName = azcliPath,
                    Arguments = $"functionapp list --query \"[?name=='{functionAppName}'].{{name:name, resourceGroup:resourceGroup}}\" --output json",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var findProcess = Process.Start(findAppProcess);
                if (findProcess == null)
                {
                    await credentialsTask.CompleteAsync(
                        "Failed to query for Function App",
                        CompletionState.CompletedWithError,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                string? resourceGroup = null;
                using (findProcess)
                {
                    var findOutput = await findProcess.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                    await findProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                    if (findProcess.ExitCode == 0 && !string.IsNullOrWhiteSpace(findOutput) && findOutput.Trim() != "[]")
                    {
                        try
                        {
                            var apps = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>>>(findOutput);
                            if (apps?.Count > 0 && apps[0].TryGetValue("resourceGroup", out var rgElem))
                            {
                                resourceGroup = rgElem.GetString();
                            }
                        }
                        catch
                        {
                            // Ignore parse errors, will handle below
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(resourceGroup))
                {
                    await credentialsTask.CompleteAsync(
                        $"Function App '{functionAppName}' not found. Ensure infrastructure provisioning completed successfully.",
                        CompletionState.CompletedWithError,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                // Now get the publishing credentials with the resource group
                var processInfo = new ProcessStartInfo
                {
                    FileName = azcliPath,
                    Arguments = $"functionapp deployment list-publishing-profiles -n {functionAppName} --resource-group {resourceGroup} --output json --query \"[?publishMethod=='MSDeploy'].{{username:userName, password:userPWD, url:publishUrl}}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var process = Process.Start(processInfo);
                if (process == null)
                {
                    await credentialsTask.CompleteAsync(
                        "Failed to start Azure CLI",
                        CompletionState.CompletedWithError,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                using (process)
                {
                    var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                    var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                    if (process.ExitCode != 0)
                    {
                        context.Logger.LogError("Azure CLI failed: {Error}", error);
                        await credentialsTask.CompleteAsync(
                            $"Failed to retrieve credentials: {error}",
                            CompletionState.CompletedWithError,
                            cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    // Parse JSON response
                    if (string.IsNullOrWhiteSpace(output) || output.Trim() == "[]")
                    {
                        await credentialsTask.CompleteAsync(
                            "No publishing credentials found. Verify Function App exists and is running.",
                            CompletionState.CompletedWithError,
                            cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    // Simple JSON parsing (ideally use System.Text.Json)
                    try
                    {
                        var profiles = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>>>(output);
                        if (profiles?.Count > 0)
                        {
                            var profile = profiles[0];
                            if (profile.TryGetValue("username", out var usernameElem)) publishingUsername = usernameElem.GetString();
                            if (profile.TryGetValue("password", out var passwordElem)) publishingPassword = passwordElem.GetString();
                            if (profile.TryGetValue("url", out var urlElem)) kuduUrl = urlElem.GetString()?.Replace("https://", "https://") ?? null;
                        }
                    }
                    catch (Exception ex)
                    {
                        context.Logger.LogError(ex, "Failed to parse credentials JSON");
                        await credentialsTask.CompleteAsync(
                            $"Failed to parse credentials: {ex.Message}",
                            CompletionState.CompletedWithError,
                            cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    if (string.IsNullOrEmpty(publishingUsername) || string.IsNullOrEmpty(publishingPassword))
                    {
                        await credentialsTask.CompleteAsync(
                            "Publishing credentials not found in profile",
                            CompletionState.CompletedWithError,
                            cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    await credentialsTask.CompleteAsync(
                        "Publishing credentials retrieved successfully",
                        CompletionState.Completed,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Credentials retrieval failed: {Error}", ex.Message);
                await credentialsTask.CompleteAsync(
                    $"Credentials retrieval failed: {ex.Message}",
                    CompletionState.CompletedWithError,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (string.IsNullOrEmpty(kuduUrl) || string.IsNullOrEmpty(publishingUsername) || string.IsNullOrEmpty(publishingPassword))
        {
            return;
        }

        // Step 2: Upload ZIP via Kudu API
        var uploadTask = await context.ReportingStep.CreateTaskAsync(
            $"Uploading deployment package to {functionAppName}",
            cancellationToken).ConfigureAwait(false);

        await using (uploadTask.ConfigureAwait(false))
        {
            try
            {
                // Kudu zip deploy endpoint
                var kuduDeployUrl = $"https://{functionAppName}.scm.azurewebsites.net/api/zipdeploy";

                using (var httpClient = new HttpClient())
                {
                    // Add basic auth header
                    var credentialsBytes = System.Text.Encoding.UTF8.GetBytes($"{publishingUsername}:{publishingPassword}");
                    httpClient.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credentialsBytes));

                    // Upload ZIP file
                    await using (var fileStream = File.OpenRead(zipPath))
                    {
                        var content = new StreamContent(fileStream);
                        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

                        var response = await httpClient.PostAsync(kuduDeployUrl, content, cancellationToken)
                            .ConfigureAwait(false);

                        if (response.IsSuccessStatusCode)
                        {
                            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken)
                                .ConfigureAwait(false);
                            context.Logger.LogInformation("Kudu deployment response: {Response}", responseContent);

                            var functionAppUrl = $"https://{functionAppName}.azurewebsites.net";
                            await uploadTask.CompleteAsync(
                                $"Deployment succeeded! Functions available at [{functionAppUrl}]({functionAppUrl})",
                                CompletionState.Completed,
                                cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken)
                                .ConfigureAwait(false);
                            context.Logger.LogError("Kudu deployment failed: {StatusCode} {Error}", response.StatusCode, errorContent);

                            await uploadTask.CompleteAsync(
                                $"Deployment failed: HTTP {response.StatusCode} - {errorContent}",
                                CompletionState.CompletedWithError,
                                cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Upload failed: {Error}", ex.Message);
                await uploadTask.CompleteAsync(
                    $"Upload failed: {ex.Message}",
                    CompletionState.CompletedWithError,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task DeployToFlexConsumptionAsync(
        PipelineStepContext context,
        ProjectResource projectResource,
        string functionAppName,
        string zipPath,
        CancellationToken cancellationToken)
    {
        var deployTask = await context.ReportingStep.CreateTaskAsync(
            $"Deploying {projectResource.Name} to FlexConsumption",
            cancellationToken).ConfigureAwait(false);

        await using (deployTask.ConfigureAwait(false))
        {
            try
            {
                var azcliPath = FindAzCliPath() ?? "az";

                // First, find the resource group
                // Poll for the Function App until it exists (provisioning may still be in-flight)
                string? resourceGroup = null;
                const int maxAttempts = 6; // ~60s total wait
                for (var attempt = 1; attempt <= maxAttempts && string.IsNullOrWhiteSpace(resourceGroup); attempt++)
                {
                    var findAppProcess = new ProcessStartInfo
                    {
                        FileName = azcliPath,
                        Arguments = $"functionapp list --query \"[?name=='{functionAppName}'].{{name:name, resourceGroup:resourceGroup}}\" --output json",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    var findProcess = Process.Start(findAppProcess);
                    if (findProcess == null)
                    {
                        await deployTask.CompleteAsync(
                            "Failed to query for Function App",
                            CompletionState.CompletedWithError,
                            cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    using (findProcess)
                    {
                        var findOutput = await findProcess.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                        await findProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                        if (findProcess.ExitCode == 0 && !string.IsNullOrWhiteSpace(findOutput) && findOutput.Trim() != "[]")
                        {
                            try
                            {
                                var apps = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>>>(findOutput);
                                if (apps?.Count > 0 && apps[0].TryGetValue("resourceGroup", out var rgElem))
                                {
                                    resourceGroup = rgElem.GetString();
                                }
                            }
                            catch
                            {
                                // Ignore parse errors
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(resourceGroup) && attempt < maxAttempts)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                    }
                }

                if (string.IsNullOrWhiteSpace(resourceGroup))
                {
                    await deployTask.CompleteAsync(
                        $"Function App '{functionAppName}' not found after waiting. Ensure infrastructure provisioning completed successfully.",
                        CompletionState.CompletedWithError,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                // Now deploy the ZIP with the resource group
                // For Function Apps, use 'az functionapp deployment source config-zip'
                var deployProcess = new ProcessStartInfo
                {
                    FileName = azcliPath,
                    Arguments = $"functionapp deployment source config-zip --resource-group {resourceGroup} --name {functionAppName} --src \"{zipPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var process = Process.Start(deployProcess);
                if (process == null)
                {
                    await deployTask.CompleteAsync(
                        "Failed to start deployment process",
                        CompletionState.CompletedWithError,
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                using (process)
                {
                    var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                    var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                    if (process.ExitCode == 0)
                    {
                        var functionAppUrl = $"https://{functionAppName}.azurewebsites.net";
                        await deployTask.CompleteAsync(
                            $"Deployment succeeded! Functions available at [{functionAppUrl}]({functionAppUrl})",
                            CompletionState.Completed,
                            cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        context.Logger.LogError("FlexConsumption deployment failed: {Error}", error);
                        await deployTask.CompleteAsync(
                            $"Deployment failed: {error}",
                            CompletionState.CompletedWithError,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "Deployment failed: {Error}", ex.Message);
                await deployTask.CompleteAsync(
                    $"Deployment failed: {ex.Message}",
                    CompletionState.CompletedWithError,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
