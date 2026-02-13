using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using System.Text.Json;
using Hmp.Devops.Tools.EnvironmentRemover.Interfaces;

namespace Hmp.Devops.Tools.EnvironmentRemover.Services
{
#nullable enable
    public class AzureDevOpsPayloadParser : IPullRequestPayloadParser
    {
        private readonly ILogger<AzureDevOpsPayloadParser> _logger;

        public AzureDevOpsPayloadParser(ILogger<AzureDevOpsPayloadParser> logger)
        {
            _logger = logger;
        }

        public async Task<Result<PullRequestInfo>> ParseAsync(string jsonBody)
        {
            // Parse the webhook payload
            JsonNode? webhookPayload;
            try
            {
                webhookPayload = JsonObject.Parse(jsonBody);
            }
            catch (JsonException ex)
            {
                _logger.LogError($"Failed to parse JSON payload: {ex.Message}");
                return Result<PullRequestInfo>.Failure("Invalid JSON payload");
            }

            // Get the event type
            string? eventType = webhookPayload?["eventType"]?.ToString();
            _logger.LogInformation($"Event type: {eventType}");
            
            // Check if this is a pull request event
            if (eventType != "git.pullrequest.updated" && eventType != "git.pullrequest.merged")
            {
                _logger.LogInformation($"Ignoring event type: {eventType}");
                return Result<PullRequestInfo>.Failure($"Event type not handled: {eventType}");
            }

            // Extract pull request information
            var resource = webhookPayload?["resource"];
            var pullRequest = resource?["pullRequestId"]?.ToString();
            var status = resource?["status"]?.ToString();
            var closedBy = resource?["closedBy"]?["displayName"]?.ToString();
            var repository = resource?["repository"]?["name"]?.ToString();
            var targetBranch = resource?["targetRefName"]?.ToString();
            var sourceBranch = resource?["sourceRefName"]?.ToString();
            var info = new PullRequestInfo(repository, pullRequest, status, closedBy, sourceBranch, targetBranch);
            _logger.LogInformation($"Pull Request #{pullRequest} status: {status}");
            return Result<PullRequestInfo>.Success(info);
        }
    }
#nullable restore
}
