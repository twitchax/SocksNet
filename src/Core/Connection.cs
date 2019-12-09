using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace SocksNet
{
    public class Connection : IDisposable
    {
        private Task? _handleSocksTask;
        private CancellationTokenSource? _handleSocksTaskCts;

        public string Id { get; }
        public TransportSocket ClientSocket { get; private set; }
        public TransportSocket? EndpointSocket { get; private set; }

        public IPAddress EndpointInterface { get; private set; }
        public int BufferSize { get; private set; }

        private Connection(TransportSocket clientSocket, IPAddress endpointInterface, int bufferSize)
        {
            Id = Guid.NewGuid().ToString().Substring(0, 8);

            ClientSocket = clientSocket;
            EndpointInterface = endpointInterface;
            BufferSize = bufferSize;
        }

        public void Dispose()
        {
            _handleSocksTaskCts?.Cancel();

            if(ClientSocket.Connected)
                ClientSocket.Disconnect(false);
            if(EndpointSocket?.Connected ?? false)
                EndpointSocket.Disconnect(false);
            
            ClientSocket?.Dispose();
            EndpointSocket?.Dispose();
        }

        public static Connection From(TransportSocket clientSocket, IPAddress endpointInterface, int bufferSize)
        {
            if(clientSocket == null)
                throw new ArgumentException($"Parameter `{nameof(clientSocket)}` cannot be `null`.");

            return new Connection(clientSocket, endpointInterface, bufferSize);
        }

        public void Handle(CancellationToken cancellationToken = default)
        {
            _handleSocksTaskCts = new CancellationTokenSource();
            var mergedCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _handleSocksTaskCts.Token).Token;

            _handleSocksTask = Task.Run(async () =>
            {
                try
                {
                    Helpers.LogLine($"[{Id}] Start.");

                    var buffer = MemoryPool<byte>.Shared.Rent(BufferSize).Memory;

                    await HandleHandshakeAsync(buffer, cancellationToken);
                    await HandleRequestAsync(buffer, cancellationToken);

                    if(EndpointSocket == null)
                        throw new Exception($"The {nameof(EndpointSocket)} was `null` after completed handshake.");

                    Console.WriteLine($"[{Id}]   Data Path: {ClientSocket.RemoteEndPoint} => {ClientSocket.LocalEndPoint} => {EndpointSocket.LocalEndPoint} => {EndpointSocket.RemoteEndPoint}");

                    await Pump.RunSequentialAsync(ClientSocket, EndpointSocket, BufferSize, cancellationToken);
                }
                catch(SocksException e)
                {
                    Helpers.LogLine($"[{Id}] ERROR: {e.Message}");
                }
                catch (Exception e)
                {
                    Helpers.LogLine($"[{Id}] EXCEPTION ({e.GetType().ToString()}): {e.Message}\n\n{e.StackTrace}");
                }
                finally
                {
                    Console.WriteLine($"[{Id}] End.");
                    this.Dispose();
                }
            }, mergedCancellationToken);
        }

        private async Task HandleHandshakeAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await ClientSocket.ReceiveAsync(buffer, cancellationToken);
            
            var handshake = Handshake.From(buffer.Span);

            Helpers.LogLine($"[{Id}]   Handshake:");
            Helpers.LogLine($"[{Id}]     Version: {handshake.Version}.");
            Helpers.LogLine($"[{Id}]     Num Methods: {handshake.NumMethods}.");
            Helpers.LogLine($"[{Id}]     Methods: {string.Join(',', handshake.Methods.Span.ToArray())}.");

            if(handshake.Version != 5)
                throw new Exception("Bad SOCKS version.");

            // TODO: Fix this, but for now, choose no auth.
            buffer.Span[0] = 0x05; // VERSION.
            buffer.Span[1] = 0x00; // NO AUTH.
            await ClientSocket.SendAsync(buffer.Slice(0, 2), cancellationToken);
        }

        private async Task HandleRequestAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // Read the request.
            var read = await ClientSocket.ReceiveAsync(buffer, cancellationToken);

            var request = Request.From(buffer.Span);

            Console.WriteLine($"[{Id}]   Request:");
            Console.WriteLine($"[{Id}]     Version: {request.Version}.");
            Console.WriteLine($"[{Id}]     Command: {Helpers.Commands[request.Command]}.");
            //Console.WriteLine($"[{Id}]     RSV: {request.Reserved}.");
            Console.WriteLine($"[{Id}]     Address Type: {Helpers.AddressTypes[request.AddressType]}.");
            Console.WriteLine($"[{Id}]     Destination: {request.Destination}.");
            Console.WriteLine($"[{Id}]     Port: {request.Port}.");

            switch(request.Command)
            {
                case 0x01 /* CONNECT */:
                    await HandleConnectRequestAsync(request, buffer, cancellationToken);
                    break;
                case 0x02 /* BIND */:
                    throw new NotImplementedException();
                    //break;
                case 0x03 /* UDP ASSOCIATE */:
                    throw new NotImplementedException();
                    //break;
                default:
                    throw new Exception("Unknown request type.");
            }
        }

        private async Task HandleConnectRequestAsync(Request request, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // Connect to remote endpoint.
            EndpointSocket = ClientSocket.CreateNewOfSameType();
            EndpointSocket.Bind(new IPEndPoint(EndpointInterface, 0 /* any */));

            var error = null as SocketError?;
            
            try
            {
                switch(request)
                {
                    case IpRequest ipRequest:
                        EndpointSocket = await EndpointSocket.ConnectAsync(ipRequest.Address, ipRequest.Port);
                        break;
                    case DomainNameRequest domainRequest:
                        EndpointSocket = await EndpointSocket.ConnectAsync(domainRequest.DomainName, domainRequest.Port);
                        break;
                    default:
                        throw new Exception("Unknown request type.");
                }
            }
            catch(SocketException e)
            {
                error = e.SocketErrorCode;
            }

            var localEndpoint = EndpointSocket?.LocalEndPoint as IPEndPoint ?? new IPEndPoint(0, 0);
            var remoteEndpoint = EndpointSocket?.RemoteEndPoint as IPEndPoint ?? new IPEndPoint(0, 0);
            
            // The bind address and port should be the server local values during a CONNECT.

            var localAddress = localEndpoint.Address;
            if(localAddress.IsIPv4MappedToIPv6)
                localAddress = localAddress.MapToIPv4();

            var (portHigh, portLow) = Helpers.GetBytesFromPort(localEndpoint.Port);

            // Select the reply code based on any errors.

            var replyField = Helpers.GetSocksReply(error);
            
            // Send reply to client.

            var replyLength = 0;

            buffer.Span[0] = 0x05; // VERSION
            buffer.Span[1] = replyField; // REPLY.
            buffer.Span[2] = 0x00; // RESERVED.

            if(localAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                buffer.Span[3] = 0x01; // ADDRESS TYPE (IPv4).
                localAddress.TryWriteBytes(buffer.Span.Slice(4, 4), out int written);
                buffer.Span[8] = portHigh;
                buffer.Span[9] = portLow;

                replyLength = 10;
            }
            else if(localAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                buffer.Span[3] = 0x04; // ADDRESS TYPE (IPv6).
                localAddress.TryWriteBytes(buffer.Span.Slice(4, 16), out int written);
                buffer.Span[20] = portHigh;
                buffer.Span[21] = portLow;

                replyLength = 22;
            }
            else
                throw new Exception("Unknown server-local bind address type.");

            // Send a response to client regardless of failure.

            await ClientSocket.SendAsync(buffer.Slice(0, replyLength), cancellationToken);

            var sock = new Socket(SocketType.Dgram, ProtocolType.Udp);
            var buf = new byte[4096];

            // In a failure scenario, ensure the SOCKS process does not continue.

            if(error != null)
                throw new SocksException($"The connection failed gracefully with `{error}`.");
        }
    }
}