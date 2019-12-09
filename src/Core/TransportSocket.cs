using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

#nullable enable
namespace SocksNet
{
    public enum TransportType
    {
        Tcp,
        Udp
    }

    public static class SocketExtensions
    {
        public static TcpSocket ToTcpSocket(this Socket s) => new TcpSocket(s);
        public static UdpSocket ToUdpSocket(this Socket s) => new UdpSocket(s);
    }

    public abstract class TransportSocket : IDisposable
    {
        public Socket UnderlyingSocket { get; private set; }
        public TransportType Type { get; private set; }

        public TransportSocket(Socket underylingSocket, TransportType type)
        {
            UnderlyingSocket = underylingSocket;
            Type = type;
        }

        public abstract void Dispose();

        public bool Connected => UnderlyingSocket.Connected;

        public EndPoint? LocalEndPoint => UnderlyingSocket.LocalEndPoint;
        public abstract EndPoint? RemoteEndPoint { get; }

        public abstract void Bind(EndPoint localEP);
        public abstract TransportSocket CreateNewOfSameType();
        public abstract void Listen(int maxConnections);
        public abstract Task<TransportSocket> AcceptAsync();
        public abstract ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
        public abstract ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
        public abstract Task<TransportSocket> ConnectAsync(IPAddress address, int port);
        public abstract Task<TransportSocket> ConnectAsync(string host, int port);
        public abstract void Disconnect(bool reuseSocket);
    }

    public class TcpSocket : TransportSocket
    {
        public TcpSocket() : base(new Socket(SocketType.Stream, ProtocolType.Tcp), TransportType.Tcp)
        {

        }

        public TcpSocket(Socket socket) : base(socket, TransportType.Tcp)
        {

        }

        public override void Dispose() => UnderlyingSocket.Dispose();

        public override TransportSocket CreateNewOfSameType()
        {
            return new TcpSocket();
        }

        public override EndPoint? RemoteEndPoint => UnderlyingSocket.RemoteEndPoint;

        public override void Bind(EndPoint localEP) =>
            UnderlyingSocket.Bind(localEP);

        public override void Listen(int backlog) => 
            UnderlyingSocket.Listen(backlog);

        public override async Task<TransportSocket> AcceptAsync() => 
            (await UnderlyingSocket.AcceptAsync()).ToTcpSocket();

        public override ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => 
            UnderlyingSocket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);

        public override ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => 
            UnderlyingSocket.SendAsync(buffer, SocketFlags.None);

        public override async Task<TransportSocket> ConnectAsync(IPAddress address, int port)
        {
            await UnderlyingSocket.ConnectAsync(address, port);
            return this;
        }

        public override async Task<TransportSocket> ConnectAsync(string host, int port)
        {
            await UnderlyingSocket.ConnectAsync(host, port);
            return this;
        }

