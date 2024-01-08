using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tterm.Ui;

namespace tterm.Remote
{
    internal class RemoteManager
    {
        private HttpListener _httpListener;
        private WebSocket _currentWebSocket;

        private const int BufferSize = 4096;
        private const string KeepAliveMessage = "keep-alive";

        public event Action<string> MessageReceived;
        public event Action<string, bool> StateHasChanged;

        public RemoteManager()
        {
            StartWebSocketServer();
        }

        private async void StartWebSocketServer()
        {
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add("http://localhost:5000/");
                _httpListener.Start();

                await AcceptClients(_httpListener);
            }
            catch (Exception ex)
            {
                // Log the exception
                Debug.WriteLine("Exception in StartWebSocketServer: " + ex.ToString());
                StateHasChanged("Error while starting", false);
            }
        }

        private async Task AcceptClients(HttpListener httpListener)
        {
            while (true)
            {
                try
                {
                    var httpContext = await httpListener.GetContextAsync();
                    if (httpContext.Request.IsWebSocketRequest)
                    {
                        var webSocketContext = await httpContext.AcceptWebSocketAsync(null);
                        _currentWebSocket = webSocketContext.WebSocket;
                        var handleWebSocketTask = HandleWebSocketConnection(_currentWebSocket);
                        StateHasChanged("Extension connected", true);
                    }
                    else
                    {
                        httpContext.Response.StatusCode = 400;
                        httpContext.Response.Close();
                    }
                }
                catch (Exception ex)
                {
                    // Log the exception
                    Debug.WriteLine("Exception in AcceptClients: " + ex.ToString());
                    StateHasChanged("Error accepting client", false);
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
                        StateHasChanged("Extension disconnected", false);
                    }
                    else if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var clientMessage = Encoding.UTF8.GetString(buffer, 0, result.Count).Trim();
                        // Проверка на keep-alive сообщение
                        if (clientMessage != KeepAliveMessage) MessageReceived(clientMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the exception
                Debug.WriteLine("Exception in HandleWebSocketConnection: " + ex.ToString());
                StateHasChanged("Error handling connection", false);
            }
        }

        public async Task TrySendingMessage(string message)
        {
            Debug.WriteLine($"\nTrying to send:\n{message}");
            if (_currentWebSocket != null && _currentWebSocket.State == WebSocketState.Open && !string.IsNullOrEmpty(message))
            {
                Debug.WriteLine($"Sent message to client");
                var serverMessageBytes = Encoding.UTF8.GetBytes(message);
                await _currentWebSocket.SendAsync(new ArraySegment<byte>(serverMessageBytes, 0, serverMessageBytes.Length), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
    }
}
