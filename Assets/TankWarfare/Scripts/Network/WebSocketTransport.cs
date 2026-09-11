using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#if !UNITY_WEBGL || UNITY_EDITOR
using System.Net.WebSockets;
#endif

namespace TankWarfare.Network
{
    /// <summary>Small WebSocket adapter for both the browser and the Unity Editor.</summary>
    public sealed class WebSocketTransport : MonoBehaviour
    {
        public event Action Opened;
        public event Action<string> MessageReceived;
        public event Action<string> Failed;
        public event Action Closed;

        private readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();
        private bool isOpen;

#if UNITY_WEBGL && !UNITY_EDITOR
        private int socketId = -1;

        [DllImport("__Internal")] private static extern int TW_WebSocketConnect(string url, string objectName);
        [DllImport("__Internal")] private static extern void TW_WebSocketSend(int id, string message);
        [DllImport("__Internal")] private static extern void TW_WebSocketClose(int id);
#else
        private ClientWebSocket socket;
        private CancellationTokenSource cancellation;
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
#endif

        public bool IsOpen => isOpen;

        public void Connect(string url)
        {
            Close();
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || (uri.Scheme != "ws" && uri.Scheme != "wss"))
            {
                Failed?.Invoke("Адрес сервера должен начинаться с ws:// или wss://");
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            socketId = TW_WebSocketConnect(url, gameObject.name);
            if (socketId < 0)
                Failed?.Invoke("Браузер не смог создать WebSocket");
#else
            cancellation = new CancellationTokenSource();
            socket = new ClientWebSocket();
            _ = ConnectDesktopAsync(uri, cancellation.Token);
#endif
        }

        public void Send(string message)
        {
            if (!isOpen || string.IsNullOrEmpty(message))
                return;

#if UNITY_WEBGL && !UNITY_EDITOR
            TW_WebSocketSend(socketId, message);
#else
            _ = SendDesktopAsync(message);
#endif
        }

        public void Close()
        {
            isOpen = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (socketId >= 0)
                TW_WebSocketClose(socketId);
            socketId = -1;
#else
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
            socket?.Dispose();
            socket = null;
#endif
        }

        private void Update()
        {
            while (mainThread.TryDequeue(out Action action))
                action.Invoke();
        }

        private void OnDestroy() => Close();

#if UNITY_WEBGL && !UNITY_EDITOR
        // Called by the .jslib bridge through SendMessage.
        public void OnWebSocketOpen(string ignored)
        {
            isOpen = true;
            Opened?.Invoke();
        }

        public void OnWebSocketMessage(string message) => MessageReceived?.Invoke(message);
        public void OnWebSocketError(string message) => Failed?.Invoke(message);
        public void OnWebSocketClose(string ignored)
        {
            isOpen = false;
            Closed?.Invoke();
        }
#else
        private async Task ConnectDesktopAsync(Uri uri, CancellationToken token)
        {
            try
            {
                await socket.ConnectAsync(uri, token);
                mainThread.Enqueue(() =>
                {
                    isOpen = true;
                    Opened?.Invoke();
                });
                await ReceiveDesktopAsync(token);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                mainThread.Enqueue(() => Failed?.Invoke(exception.Message));
            }
        }

        private async Task SendDesktopAsync(string message)
        {
            bool lockTaken = false;
            try
            {
                await sendLock.WaitAsync(cancellation?.Token ?? CancellationToken.None);
                lockTaken = true;
                byte[] bytes = Encoding.UTF8.GetBytes(message);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                    cancellation?.Token ?? CancellationToken.None);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                mainThread.Enqueue(() => Failed?.Invoke(exception.Message));
            }
            finally
            {
                if (lockTaken) sendLock.Release();
            }
        }

        private async Task ReceiveDesktopAsync(CancellationToken token)
        {
            var buffer = new byte[16 * 1024];
            var builder = new StringBuilder();

            while (socket != null && socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        mainThread.Enqueue(() =>
                        {
                            isOpen = false;
                            Closed?.Invoke();
                        });
                        return;
                    }

                    builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                string text = builder.ToString();
                builder.Clear();
                mainThread.Enqueue(() => MessageReceived?.Invoke(text));
            }
        }
#endif
    }
}
