using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Fleck2.Interfaces;

namespace Fleck2
{
    public class WebSocketServer : IWebSocketServer
    {
        private readonly string _scheme;
        private Action<IWebSocketConnection> _config;

        public WebSocketServer(string location)
            : this(8181, location, false)
        {
        }

        public WebSocketServer(string location, bool bindOnlyToLoopback)
            : this(8181, location, bindOnlyToLoopback)
        {
        }

        [Obsolete("Uri.Port default for ws:// has changed from 0 to 80 in recent .NET versions. Hence, supplying a port value independently of the location string can result in different behaviour in different client environments.")]
        public WebSocketServer(int port, string location)
            : this(port, location, false)
        {
        }

        public WebSocketServer(int port, string location, bool bindOnlyToLoopback)
        {
            var uri = new Uri(location);

            // uri.Port default for ws:// has changed from 0 to 80 in recent .NET versions 
            // so you should supply a custom port in the location string.
            Port = uri.Port > 0 ? uri.Port : port;
            BindOnlyToLoopback = bindOnlyToLoopback;
            Location = location;
            _scheme = uri.Scheme;
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            ListenerSocket = new SocketWrapper(socket);
            FleckLog.Debug("Constructed server at " + location + ". Explicit port: " + port + ". Implicit port: " + uri.Port + ". Loopback only? " + BindOnlyToLoopback, null);
        }

        public ISocket ListenerSocket { get; set; }
        public string Location { get; private set; }
        public int Port { get; private set; }
        public bool BindOnlyToLoopback { get; private set; }
        public X509Certificate2 Certificate { get; set; }

        public bool IsSecure
        {
            get { return _scheme == "wss" && Certificate != null; }
        }

        public void Dispose()
        {
            ListenerSocket.Dispose();
        }

        public void Start(Action<IWebSocketConnection> config)
        {
            try
            {
                FleckLog.Debug("Starting server at " + Location + ". Loopback only? " + BindOnlyToLoopback, null);
                var ipLocal = new IPEndPoint(BindOnlyToLoopback ? IPAddress.Loopback : IPAddress.Any, Port);
                ListenerSocket.Bind(ipLocal);
                ListenerSocket.Listen(100);
            }
            catch (SocketException sex)
            {
                if (sex.SocketErrorCode == SocketError.AccessDenied)
                    FleckLog.Warn("Starting server at " + Location
                            + " in enforced binding to any network address because windows denied us permission to bind to only localhost.", sex);
                else
                    FleckLog.Warn("Starting server at " + Location
                            + " in enforced binding to any network address because of a socket exception with code: " + sex.SocketErrorCode + ".", sex);

                try
                {
                    var ipAny = new IPEndPoint(IPAddress.Any, Port);
                    ListenerSocket.Bind(ipAny);
                    ListenerSocket.Listen(100);
                }
                catch (SocketException sex2)
                {
                    FleckLog.Error("Server failed to start for " + Location
                        + ". Because of a socket exception with code: " + sex2.SocketErrorCode + ".", sex2);
                    throw;
                }
            }
            catch (Exception ex)
            {
                FleckLog.Error("Server failed to start for " + Location + ". Because of a general exception.", ex);
                throw;
            }
            FleckLog.Info("Server started at " + Location,null);
            if (_scheme == "wss")
            {
                if (Certificate == null)
                {
                    FleckLog.Error("Scheme cannot be 'wss' without a Certificate",null);
                    return;
                }
            }
            ListenForClients();
            _config = config;
        }

        private void ListenForClients()
        {
            ListenerSocket.Accept(OnClientConnect, e => FleckLog.Error("Listener socket is closed", e));
        }

        private void OnClientConnect(ISocket clientSocket)
        {
            FleckLog.Debug(String.Format("Client connected from {0}:{1}", clientSocket.RemoteIpAddress, clientSocket.RemotePort.ToString(CultureInfo.InvariantCulture)),null);
            ListenForClients();

            WebSocketConnection connection = null;

            connection = new WebSocketConnection(
                clientSocket,
                _config,
                bytes => RequestParser.Parse(bytes, _scheme),
                r => HandlerFactory.BuildHandler(r,
                                                 s => connection.OnMessage(s),
                                                 connection.Close,
                                                 b => connection.OnBinary(b)));

            if (IsSecure)
            {
                FleckLog.Debug("Authenticating Secure Connection",null);
                clientSocket
                    .Authenticate(Certificate,
                                  connection.StartReceiving,
                                  e => FleckLog.Warn("Failed to Authenticate", e));
            }
            else
            {
                connection.StartReceiving();
            }
        }
    }
}
