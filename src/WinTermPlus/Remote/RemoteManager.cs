using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using wtp.Ui;
using Newtonsoft.Json;

namespace wtp.Remote
{
    internal class RemoteManager
    {
        private HttpListener _httpListener;
        private WebSocket _currentWebSocket;

        private const int BufferSize = 4096;
        private const string KeepAliveMessage = "keep-alive";

        private CancellationTokenSource _cts = new CancellationTokenSource();

        public event Action<string> MessageReceived;
        public event Action<string, bool> StateHasChanged;

        public string State { get; private set; } = "Initialized";
        public bool IsConnected { get; private set; } = false;
        public string LastRecievedMessage = "";


        private int _wsPort = 5001;
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

        private class WebSocketMessage
        {
            public string Type { get; set; }
            public string Content { get; set; }
        }

        public RemoteManager(Action<string> messageReceived, Action<string, bool> stateHasChanged, int wsPort = 0)
        {
            MessageReceived = messageReceived;
            StateHasChanged = stateHasChanged;

            if( IsPortValid(wsPort) ) _wsPort = wsPort;

            StartWebSocketServer().ConfigureAwait(false);
        }

        private void ChangeState(string status, bool isConnected)
        {
            State = status;
            IsConnected = isConnected;
            StateHasChanged?.Invoke(status, isConnected);
        }

        public bool IsPortValid(int port) => port >= 1 && port <= 65535;

        private async Task StartWebSocketServer()
        {
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://localhost:{WsPort}/");
                _httpListener.Start();

                ChangeState("Started", true); 

                await AcceptClients(_httpListener);
            }
            catch (HttpListenerException ex)
            {
                Debug.WriteLine("Exception in StartWebSocketServer: " + ex.ToString());
                string newStatus = ex.ErrorCode == 183 || ex.ErrorCode == 32 ? "Port busy" : "Start error"; // проверка на ERROR_ALREADY_EXISTS или ERROR_SHARING_VIOLATION
                ChangeState(newStatus, false);
                
                // Важно обнулить _httpListener, чтобы избежать работы с некорректным объектом
                _httpListener = null;
            }
        }


        public async Task RestartServer()
        {
            await StopWebSocketServer();
            await StartWebSocketServer();
        }

        private async Task StopWebSocketServer()
        {
            if (_currentWebSocket != null && _currentWebSocket.State == WebSocketState.Open)
            {
                await _currentWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server stopping", CancellationToken.None);
                _currentWebSocket = null;
            }

            _cts.Cancel();
            _cts = new CancellationTokenSource();

            if (_httpListener != null)
            {
                try
                {
                    if (_httpListener.IsListening)
                    {
                        _httpListener.Stop(); // Остановка сервера
                    }
                }
                catch (ObjectDisposedException ex)
                {
                    Debug.WriteLine($"HttpListener was already disposed: {ex.Message}");
                }
                finally
                {
                    _httpListener.Close();
                    _httpListener = null;
                }
            }
        }




        private async Task AcceptClients(HttpListener httpListener)
        {
            ChangeState("Awaiting connection", false);
            while (true)
            {
                try
                {
                    // Ожидаем подключение с возможностью отмены
                    var getContextTask = httpListener.GetContextAsync();
                    var completedTask = await Task.WhenAny(getContextTask, Task.Delay(-1, _cts.Token));
                    if (completedTask == getContextTask)
                    {
                        // Продолжаем, если было получено подключение
                        var httpContext = await getContextTask;
                        if (httpContext.Request.IsWebSocketRequest)
                        {
                            var webSocketContext = await httpContext.AcceptWebSocketAsync(null);
                            _currentWebSocket = webSocketContext.WebSocket;
                            var handleWebSocketTask = HandleWebSocketConnection(_currentWebSocket);
                            ChangeState("Extension connected", true);
                        }
                        else
                        {
                            httpContext.Response.StatusCode = 400;
                            httpContext.Response.Close();
                            ChangeState("Awaiting connection", false);
                        }
                    }
                    else
                    {
                        // Завершаем, если операция была отменена
                        break;
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
                {
                    // Обработка отмены операции или закрытия HttpListener
                    Debug.WriteLine("Operation was canceled or HttpListener was closed.");
                    ChangeState("Error accepting client", false);
                    break;
                }
            }
        }


        private async Task HandleWebSocketConnection(WebSocket webSocket)
        {
            var buffer = new byte[BufferSize];
            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        ChangeState("Extension disconnected", false);
                    }
                    else if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var clientMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Debug.WriteLine($"Recieved message:\n{clientMessage}");
                        // Process JSON message
                        try
                        {
                            var messageObject = JsonConvert.DeserializeObject<WebSocketMessage>(clientMessage);
                            switch (messageObject.Type)
                            {
                                case "keepAlive":
                                    // Process keep-alive message
                                    break;
                                default:
                                    LastRecievedMessage = messageObject.Content;
                                    MessageReceived(messageObject.Content);
                                    break;
                            }
                        }
                        catch (JsonException jsonEx)
                        {
                            Debug.WriteLine($"JSON parsing error: {jsonEx.Message}");
                            // Optionally send error message back to client
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception in HandleWebSocketConnection: " + ex.ToString());
                ChangeState("Error handling connection", false);
            }
        }


        public async Task TrySendingMessage(string type, string content)
        {
            if (_currentWebSocket != null && _currentWebSocket.State == WebSocketState.Open)
            {
                var message = new WebSocketMessage
                {
                    Type = type,
                    Content = content
                };
                var messageJson = JsonConvert.SerializeObject(message);  // Serialize the message object to JSON
                var messageBytes = Encoding.UTF8.GetBytes(messageJson);   // Convert the JSON string to bytes

                Debug.WriteLine($"\nSending message to client: {messageJson}");

                try
                {
                    await _currentWebSocket.SendAsync(new ArraySegment<byte>(messageBytes, 0, messageBytes.Length), WebSocketMessageType.Text, true, CancellationToken.None);
                    Debug.WriteLine("Message sent successfully.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to send message: {ex.Message}");
                }
            }
            else
            {
                Debug.WriteLine("WebSocket is not connected or the message is null/empty.");
            }
        }
    }
}
