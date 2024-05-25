using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace wtp.Remote
{
    public class RemoteManager
    {
        private IHost _host;
        private Dispatcher _dispatcher;
        public string _clientConnectionId;
        private string ServerAddress => $"http://localhost:{_wsPort}";

        private int _wsPort = 5005;
        public int WsPort
        {
            get => _wsPort;
            set
            {
                if (_wsPort != value && IsPortValid(value))
                {
                    _wsPort = value;
                    RestartServer().ConfigureAwait(false);
                }
            }
        }

        public event Action<string> MessageReceived;
        public event Action<string, bool> StateHasChanged;

        public string State { get; private set; } = "Initialized";
        public bool IsConnected { get; private set; } = false;
        public string LastRecievedMessage { get; private set; } = "";

        public RemoteManager(Action<string> messageReceived, Action<string, bool> stateHasChanged, int port)
        {
            Debug.WriteLine("RemoteManager initializing");
            MessageReceived = messageReceived;
            StateHasChanged = stateHasChanged;
            WsPort = port;
            _dispatcher = Dispatcher.CurrentDispatcher;

            StartSignalRServer().ConfigureAwait(false);
        }

        public bool IsPortValid(int port) => port >= 1 && port <= 65535;

        public async Task RestartServer()
        {
            await StopSignalRServer();
            await StartSignalRServer();
        }

        public void ChangeState(string status, bool isConnected)
        {
            State = status;
            IsConnected = isConnected;
            StateHasChanged?.Invoke(status, isConnected);
        }

        private async Task StartSignalRServer()
        {
            Debug.WriteLine("SignalRServer start");

            _host = CreateHostBuilder().Build();

            try
            {
                await _host.StartAsync();
                ChangeState("Server started", true);
                Console.WriteLine("SignalR server started.");
            }
            catch (IOException ex) when (ex.Message.Contains("address already in use"))
            {
                ChangeState("Port already in use", false);
                Console.WriteLine("Failed to start server: Port already in use.");
            }
            catch (Exception ex)
            {
                ChangeState("Server start failed", false);
                Console.WriteLine($"Failed to start server: {ex.Message}");
            }
        }

        private async Task StopSignalRServer()
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
                _host = null;
                ChangeState("Server stopped", false);
            }
        }

        public IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseUrls(ServerAddress)
                              .ConfigureServices(services =>
                              {
                                  services.AddCors(options =>
                                  {
                                      options.AddDefaultPolicy(builder =>
                                      {
                                          builder.AllowAnyOrigin()
                                                 .AllowAnyMethod()
                                                 .AllowAnyHeader();
                                      });
                                  });
                                  services.AddSignalR();
                                  services.AddSingleton<RemoteManager>(this); // Register RemoteManager as a singleton
                              })
                              .Configure(app =>
                              {
                                  app.UseRouting();
                                  app.UseCors();

                                  app.UseEndpoints(endpoints =>
                                  {
                                      endpoints.MapHub<ChatHub>("/chathub");
                                  });
                              });
                });

        public async Task TrySendingMessage(string type, string content)
        {
            var hubContext = _host.Services.GetService<IHubContext<ChatHub>>();
            if (hubContext != null)
            {
                var message = new { Type = type, Content = content };
                var messageJson = System.Text.Json.JsonSerializer.Serialize(message);

                try
                {
                    await hubContext.Clients.All.SendAsync("ReceiveMessage", "RemoteManager", messageJson);
                    Console.WriteLine("Message sent successfully.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to send message: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("HubContext is not available.");
            }
        }

        public void ProcessMessage(string msg)
        {
            Debug.WriteLine($"Processing message:\n{msg}");
            _dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrWhiteSpace(msg)) MessageReceived?.Invoke(msg);
            });
        }

        public async Task<string> GetChatDataFromClient()
        {
            Debug.WriteLine("GetChatDataFromClient called");
            var hubContext = _host.Services.GetService<IHubContext<ChatHub>>();
            Debug.WriteLine("hubContext recievbed");
            if (hubContext != null && !string.IsNullOrEmpty(_clientConnectionId))
            {
                try
                {
                    // Invoke the client method and return the result
                    var result = await hubContext.Clients.Client(_clientConnectionId).InvokeAsync<string>("getChatData", CancellationToken.None);
                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to call client method: {ex.Message}");
                    Debug.WriteLine($"Exception details: {ex}");
                    return null;
                }
            }
            else
            {
                Console.WriteLine("HubContext or client connection ID is not available.");
                return null;
            }
        }

    }

    public class ChatHub : Hub
    {
        private readonly RemoteManager _remoteManager;

        public ChatHub(RemoteManager remoteManager)
        {
            _remoteManager = remoteManager;
        }

        public async Task SendMessage(string user, string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", user, message);
            _remoteManager.ProcessMessage(message);
        }

        public async Task<string> Echo(string message)
        {
            return await Task.FromResult($"Echo: {message}");
        }

        public async Task<string> CallClientMethod(string message)
        {
            await Clients.Caller.SendAsync("ClientMethod", message);
            return await Task.FromResult($"Processed message: {message}");
        }

        // Новый метод для вызова getChatData на клиенте
        public async Task<string> WaitForMessage()
        {
            var message = await _remoteManager.GetChatDataFromClient();
            return message;
        }

        // Implement the KeepAlive method
        public async Task KeepAlive(string message)
        {
            await Task.CompletedTask; // Just acknowledging the keep-alive message
        }

        public override async Task OnConnectedAsync()
        {
            var connectionId = Context.ConnectionId;
            _remoteManager._clientConnectionId = connectionId;
            _remoteManager.ChangeState("Connected", true);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            _remoteManager._clientConnectionId = null;
            _remoteManager.ChangeState("Disconnected", false);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
