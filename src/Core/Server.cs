using System;
using System.Buffers;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SocksNet
{
    public class Server
    {
        public IPAddress ListenInterface { get; private set; } = IPAddress.Any;
        public IPAddress EndpointInterface { get; private set; } = IPAddress.Any;
        public int Port { get; private set; } = 1080;
        public int MaxConnections { get; private set; } = 100;
        public int BufferSize { get; private set; } = Environment.SystemPageSize;

        public Server()
        {

        }

        public Server(IPAddress listenInterface, IPAddress endpointInterface, int port, int maxConnections, int bufferSize)
        {
            ListenInterface = listenInterface;
            EndpointInterface = endpointInterface;
            Port = port;
            MaxConnections = maxConnections;
            BufferSize = bufferSize;
        }

        private static Server CreateFrom(
            Server other,
            IPAddress? listenInterface = null,
            IPAddress? endpointInterface = null,
            int? port = null,
            int? maxConnections = null,
            int? bufferSize = null)
        {
            var server = new Server();
            server.ListenInterface = listenInterface ?? other.ListenInterface;
            server.EndpointInterface = endpointInterface ?? other.EndpointInterface;
            server.Port = port ?? other.Port;
            server.MaxConnections = maxConnections ?? other.MaxConnections;
            server.BufferSize = bufferSize ?? other.BufferSize;

            return server;
        }

        public static Server Instance => new Server();

        public Server WithListenInterface(IPAddress listenInterface) => CreateFrom(this, listenInterface: listenInterface);
        public Server WithEndpointInterface(IPAddress endpointInterface) => CreateFrom(this, endpointInterface: endpointInterface);
        public Server WithPort(int port) => CreateFrom(this, port: port);
        public Server WithMaxConnections(int maxConnections) => CreateFrom(this, maxConnections: maxConnections);
        public Server WithBufferSize(int bufferSize) => CreateFrom(this, bufferSize: bufferSize);

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            Helpers.LogLine("\nSettings:");
            Helpers.LogLine($"  Listen Interface: {ListenInterface}");
            Helpers.LogLine($"  Endpoint Interface: {EndpointInterface}");
            Helpers.LogLine($"  Listen Port: {Port}");
            Helpers.LogLine($"  Max Connections: {MaxConnections}");
            Helpers.LogLine($"  Buffer Size: {BufferSize}\n");

            var tcpServer = Task.Run(async () =>
            {
                var serverEndpoint = new IPEndPoint(ListenInterface, Port);
                var server = new TcpSocket();

                server.Bind(serverEndpoint);
                server.Listen(MaxConnections);
                
                Helpers.LogLine($"Listening for SOCKS5 connections at tcp://{serverEndpoint} ...\n");
                
                while(true)
                {
                    if(cancellationToken.IsCancellationRequested)
                        return;

                    var conn = await server.AcceptAsync();
                    Connection.From(conn, EndpointInterface, BufferSize).Handle();
                }
            });

            var udpServer = Task.Run(async () =>
            {
                var serverEndpoint = new IPEndPoint(ListenInterface, Port);
                var server = new UdpSocket();

                server.Bind(serverEndpoint);
                server.Listen(MaxConnections);
                
                Helpers.LogLine($"Listening for SOCKS5 connections at udp://{serverEndpoint} ...\n");
                
                while(true)
                {
                    if(cancellationToken.IsCancellationRequested)
                        return;

                    var conn = await server.AcceptAsync();
                    Connection.From(conn, EndpointInterface, BufferSize).Handle();
                }
            });

            await Task.WhenAll(tcpServer, udpServer);
        }
    }
}