﻿using System;
using System.Collections.Generic;
using System.IO;
using Fleck2.Interfaces;

namespace Fleck2
{
    public class WebSocketConnection : IWebSocketConnection
    {
        public WebSocketConnection(ISocket socket, Action<IWebSocketConnection> initialize, 
            FleckExtensions.Func<byte[], WebSocketHttpRequest> parseRequest, 
            FleckExtensions.Func<WebSocketHttpRequest, IHandler> handlerFactory)
        {
            Socket = socket;
            OnOpen = () => { };
            OnClose = () => { };
            OnMessage = x => { };
            OnBinary = x => { };
            OnError = x => { };
            _initialize = initialize;
            _handlerFactory = handlerFactory;
            _parseRequest = parseRequest;
        }

        public ISocket Socket { get; set; }

        private readonly Action<IWebSocketConnection> _initialize;
        private readonly FleckExtensions.Func<WebSocketHttpRequest, IHandler> _handlerFactory;
        readonly FleckExtensions.Func<byte[], WebSocketHttpRequest> _parseRequest;
        public IHandler Handler { get; set; }
        private bool _closed;
        private const int ReadSize = 1024 * 4;

        public FleckExtensions.Action OnOpen { get; set; }
        public FleckExtensions.Action OnClose { get; set; }
        public Action<string> OnMessage { get; set; }
        public Action<byte[]> OnBinary { get; set; }
        public Action<Exception> OnError { get; set; }
        public IWebSocketConnectionInfo ConnectionInfo { get; private set; }

        public void Send(string message)
        {
            if (Handler == null)
                throw new InvalidOperationException("Cannot send before handshake");

            if (_closed || !Socket.Connected)
            {
                FleckLog.Warn("Data sent after close. Ignoring.");
                return;
            }

            var bytes = Handler.FrameText(message);
            SendBytes(bytes);
        }
        
        public void Send(byte[] message)
        {
            if (Handler == null)
                throw new InvalidOperationException("Cannot send before handshake");

            if (_closed || !Socket.Connected)
            {
                FleckLog.Warn("Data sent after close. Ignoring.");
                return;
            }

            var bytes = Handler.FrameBinary(message);
            SendBytes(bytes);
        }

        public void StartReceiving()
        {
            var data = new List<byte>(ReadSize);
            var buffer = new byte[ReadSize];
            Read(data, buffer);
        }
        
        public void Close()
        {
            Close(WebSocketStatusCodes.NormalClosure);
        }

        public void Close(int code)
        {
            if (Handler == null)
            {
                CloseSocket();
                return;
            }

            var bytes = Handler.FrameClose(code);
            if (bytes.Length == 0)
                CloseSocket();
            else
                SendBytes(bytes, CloseSocket);
        }

        public void CreateHandler(IEnumerable<byte> data)
        {
            var request = _parseRequest(data.ToArray());
            if (request == null)
                return;
            Handler = _handlerFactory(request);
            if (Handler == null)
                return;
            ConnectionInfo = WebSocketConnectionInfo.Create(request, Socket.RemoteIpAddress, Socket.RemotePort);

            _initialize(this);

            var handshake = Handler.CreateHandshake();
            SendBytes(handshake, OnOpen);
        }


        private void Read(List<byte> data, byte[] buffer)
        {
            if (_closed || !Socket.Connected)
                return;
            Socket.Receive(buffer, r =>
            {
                if (r <= 0)
                {
                    FleckLog.Debug("0 bytes read. Closing.");
                    CloseSocket();
                    return;
                }
                FleckLog.Debug(r + " bytes read");
                byte[] readBytes = buffer.Take(r).ToArray();

                if (Handler != null)
                {
                    Handler.Receive(readBytes);
                }
                else
                {
                    data.AddRange(readBytes);
                    CreateHandler(data);
                }
                
                Read(data, buffer);
            },
            HandleReadError);
        }
        
        private void HandleReadError(Exception e)
        {
           
            if (e is ObjectDisposedException)
            {
                FleckLog.Debug("Swallowing ObjectDisposedException", e);
                return;
            }
            
            OnError(e);
            
            if (e is HandshakeException)
            {
                FleckLog.Debug("Error while reading", e);
            }
            else if (e is WebSocketException)
            {
                FleckLog.Debug("Error while reading", e);
                Close(((WebSocketException)e).StatusCode);
            }
            else if (e is IOException)
            {
                FleckLog.Debug("Error while reading", e);
                Close(WebSocketStatusCodes.AbnormalClosure);
            }
            else
            {
                FleckLog.Error("Application Error", e);
                Close(WebSocketStatusCodes.InternalServerError);
            }
        }

        private void SendBytes(byte[] bytes, FleckExtensions.Action callback = null)
        {
            Socket.Send(bytes, () =>
            {
                FleckLog.Debug("Sent " + bytes.Length + " bytes");
                if (callback != null)
                    callback();
            },
            e =>
            {
                if (e is IOException)
                    FleckLog.Debug("Failed to send. Disconnecting.", e);
                else
                    FleckLog.Info("Failed to send. Disconnecting.", e);
                CloseSocket();
            });
        }

        private void CloseSocket()
        {
            OnClose();
            _closed = true;
            Socket.Close();
            Socket.Dispose();
        }
    }
}