        public override void Disconnect(bool reuseSocket) => 
            UnderlyingSocket.Disconnect(reuseSocket);
    }

    // This whole class is a (marginally clever) attempt at modeling UDP sockets as TCP sockets so 
    // I can reuse code everywhere else.  It is also an abomination: please avert your eye-holes.

    public class UdpSocket : TransportSocket
    {
        // Generic UDP socket properties.
        
        public Channel<Memory<byte>> AwaitingReceives { get; } = Channel.CreateUnbounded<Memory<byte>>();
        public IPEndPoint? TrackedRemote { get; private set; }

        // Properties of UDP sockets that are "serveresque".

        public Channel<UdpSocket> AwaitingAccepts { get; } = Channel.CreateUnbounded<UdpSocket>();
        public bool ActsAsServer { get; private set; } = false;
        public Task? ServerTask { get; private set; }
        public CancellationTokenSource? ServerTaskCancellationTokenSource { get; private set; }
        public ConcurrentDictionary<IPEndPoint, UdpSocket>? Clients { get; private set; }

        public UdpSocket() : base(new Socket(SocketType.Dgram, ProtocolType.Udp), TransportType.Udp)
        {

        }

        public UdpSocket(Socket socket) : base(socket, TransportType.Udp)
        {

        }

        public override void Dispose()
        {
            if(!this.ActsAsServer)
                return; // NO-OP for client sockets.

            this.Disconnect(false);
            UnderlyingSocket.Dispose();
        }

        public override TransportSocket CreateNewOfSameType() =>
            new UdpSocket();

        public override EndPoint? RemoteEndPoint => 
            TrackedRemote;

        public override void Bind(EndPoint localEP)
        {
            if(this.ActsAsServer)
                throw new Exception("Cannot call {nameof(Bind)} multiple times.");

            UnderlyingSocket.Bind(localEP);

            this.ActsAsServer = true;
            this.Clients = new ConcurrentDictionary<IPEndPoint, UdpSocket>();
            this.ServerTaskCancellationTokenSource = new CancellationTokenSource();

            // All "receives" should be handled by this "server" instance.  For now, let's try one background thread.
            ServerTask = Task.Run(async() =>
            {
                try
                {
                    var buffer = new byte[4096];
                
                    while(true)
                    {
                        // TODO: There should be a TTL on "acting as server" sockets that are "connected" to remote endpoints, since
                        // UDP clients won't ever request a "close".
                        var receive = await UnderlyingSocket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0 /* any */));
                        var read = receive.ReceivedBytes;
                        var remoteEndpoint = (IPEndPoint)receive.RemoteEndPoint;

                        var memory = new byte[read].AsMemory();
                        buffer.AsMemory().Slice(0, read).CopyTo(memory);

                        // See if this is for a "client" socket; if so, queue up for that socket's receive.

                        if(Clients.TryGetValue(remoteEndpoint, out UdpSocket? actualReceiver))
                        {
                            await actualReceiver.AwaitingReceives.Writer.WriteAsync(memory, ServerTaskCancellationTokenSource.Token);
                            continue;
                        }

                        // Otherwise, queue up for "self" in awaiting accepts.

                        var acceptingSocket = new UdpSocket(UnderlyingSocket) 
                        {
                            TrackedRemote = remoteEndpoint
                        };
                        await acceptingSocket.AwaitingReceives.Writer.WriteAsync(memory);

                        await this.AwaitingAccepts.Writer.WriteAsync(acceptingSocket, ServerTaskCancellationTokenSource.Token);
                    }
                }
                catch(Exception e)
                {
                    Console.WriteLine(e.Message);
                    throw e;
                }
                
            }, ServerTaskCancellationTokenSource.Token);
        }

        public override void Listen(int maxConnections)
        {
            // NO-OP!
        }

        public override async Task<TransportSocket> AcceptAsync()
        {
            if(!ActsAsServer || this.Clients == null)
                throw new Exception($"Only server UDP sockets may call {nameof(AcceptAsync)}.");

            var socketToAccept = await AwaitingAccepts.Reader.ReadAsync();

            if(socketToAccept.TrackedRemote == null)
                throw new Exception("An accepting socket should have a tracked remote.");

            if(Clients.TryAdd(socketToAccept.TrackedRemote, socketToAccept))
                return socketToAccept;

            throw new Exception("Could not track accepted connection suring accept.");
            
        }

        public override async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var awaitingReceive = await AwaitingReceives.Reader.ReadAsync(cancellationToken);

            awaitingReceive.CopyTo(buffer);
            return awaitingReceive.Length;
        }

        public override ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(UnderlyingSocket.SendToAsync(buffer.ToArray(), SocketFlags.None, TrackedRemote));
        }

        public override Task<TransportSocket> ConnectAsync(IPAddress address, int port)
        {
            if(!ActsAsServer || this.Clients == null)
                throw new Exception($"Only server UDP sockets may call {nameof(ConnectAsync)}.");

            var newSocket = new UdpSocket(UnderlyingSocket) 
            {
                TrackedRemote = new IPEndPoint(address.MapToIPv6(), port)
            };

            if(!this.Clients.TryAdd(newSocket.TrackedRemote, newSocket))
                throw new Exception("Unable to add new socket to client map.");

            return Task<TransportSocket>.FromResult((TransportSocket)newSocket);
        }

        public override async Task<TransportSocket> ConnectAsync(string host, int port)
        {
            // TODO: What if the host is only available on a specific interface?!?
            var addresses = await Dns.GetHostAddressesAsync(host);

            // Use the first address for now because lazy.
            return await ConnectAsync(addresses[0], port);
        }

        public override void Disconnect(bool reuseSocket)
        {
            if(!ActsAsServer)
                return; // NO-OP for "client" sockets.

            // Clear all clients.
            this.Clients?.Clear();

            // Cancel listening thread.
            ServerTaskCancellationTokenSource?.Cancel();

            // Disconnect underlying socket.
            UnderlyingSocket.Disconnect(reuseSocket);
        }
    }
}