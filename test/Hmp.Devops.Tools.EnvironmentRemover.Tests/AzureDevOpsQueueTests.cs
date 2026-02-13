using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Hmp.Devops.Tools.EnvironmentRemover.Interfaces;

namespace Hmp.Devops.Tools.EnvironmentRemover.Tests
{
    public class AzureDevOpsQueueTests
    {
        private readonly Mock<ILogger<AzureDevOpsQueue>> _mockLogger;
        private readonly Mock<IPullRequestPayloadParser> _mockParser;
        private readonly Mock<IResourceGroupManager> _mockResourceGroupManager;
        private readonly AzureDevOpsQueue _queue;

        public AzureDevOpsQueueTests()
        {
            _mockLogger = new Mock<ILogger<AzureDevOpsQueue>>();
            _mockParser = new Mock<IPullRequestPayloadParser>();
            _mockResourceGroupManager = new Mock<IResourceGroupManager>();
            _queue = new AzureDevOpsQueue(_mockLogger.Object, _mockParser.Object, _mockResourceGroupManager.Object);
        }

        [Fact]
        public async Task Queue_WithEmptyMessage_ReturnsBadRequest()
        {
            // Arrange
            // Act
            var result = await _queue.Queue("");

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Empty request body", ((dynamic)badRequestResult.Value));
        }

        [Fact]
        public async Task Queue_WithInvalidJson_ReturnsBadRequest()
        {
            // Arrange
            _mockParser
                .Setup(p => p.ParseAsync(It.IsAny<string>()))
                .ThrowsAsync(new System.Text.Json.JsonException("Invalid JSON"));

            // Act
            var result = await _queue.Queue("{ invalid json }");

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(((dynamic)badRequestResult.Value)?.error);
        }

        [Fact]
        public async Task Queue_WithNonClosingPullRequest_ReturnsOk()
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""git.pullrequest.updated"",
                ""resource"": {
                    ""pullRequestId"": ""123"",
                    ""status"": ""active"",
                    ""repository"": { ""name"": ""TestRepo"" },
                    ""closedBy"": { ""displayName"": ""John Doe"" },
                    ""sourceRefName"": ""refs/heads/feature"",
                    ""targetRefName"": ""refs/heads/main""
                }
            }";
            var prInfo = new PullRequestInfo("TestRepo", "123", "active", "John Doe", "refs/heads/feature", "refs/heads/main");
            _mockParser
                .Setup(p => p.ParseAsync(payload))
                .ReturnsAsync(Result<PullRequestInfo>.Success(prInfo));

            // Act
            var result = await _queue.Queue(payload);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = (dynamic)okResult.Value;
            Assert.Equal("Not a closing event", response.message);
        }

        [Theory]
        [InlineData("completed")]
        [InlineData("abandoned")]
        public async Task Queue_WithClosingPullRequest_DeletesResourceGroup(string status)
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""git.pullrequest.updated"",
                ""resource"": {
                    ""pullRequestId"": ""234"",
                    ""status"": """ + status + @""",
                    ""repository"": { ""name"": ""QueueTestRepo"" },
                    ""closedBy"": { ""displayName"": ""Bob"" },
                    ""sourceRefName"": ""refs/heads/feature"",
                    ""targetRefName"": ""refs/heads/main""
                }
            }";
            var prInfo = new PullRequestInfo("QueueTestRepo", "234", status, "Bob", "refs/heads/feature", "refs/heads/main");
            
            _mockParser
                .Setup(p => p.ParseAsync(payload))
                .ReturnsAsync(Result<PullRequestInfo>.Success(prInfo));
            
            _mockResourceGroupManager
                .Setup(m => m.DeleteResourceGroupAsync("QueueTestRepo", "234"))
                .ReturnsAsync(Result<bool>.Success(true));

            // Act
            var result = await _queue.Queue(payload);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = (dynamic)okResult.Value;
            Assert.Equal("Pull request closed event processed successfully", response.message);
            Assert.Equal("234", response.pullRequestId);
            
            // Verify that DeleteResourceGroupAsync was called
            _mockResourceGroupManager.Verify(m => m.DeleteResourceGroupAsync("QueueTestRepo", "234"), Times.Once);
        }

        [Fact]
        public async Task Queue_WithClosingPullRequest_WhenResourceGroupDeletionFails_ReturnsBadRequest()
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""git.pullrequest.updated"",
                ""resource"": {
                    ""pullRequestId"": ""345"",
                    ""status"": ""completed"",
                    ""repository"": { ""name"": ""QueueTestRepo"" },
                    ""closedBy"": { ""displayName"": ""Carol"" },
                    ""sourceRefName"": ""refs/heads/bugfix"",
                    ""targetRefName"": ""refs/heads/main""
                }
            }";
            var prInfo = new PullRequestInfo("QueueTestRepo", "345", "completed", "Carol", "refs/heads/bugfix", "refs/heads/main");
            
            _mockParser
                .Setup(p => p.ParseAsync(payload))
                .ReturnsAsync(Result<PullRequestInfo>.Success(prInfo));
            
            _mockResourceGroupManager
                .Setup(m => m.DeleteResourceGroupAsync("QueueTestRepo", "345"))
                .ReturnsAsync(Result<bool>.Failure("Resource group deletion failed"));

            // Act
            var result = await _queue.Queue(payload);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            var response = (dynamic)badRequestResult.Value;
            Assert.Equal("Resource group deletion failed", response.error);
        }

        [Fact]
        public async Task Queue_WithClosingPullRequest_WhenResourceGroupDoesNotExist_ReturnsOkWithNothingDone()
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""git.pullrequest.merged"",
                ""resource"": {
                    ""pullRequestId"": ""456"",
                    ""status"": ""completed"",
                    ""repository"": { ""name"": ""AnotherRepo"" },
                    ""closedBy"": { ""displayName"": ""David"" },
                    ""sourceRefName"": ""refs/heads/feature"",
                    ""targetRefName"": ""refs/heads/main""
                }
            }";
            var prInfo = new PullRequestInfo("AnotherRepo", "456", "completed", "David", "refs/heads/feature", "refs/heads/main");
            
            _mockParser
                .Setup(p => p.ParseAsync(payload))
                .ReturnsAsync(Result<PullRequestInfo>.Success(prInfo));
            
            _mockResourceGroupManager
                .Setup(m => m.DeleteResourceGroupAsync("AnotherRepo", "456"))
                .ReturnsAsync(Result<bool>.Success(false));

            // Act
            var result = await _queue.Queue(payload);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = (dynamic)okResult.Value;
            Assert.Equal("Nothing was done.", response.message);
            Assert.Equal("456", response.pullRequestId);
        }

        [Fact]
        public async Task Queue_WhenUnexpectedException_ReturnsInternalServerError()
        {
            // Arrange
            _mockParser
                .Setup(p => p.ParseAsync(It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Unexpected error"));

            // Act
            var result = await _queue.Queue("some payload");

            // Assert
            var statusResult = Assert.IsType<StatusCodeResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        #region Helper Methods

        private HttpRequest CreateHttpRequest(string body)
        {
            var mockRequest = new Mock<HttpRequest>();
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
            mockRequest.Setup(r => r.Body).Returns(stream);
            return mockRequest.Object;
        }

        #endregion
    }
}
