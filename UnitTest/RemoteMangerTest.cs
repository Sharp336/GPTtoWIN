using Xunit;
using Moq;
using Microsoft.AspNetCore.SignalR;
using wtp.Remote;
using System.Threading.Tasks;
using System.Threading;

public class RemoteManagerTests
{
    private readonly Mock<IHubContext<ChatHub>> _mockHubContext;
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly RemoteManager _remoteManager;

    public RemoteManagerTests()
    {
        _mockHubContext = new Mock<IHubContext<ChatHub>>();
        _mockClientProxy = new Mock<IClientProxy>();
        _remoteManager = new RemoteManager(msg => { }, (state, connected) => { }, 4005);

        _mockHubContext.Setup(h => h.Clients.All).Returns(_mockClientProxy.Object);
    }


    [Fact]
    public async Task Test_of_test()
    {
        // Assert
        Assert.Equal("Test running", "Test running");
    }

    [Fact]
    public async Task ChangeSignalRServerPort_ShouldChangeStateToStarted()
    {
        // Act
        _remoteManager.WsPort = 5005;

        // Assert
        Assert.Equal("Server started", _remoteManager.State);
        Assert.True(_remoteManager.IsConnected);
    }
}

