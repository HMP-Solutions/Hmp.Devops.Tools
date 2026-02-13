using System;
using System.Threading.Tasks;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using Hmp.Devops.Tools.EnvironmentRemover.Interfaces;

namespace Hmp.Devops.Tools.EnvironmentRemover.Services
{
    public class ResourceGroupManager : IResourceGroupManager
    {
        private readonly ArmClient _armClient;
        private readonly ILogger<ResourceGroupManager> _logger;

        public ResourceGroupManager(ArmClient armClient, ILogger<ResourceGroupManager> logger)
        {
            _armClient = armClient;
            _logger = logger;
        }

        public async Task<Result<bool>> DeleteResourceGroupAsync(string repositoryName, string pullRequestId)
        {
            try
            {
                // Construct the resource group name from repository name and PR ID
                // Format: rg-{repositoryName}-pr-{pullRequestId}
                string resourceGroupName = $"rg-{repositoryName}-pr-{pullRequestId}";

                _logger.LogInformation($"Checking for resource group: {resourceGroupName}");

                // Get the subscription
                var subscription = await _armClient.GetDefaultSubscriptionAsync();

                // Check if the resource group exists
                var resourceGroupExists = await subscription.GetResourceGroups().ExistsAsync(resourceGroupName);

                if (!resourceGroupExists)
                {
                    _logger.LogInformation($"Resource group '{resourceGroupName}' does not exist. Nothing to delete.");
                    return Result<bool>.Success(false);
                }

                _logger.LogInformation($"Resource group '{resourceGroupName}' exists. Initiating deletion...");

                // Get the resource group
                var resourceGroup = await subscription.GetResourceGroups().GetAsync(resourceGroupName);

                // Delete the resource group (this is an async operation in Azure)
                var deleteOperation = await resourceGroup.Value.DeleteAsync(Azure.WaitUntil.Started);

                _logger.LogInformation($"Resource group '{resourceGroupName}' deletion initiated successfully. Operation ID: {deleteOperation.Id}");
                return Result<bool>.Success(true);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogInformation($"Resource group not found (404). It may have already been deleted.");
                return Result<bool>.Success(false);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error deleting resource group: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                return Result<bool>.Failure($"Error deleting resource group: {ex.Message}");
            }
        }
    }
}
