using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;
using System.Text.Json;
using Hmp.Devops.Tools.EnvironmentRemover.Interfaces;

namespace Hmp.Devops.Tools.EnvironmentRemover
{
    public class AzureDevOpsQueue
    {
        private readonly ILogger<AzureDevOpsQueue> _logger;
        private readonly IPullRequestPayloadParser _parser;
        private readonly IResourceGroupManager _resourceGroupManager;

        public AzureDevOpsQueue(
            ILogger<AzureDevOpsQueue> logger,
            IPullRequestPayloadParser parser,
            IResourceGroupManager resourceGroupManager)
        {
            _logger = logger;
            _parser = parser;
            _resourceGroupManager = resourceGroupManager;
        }
        
        [Function("AzureDevOpsQueue")]
        public async Task<IActionResult> Queue(
            [QueueTrigger("azure-devops-queue", Connection = "AzureWebJobsStorage")] string queueMessage)
        {
            _logger.LogInformation("Azure DevOps queue received.");

            try
            {
                if (string.IsNullOrEmpty(queueMessage))
                {
                    _logger.LogWarning("Empty request body received.");
                    return new BadRequestObjectResult("Empty request body");
                }

                return await ProcessPullRequestPayloadAsync(queueMessage);
            }
            catch (JsonException ex)
            {
                _logger.LogError($"Error parsing webhook payload: {ex.Message}");
                return new BadRequestObjectResult(new { error = "Invalid JSON payload", details = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing webhook: {ex.Message}");
                return new StatusCodeResult(500);
            }
        }

        private async Task<IActionResult> ProcessPullRequestPayloadAsync(string payload)
        {
            var result = await _parser.ParseAsync(payload);
            if (!result.IsSuccess)
            {
                return new BadRequestObjectResult(new { error = result.ErrorMessage });
            }
            if (!result.Value.Closing)
            {
                return new OkObjectResult(new { message = "Not a closing event", status = result.Value.Status });
            }
            _logger.LogInformation($"Pull Request #{result.Value.PullRequestId} was closed.");
            _logger.LogInformation($"Repository: {result.Value.RepositoryName}");
            _logger.LogInformation($"Source Branch: {result.Value.SourceBranch}");
            _logger.LogInformation($"Target Branch: {result.Value.TargetBranch}");
            _logger.LogInformation($"Closed By: {result.Value.ClosedBy}");
            _logger.LogInformation($"Status: {result.Value.Status}");

            // Delete the resource group associated with this PR
            var deletionResult = await _resourceGroupManager.DeleteResourceGroupAsync(
                result.Value.RepositoryName,
                result.Value.PullRequestId);
            
            if (!deletionResult.IsSuccessful)
            {
                _logger.LogError($"Failed to delete resource group: {deletionResult.ErrorMessage}");
                return new BadRequestObjectResult(new { error = deletionResult.ErrorMessage });
            }
            if (deletionResult.Value)
            {
                _logger.LogInformation("Resource group deleted successfully.");
                return new OkObjectResult(new
                {
                    message = "Pull request closed event processed successfully",
                    pullRequestId = result.Value.PullRequestId,
                    status = result.Value.Status,
                    repository = result.Value.RepositoryName
                });
            }
            else
            {
                _logger.LogInformation("No resource group to delete.");
                return new OkObjectResult(new
                {
                    message = "Nothing was done.",
                    pullRequestId = result.Value.PullRequestId,
                    status = result.Value.Status,
                    repository = result.Value.RepositoryName
                });
            }
        }
    }
}
