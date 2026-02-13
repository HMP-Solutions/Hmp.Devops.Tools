using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Hmp.Devops.Tools.EnvironmentRemover.Services;

namespace Hmp.Devops.Tools.EnvironmentRemover.Tests
{
    public class AzureDevOpsPayloadParserTests
    {
        private readonly Mock<ILogger<AzureDevOpsPayloadParser>> _mockLogger;
        private readonly AzureDevOpsPayloadParser _parser;

        public AzureDevOpsPayloadParserTests()
        {
            _mockLogger = new Mock<ILogger<AzureDevOpsPayloadParser>>();
            _parser = new AzureDevOpsPayloadParser(_mockLogger.Object);
        }

        #region Valid Payload Tests

        [Fact]
        public async Task ParseAsync_WithValidUpdatedEvent_ReturnsSuccessfulResult()
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""git.pullrequest.updated"",
                ""resource"": {
                    ""pullRequestId"": ""123"",
                    ""status"": ""completed"",
                    ""repository"": { ""name"": ""TestRepo"" },
                    ""closedBy"": { ""displayName"": ""John Doe"" },
                    ""sourceRefName"": ""refs/heads/feature"",
                    ""targetRefName"": ""refs/heads/main""
                }
            }";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("TestRepo", result.Value.RepositoryName);
            Assert.Equal("123", result.Value.PullRequestId);
            Assert.Equal("completed", result.Value.Status);
            Assert.Equal("John Doe", result.Value.ClosedBy);
            Assert.Equal("refs/heads/feature", result.Value.SourceBranch);
            Assert.Equal("refs/heads/main", result.Value.TargetBranch);
        }

        [Fact]
        public async Task ParseAsync_WithValidMergedEvent_ReturnsSuccessfulResult()
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""git.pullrequest.merged"",
                ""resource"": {
                    ""pullRequestId"": ""456"",
                    ""status"": ""completed"",
                    ""repository"": { ""name"": ""AnotherRepo"" },
                    ""closedBy"": { ""displayName"": ""Jane Smith"" },
                    ""sourceRefName"": ""refs/heads/bugfix"",
                    ""targetRefName"": ""refs/heads/develop""
                }
            }";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("AnotherRepo", result.Value.RepositoryName);
            Assert.Equal("456", result.Value.PullRequestId);
            Assert.Equal("completed", result.Value.Status);
        }

        [Fact]
        public async Task ParseAsync_WithAbandonedStatus_ReturnsSuccessfulResult()
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""git.pullrequest.updated"",
                ""resource"": {
                    ""pullRequestId"": ""789"",
                    ""status"": ""abandoned"",
                    ""repository"": { ""name"": ""MyRepo"" },
                    ""closedBy"": { ""displayName"": ""Bob Wilson"" },
                    ""sourceRefName"": ""refs/heads/wip"",
                    ""targetRefName"": ""refs/heads/main""
                }
            }";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("abandoned", result.Value.Status);
            Assert.True(result.Value.Closing);
        }

        [Fact]
        public async Task ParseAsync_WithActiveStatus_ReturnsSuccessfulResult()
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""git.pullrequest.updated"",
                ""resource"": {
                    ""pullRequestId"": ""111"",
                    ""status"": ""active"",
                    ""repository"": { ""name"": ""TestRepo"" },
                    ""closedBy"": { ""displayName"": ""Charlie Brown"" },
                    ""sourceRefName"": ""refs/heads/feature"",
                    ""targetRefName"": ""refs/heads/main""
                }
            }";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("active", result.Value.Status);
            Assert.False(result.Value.Closing);
        }

        #endregion

        #region Invalid JSON Tests

        [Fact]
        public async Task ParseAsync_WithInvalidJson_ReturnsFailureResult()
        {
            // Arrange
            var payload = "{ this is not valid json }";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Invalid JSON payload", result.ErrorMessage);
        }

        [Fact]
        public async Task ParseAsync_WithEmptyJson_ReturnsFailureResult()
        {
            // Arrange
            var payload = "";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Invalid JSON payload", result.ErrorMessage);
        }

        [Fact]
        public async Task ParseAsync_WithMalformedJson_ReturnsFailureResult()
        {
            // Arrange
            var payload = @"{ ""eventType"": ""git.push"", ""resource"": { incomplete }";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Invalid JSON payload", result.ErrorMessage);
        }

        #endregion

        #region Unsupported Event Type Tests

        [Fact]
        public async Task ParseAsync_WithGitPushEvent_ReturnsFailureResult()
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""git.push"",
                ""resource"": {}
            }";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("Event type not handled", result.ErrorMessage);
        }

        [Fact]
        public async Task ParseAsync_WithCodeReviewEvent_ReturnsFailureResult()
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""git.pullrequest.reviewed"",
                ""resource"": {}
            }";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("Event type not handled", result.ErrorMessage);
        }

        [Fact]
        public async Task ParseAsync_WithBuildCompleteEvent_ReturnsFailureResult()
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""build.complete"",
                ""resource"": {}
            }";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("Event type not handled", result.ErrorMessage);
        }

        [Fact]
        public async Task ParseAsync_WithNoEventType_ReturnsFailureResult()
        {
            // Arrange
            var payload = @"{
                ""resource"": {
                    ""pullRequestId"": ""123"",
                    ""status"": ""completed""
                }
            }";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("Event type not handled", result.ErrorMessage);
        }

        #endregion

        #region Missing/Null Fields Tests

        [Fact]
        public async Task ParseAsync_WithMissingPullRequestId_ReturnsSuccessWithNullValue()
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""git.pullrequest.updated"",
                ""resource"": {
                    ""status"": ""completed"",
                    ""repository"": { ""name"": ""TestRepo"" },
                    ""closedBy"": { ""displayName"": ""John Doe"" },
                    ""sourceRefName"": ""refs/heads/feature"",
                    ""targetRefName"": ""refs/heads/main""
                }
            }";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Null(result.Value.PullRequestId);
        }

        [Fact]
        public async Task ParseAsync_WithMissingRepository_ReturnsSuccessWithNullName()
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""git.pullrequest.updated"",
                ""resource"": {
                    ""pullRequestId"": ""123"",
                    ""status"": ""completed"",
                    ""closedBy"": { ""displayName"": ""John Doe"" },
                    ""sourceRefName"": ""refs/heads/feature"",
                    ""targetRefName"": ""refs/heads/main""
                }
            }";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Null(result.Value.RepositoryName);
        }

        [Fact]
        public async Task ParseAsync_WithMissingClosedBy_ReturnsSuccessWithNullValue()
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""git.pullrequest.updated"",
                ""resource"": {
                    ""pullRequestId"": ""123"",
                    ""status"": ""completed"",
                    ""repository"": { ""name"": ""TestRepo"" },
                    ""sourceRefName"": ""refs/heads/feature"",
                    ""targetRefName"": ""refs/heads/main""
                }
            }";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Null(result.Value.ClosedBy);
        }

        [Fact]
        public async Task ParseAsync_WithMissingBranches_ReturnsSuccessWithNullValues()
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""git.pullrequest.updated"",
                ""resource"": {
                    ""pullRequestId"": ""123"",
                    ""status"": ""completed"",
                    ""repository"": { ""name"": ""TestRepo"" },
                    ""closedBy"": { ""displayName"": ""John Doe"" }
                }
            }";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Null(result.Value.SourceBranch);
            Assert.Null(result.Value.TargetBranch);
        }

        #endregion

        #region Closing Property Tests

        [Theory]
        [InlineData("completed", true)]
        [InlineData("abandoned", true)]
        [InlineData("active", false)]
        [InlineData("draft", false)]
        public async Task ParseAsync_ClosingProperty_IsCorrectForStatus(string status, bool expectedClosing)
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""git.pullrequest.updated"",
                ""resource"": {
                    ""pullRequestId"": ""123"",
                    ""status"": """ + status + @""",
                    ""repository"": { ""name"": ""TestRepo"" },
                    ""closedBy"": { ""displayName"": ""John Doe"" },
                    ""sourceRefName"": ""refs/heads/feature"",
                    ""targetRefName"": ""refs/heads/main""
                }
            }";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(expectedClosing, result.Value.Closing);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public async Task ParseAsync_WithSpecialCharactersInRepository_ParsesSuccessfully()
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""git.pullrequest.updated"",
                ""resource"": {
                    ""pullRequestId"": ""123"",
                    ""status"": ""completed"",
                    ""repository"": { ""name"": ""Test-Repo_2024"" },
                    ""closedBy"": { ""displayName"": ""John Doe"" },
                    ""sourceRefName"": ""refs/heads/feature-x-123"",
                    ""targetRefName"": ""refs/heads/main""
                }
            }";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("Test-Repo_2024", result.Value.RepositoryName);
        }

        [Fact]
        public async Task ParseAsync_WithLargePullRequestId_ParsesSuccessfully()
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""git.pullrequest.updated"",
                ""resource"": {
                    ""pullRequestId"": ""999999999"",
                    ""status"": ""completed"",
                    ""repository"": { ""name"": ""TestRepo"" },
                    ""closedBy"": { ""displayName"": ""John Doe"" },
                    ""sourceRefName"": ""refs/heads/feature"",
                    ""targetRefName"": ""refs/heads/main""
                }
            }";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("999999999", result.Value.PullRequestId);
        }

        [Fact]
        public async Task ParseAsync_WithUnicodeInDisplayName_ParsesSuccessfully()
        {
            // Arrange
            var payload = @"{
                ""eventType"": ""git.pullrequest.updated"",
                ""resource"": {
                    ""pullRequestId"": ""123"",
                    ""status"": ""completed"",
                    ""repository"": { ""name"": ""TestRepo"" },
                    ""closedBy"": { ""displayName"": ""José María"" },
                    ""sourceRefName"": ""refs/heads/feature"",
                    ""targetRefName"": ""refs/heads/main""
                }
            }";

            // Act
            var result = await _parser.ParseAsync(payload);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("José María", result.Value.ClosedBy);
        }

        #endregion
    }
}
