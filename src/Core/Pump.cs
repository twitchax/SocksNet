using System.Net.Sockets;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Buffers;
using System.Threading;
using System.Runtime.CompilerServices;

namespace SocksNet
{
    public static class Pump
    {
        public static async Task RunSequentialAsync(TransportSocket clientSocket, TransportSocket endpointSocket, int bufferSize, CancellationToken cancellationToken = default)
        {
            var pumpUp = Task.Run(async () =>
            {
                var bufferUp = MemoryPool<byte>.Shared.Rent(bufferSize).Memory;
                while(true)
                {
                    var readFromClient = await clientSocket.ReceiveAsync(bufferUp, cancellationToken);

                    if(readFromClient == 0)
                    {
                        try
                        {
                            if(endpointSocket.Connected)
                                endpointSocket.Disconnect(false);
                        } catch(SocketException) {}
                        return;
                    }

                    var data = bufferUp.Slice(0, readFromClient);
                    var pushedToEndpoint = await endpointSocket.SendAsync(data, cancellationToken);

                    if(cancellationToken.IsCancellationRequested)
                        return;
                }
            });

            var pumpDown = Task.Run(async () =>
            {
                var bufferDown = MemoryPool<byte>.Shared.Rent(bufferSize).Memory;
                while(true)
                {
                    var readFromEndpoint = await endpointSocket.ReceiveAsync(bufferDown, cancellationToken);
                    
                    if(readFromEndpoint == 0)
                    {
                        try
                        {
                            if(clientSocket.Connected)
                                clientSocket.Disconnect(false);
                        } catch(SocketException) {}
                        return;
                    }

                    var data = bufferDown.Slice(0, readFromEndpoint);
                    var pushedToClient = await clientSocket.SendAsync(data, cancellationToken);

                    if(cancellationToken.IsCancellationRequested)
                        return;
                }
            });

            await Task.WhenAll(pumpUp, pumpDown);
        }

        public static Task RunAsyncEnumerable(Socket clientSocket, Socket endpointSocket, int bufferSize, CancellationToken cancellationToken = default)
        {
            var receiveFromClient = ReceiveChunksAsync(clientSocket, bufferSize, cancellationToken);
            var pumpToEndpoint = SendAsync(endpointSocket, receiveFromClient, cancellationToken);

            var receiveFromEndpoint = ReceiveChunksAsync(endpointSocket, bufferSize, cancellationToken);
            var pumpToClient = SendAsync(clientSocket, receiveFromEndpoint, cancellationToken);

            return Task.WhenAll(pumpToEndpoint, pumpToClient);
        }

        private static async IAsyncEnumerable<Memory<byte>> ReceiveChunksAsync(Socket socket, int bufferSize, [EnumeratorCancellation]CancellationToken cancellationToken = default)
        {
            while (true)
            {
                var memory = MemoryPool<byte>.Shared.Rent(bufferSize).Memory;

                int read = await socket.ReceiveAsync(memory, SocketFlags.None, cancellationToken);
                    
                yield return memory.Slice(0, read);

                if (read == 0 || !socket.Connected || cancellationToken.IsCancellationRequested)
                    yield break;
            }
        }

        private static async Task SendAsync(Socket socket, IAsyncEnumerable<Memory<byte>> chunks, CancellationToken cancellationToken = default)
        {
            await foreach(var chunk in chunks)
            {
                await socket.SendAsync(chunk, SocketFlags.None, cancellationToken);
                if(!socket.Connected || cancellationToken.IsCancellationRequested)
                    break;
            }

            try
            {
                if(socket.Connected)
                    socket.Disconnect(false);
            }
            catch(Exception) {}
        }
    }
}