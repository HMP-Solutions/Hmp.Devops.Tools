using System;
using System.Threading.Tasks;
using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Hmp.Devops.Tools.EnvironmentRemover.Services;

namespace Hmp.Devops.Tools.EnvironmentRemover.Tests
{
    public class ResourceGroupManagerTests
    {
        private readonly Mock<ILogger<ResourceGroupManager>> _mockLogger;
        private readonly Mock<ArmClient> _mockArmClient;
        private readonly ResourceGroupManager _resourceGroupManager;

        public ResourceGroupManagerTests()
        {
            _mockLogger = new Mock<ILogger<ResourceGroupManager>>();
            _mockArmClient = new Mock<ArmClient>();
            _resourceGroupManager = new ResourceGroupManager(_mockArmClient.Object, _mockLogger.Object);
        }

        #region Successful Deletion Tests

        [Fact]
        public async Task DeleteResourceGroupAsync_WhenResourceGroupExists_CallsDeleteAsync()
        {
            // Arrange
            var repositoryName = "TestRepo";
            var pullRequestId = "123";
            var expectedResourceGroupName = "rg-TestRepo-pr-123";

            var mockSubscription = new Mock<SubscriptionResource>();
            var mockResourceGroupContainer = new Mock<ResourceGroupCollection>();
            var mockResourceGroup = new Mock<ResourceGroupResource>();

            _mockArmClient
                .Setup(c => c.GetDefaultSubscriptionAsync())
                .ReturnsAsync(mockSubscription.Object);

            mockSubscription
                .Setup(s => s.GetResourceGroups())
                .Returns(mockResourceGroupContainer.Object);

            mockResourceGroupContainer
                .Setup(c => c.ExistsAsync(expectedResourceGroupName))
                .ReturnsAsync(Response.FromValue(true, new Mock<Response>().Object));

            mockResourceGroupContainer
                .Setup(c => c.GetAsync(expectedResourceGroupName))
                .ReturnsAsync(Response.FromValue(mockResourceGroup.Object, new Mock<Response>().Object));

            // Create a mock ArmOperation that behaves correctly
            var mockArmOperation = new Mock<Azure.ResourceManager.ArmOperation>();
            mockArmOperation.Setup(o => o.Id).Returns("operation-id-123");

            mockResourceGroup
                .Setup(rg => rg.DeleteAsync(Azure.WaitUntil.Started))
                .ReturnsAsync(mockArmOperation.Object);

            // Act
            var result = await _resourceGroupManager.DeleteResourceGroupAsync(repositoryName, pullRequestId);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.True(result.Value);
            mockResourceGroup.Verify(rg => rg.DeleteAsync(Azure.WaitUntil.Started), Times.Once);
        }

        #endregion

        #region Resource Group Not Found Tests

