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

        public WebSocketServer(int port, string location)
            : this(8181, location, false)
        {
        }

        public WebSocketServer(int port, string location, bool bindOnlyToLoopback)
        {
            var uri = new Uri(location);
            Port = uri.Port > 0 ? uri.Port : port;
            BindOnlyToLoopback = bindOnlyToLoopback;
            Location = location;
            _scheme = uri.Scheme;
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
            ListenerSocket = new SocketWrapper(socket);
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

        public void Start_old(Action<IWebSocketConnection> config)		//Save old start method but renamed to Start_old
        {
            var ipLocal = new IPEndPoint(IPAddress.Any, Port);
            Start_old(config, IPAddress.Any);
        }

/*

 Add overload with `IPAddress` to `Start()`.	https://github.com/alastairs/Fleck2/commit/c1bb87804cc4c99c32df805f380fe957138bbdae

The `Start()` method kicks off a web socket listening on all bound IP
addresses for the machine, including public ones. Add an overload that
takes an `IPAddress` parameter to allow the web socket to be started on
the loopback interface only.

+ renamed to Start_old
*/
        public void Start_old(Action<IWebSocketConnection> config, IPAddress ipAddress)
        {
            var ipLocal = new IPEndPoint(ipAddress, Port);
            ListenerSocket.Bind(ipLocal);
            ListenerSocket.Listen(100);
            FleckLog.Info("Server started at " + Location);
            if (_scheme == "wss")
            {
                if (Certificate == null)
                {
                    FleckLog.Error("Scheme cannot be 'wss' without a Certificate");
                    return;
                }
            }
            ListenForClients();
            _config = config;
		}

        public void Start(Action<IWebSocketConnection> config)
        {
            var ipLocal = new IPEndPoint(BindOnlyToLoopback ? IPAddress.Loopback : IPAddress.Any, Port);
            ListenerSocket.Bind(ipLocal);
            ListenerSocket.Listen(100);
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
