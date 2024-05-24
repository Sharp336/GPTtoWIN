using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace wtp.Remote
{
    internal class RemoteManager
    {
        private IHost _host;
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
        public string LastRecieved { get; private set; } = "";

        public RemoteManager(Action<string> messageReceived, Action<string, bool> stateHasChanged, int port)
        {
            Debug.WriteLine("RemoteManager initializing");
            MessageReceived = messageReceived;
            StateHasChanged = stateHasChanged;
            WsPort = port;

            StartSignalRServer().ConfigureAwait(false);
        }

        public bool IsPortValid(int port) => port >= 1 && port <= 65535;

        public async Task RestartServer()
        {
            await StopSignalRServer();
            await StartSignalRServer();
        }

        private void ChangeState(string status, bool isConnected)
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
    }

    public class ChatHub : Hub
    {
        public async Task SendMessage(string user, string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", user, message);
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
    }
}
