using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Moq;
using VideoOzet.API.Hubs;
using Xunit;

namespace VideoOzet.UnitTests.API.Hubs;

public class NotificationHubTests
{
    [Fact]
    public async Task OnConnectedAsync_ShouldAddToGroup_WhenUserIdentifierExists()
    {
        // Arrange
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(c => c.UserIdentifier).Returns("user-456");
        mockContext.Setup(c => c.ConnectionId).Returns("conn-xyz");

        var mockGroups = new Mock<IGroupManager>();
        mockGroups.Setup(g => g.AddToGroupAsync("conn-xyz", "user-user-456", It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        var hub = new NotificationHub
        {
            Context = mockContext.Object,
            Groups = mockGroups.Object
        };

        // Act
        await hub.OnConnectedAsync();

        // Assert
        mockGroups.Verify(g => g.AddToGroupAsync("conn-xyz", "user-user-456", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_ShouldNotAddToGroup_WhenUserIdentifierIsNull()
    {
        // Arrange
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(c => c.UserIdentifier).Returns((string?)null);
        mockContext.Setup(c => c.ConnectionId).Returns("conn-xyz");

        var mockGroups = new Mock<IGroupManager>();

        var hub = new NotificationHub
        {
            Context = mockContext.Object,
            Groups = mockGroups.Object
        };

        // Act
        await hub.OnConnectedAsync();

        // Assert
        mockGroups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