        [Fact]
        public async Task DeleteResourceGroupAsync_WhenResourceGroupDoesNotExist_ReturnsSuccessFalse()
        {
            // Arrange
            var repositoryName = "TestRepo";
            var pullRequestId = "456";
            var expectedResourceGroupName = "rg-TestRepo-pr-456";

            var mockSubscription = new Mock<SubscriptionResource>();
            var mockResourceGroupContainer = new Mock<ResourceGroupCollection>();

            _mockArmClient
                .Setup(c => c.GetDefaultSubscriptionAsync())
                .ReturnsAsync(mockSubscription.Object);

            mockSubscription
                .Setup(s => s.GetResourceGroups())
                .Returns(mockResourceGroupContainer.Object);

            mockResourceGroupContainer
                .Setup(c => c.ExistsAsync(expectedResourceGroupName))
                .ReturnsAsync(Response.FromValue(false, new Mock<Response>().Object));

            // Act
            var result = await _resourceGroupManager.DeleteResourceGroupAsync(repositoryName, pullRequestId);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.False(result.Value);
            mockResourceGroupContainer.Verify(c => c.GetAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task DeleteResourceGroupAsync_When404Exception_ReturnsSuccessFalse()
        {
            // Arrange
            var repositoryName = "TestRepo";
            var pullRequestId = "789";
            var expectedResourceGroupName = "rg-TestRepo-pr-789";

            var mockSubscription = new Mock<SubscriptionResource>();
            var mockResourceGroupContainer = new Mock<ResourceGroupCollection>();

            _mockArmClient
                .Setup(c => c.GetDefaultSubscriptionAsync())
                .ReturnsAsync(mockSubscription.Object);

            mockSubscription
                .Setup(s => s.GetResourceGroups())
                .Returns(mockResourceGroupContainer.Object);

            mockResourceGroupContainer
                .Setup(c => c.ExistsAsync(expectedResourceGroupName))
                .ThrowsAsync(new Azure.RequestFailedException(404, "Not found"));

            // Act
            var result = await _resourceGroupManager.DeleteResourceGroupAsync(repositoryName, pullRequestId);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.False(result.Value);
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public async Task DeleteResourceGroupAsync_WhenDeleteFails_ReturnsFailure()
        {
            // Arrange
            var repositoryName = "TestRepo";
            var pullRequestId = "999";
            var expectedResourceGroupName = "rg-TestRepo-pr-999";
            var errorMessage = "Delete operation failed";

            var mockSubscription = new Mock<SubscriptionResource>();
            var mockResourceGroupContainer = new Mock<ResourceGroupCollection>();
            var mockResourceGroup = new Mock<ResourceGroupResource>();

            _mockArmClient
                .Setup(c => c.GetDefaultSubscriptionAsync())
                .ReturnsAsync(mockSubscription.Object);

            mockSubscription
                .Setup(s => s.GetResourceGroups())
                .Returns(mockResourceGroupContainer.Object);

            mockResourceGroupContainer
                .Setup(c => c.ExistsAsync(expectedResourceGroupName))
                .ReturnsAsync(Response.FromValue(true, new Mock<Response>().Object));

            mockResourceGroupContainer
                .Setup(c => c.GetAsync(expectedResourceGroupName))
                .ReturnsAsync(Response.FromValue(mockResourceGroup.Object, new Mock<Response>().Object));

            mockResourceGroup
                .Setup(rg => rg.DeleteAsync(Azure.WaitUntil.Started))
                .Throws(new Azure.RequestFailedException(errorMessage));

            // Act
            var result = await _resourceGroupManager.DeleteResourceGroupAsync(repositoryName, pullRequestId);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains(errorMessage, result.ErrorMessage);
        }

        [Fact]
        public async Task DeleteResourceGroupAsync_WhenGetSubscriptionFails_ReturnsFailure()
        {
            // Arrange
            var repositoryName = "TestRepo";
            var pullRequestId = "111";

            _mockArmClient
                .Setup(c => c.GetDefaultSubscriptionAsync())
                .ThrowsAsync(new InvalidOperationException("Failed to get subscription"));

            // Act
            var result = await _resourceGroupManager.DeleteResourceGroupAsync(repositoryName, pullRequestId);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains("Failed to get subscription", result.ErrorMessage);
        }

        [Fact]
        public async Task DeleteResourceGroupAsync_WhenExistsThrowsUnexpectedException_ReturnsFailure()
        {
            // Arrange
            var repositoryName = "TestRepo";
            var pullRequestId = "222";
            var expectedResourceGroupName = "rg-TestRepo-pr-222";

            var mockSubscription = new Mock<SubscriptionResource>();
            var mockResourceGroupContainer = new Mock<ResourceGroupCollection>();

            _mockArmClient
                .Setup(c => c.GetDefaultSubscriptionAsync())
                .ReturnsAsync(mockSubscription.Object);

            mockSubscription
                .Setup(s => s.GetResourceGroups())
                .Returns(mockResourceGroupContainer.Object);

            mockResourceGroupContainer
                .Setup(c => c.ExistsAsync(expectedResourceGroupName))
                .ThrowsAsync(new Azure.RequestFailedException(500, "Server error"));

            // Act
            var result = await _resourceGroupManager.DeleteResourceGroupAsync(repositoryName, pullRequestId);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.ErrorMessage);
        }

        #endregion

        #region Resource Group Name Construction Tests

        [Fact]
        public async Task DeleteResourceGroupAsync_ConstructsCorrectResourceGroupName()
        {
            // Arrange
            var repositoryName = "MyTestRepository";
            var pullRequestId = "12345";
            var expectedResourceGroupName = "rg-MyTestRepository-pr-12345";

            var mockSubscription = new Mock<SubscriptionResource>();
            var mockResourceGroupContainer = new Mock<ResourceGroupCollection>();

            _mockArmClient
                .Setup(c => c.GetDefaultSubscriptionAsync())
                .ReturnsAsync(mockSubscription.Object);

            mockSubscription
                .Setup(s => s.GetResourceGroups())
                .Returns(mockResourceGroupContainer.Object);

            mockResourceGroupContainer
                .Setup(c => c.ExistsAsync(expectedResourceGroupName))
                .ReturnsAsync(Response.FromValue(false, new Mock<Response>().Object));

            // Act
            await _resourceGroupManager.DeleteResourceGroupAsync(repositoryName, pullRequestId);

            // Assert
            mockResourceGroupContainer.Verify(
                c => c.ExistsAsync(expectedResourceGroupName),
                Times.Once,
                $"Expected ExistsAsync to be called with {expectedResourceGroupName}");
        }

        [Fact]
        public async Task DeleteResourceGroupAsync_WithSpecialCharactersInNames_ConstructsCorrectName()
        {
            // Arrange
            var repositoryName = "my-test-repo";
            var pullRequestId = "pr-999";
            var expectedResourceGroupName = "rg-my-test-repo-pr-pr-999";

            var mockSubscription = new Mock<SubscriptionResource>();
            var mockResourceGroupContainer = new Mock<ResourceGroupCollection>();

            _mockArmClient
                .Setup(c => c.GetDefaultSubscriptionAsync())
                .ReturnsAsync(mockSubscription.Object);

            mockSubscription
                .Setup(s => s.GetResourceGroups())
                .Returns(mockResourceGroupContainer.Object);

            mockResourceGroupContainer
                .Setup(c => c.ExistsAsync(expectedResourceGroupName))
                .ReturnsAsync(Response.FromValue(false, new Mock<Response>().Object));

            // Act
            await _resourceGroupManager.DeleteResourceGroupAsync(repositoryName, pullRequestId);

            // Assert
            mockResourceGroupContainer.Verify(
                c => c.ExistsAsync(expectedResourceGroupName),
                Times.Once);
        }

        #endregion

        #region Logging Tests

        [Fact]
        public async Task DeleteResourceGroupAsync_LogsInformationWhenResourceGroupNotFound()
        {
            // Arrange
            var repositoryName = "TestRepo";
            var pullRequestId = "123";
            var expectedResourceGroupName = "rg-TestRepo-pr-123";

            var mockSubscription = new Mock<SubscriptionResource>();
            var mockResourceGroupContainer = new Mock<ResourceGroupCollection>();

            _mockArmClient
                .Setup(c => c.GetDefaultSubscriptionAsync())
                .ReturnsAsync(mockSubscription.Object);

            mockSubscription
                .Setup(s => s.GetResourceGroups())
                .Returns(mockResourceGroupContainer.Object);

            mockResourceGroupContainer
                .Setup(c => c.ExistsAsync(expectedResourceGroupName))
                .ReturnsAsync(Response.FromValue(false, new Mock<Response>().Object));

            // Act
            await _resourceGroupManager.DeleteResourceGroupAsync(repositoryName, pullRequestId);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("does not exist")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task DeleteResourceGroupAsync_LogsErrorWhenDeleteFails()
        {
            // Arrange
            var repositoryName = "TestRepo";
            var pullRequestId = "456";
            var expectedResourceGroupName = "rg-TestRepo-pr-456";

            var mockSubscription = new Mock<SubscriptionResource>();
            var mockResourceGroupContainer = new Mock<ResourceGroupCollection>();
            var mockResourceGroup = new Mock<ResourceGroupResource>();

            _mockArmClient
                .Setup(c => c.GetDefaultSubscriptionAsync())
                .ReturnsAsync(mockSubscription.Object);

            mockSubscription
                .Setup(s => s.GetResourceGroups())
                .Returns(mockResourceGroupContainer.Object);

            mockResourceGroupContainer
                .Setup(c => c.ExistsAsync(expectedResourceGroupName))
                .ReturnsAsync(Response.FromValue(true, new Mock<Response>().Object));

            mockResourceGroupContainer
                .Setup(c => c.GetAsync(expectedResourceGroupName))
                .ReturnsAsync(Response.FromValue(mockResourceGroup.Object, new Mock<Response>().Object));

            mockResourceGroup
                .Setup(rg => rg.DeleteAsync(Azure.WaitUntil.Started))
                .Throws(new Azure.RequestFailedException("Deletion failed"));

            // Act
            await _resourceGroupManager.DeleteResourceGroupAsync(repositoryName, pullRequestId);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error deleting")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        #endregion
    }
}

