using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace SocksNet
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var settings = Settings.Default;

            if(args.Length == 1)
                settings = await Settings.FromAsync(args[0]);

            // [ARoney] TODO: Allow IPv6.  The rest of the code allows for it, but hardcoding here, for now.

            var listenInterfaceAddress = 
                settings.ListenInterface == "*" ? 
                    IPAddress.Any :
                    Helpers.Ipv4AddressByInterface.GetValueOrDefault(settings.ListenInterface) ?? throw new Exception($"Could not find listen interface `{settings.ListenInterface}`.");

            var endpointInterfaceAddress =
                settings.EndpointInterface == "*" ? 
                    IPAddress.Any :
                    Helpers.Ipv4AddressByInterface.GetValueOrDefault(settings.EndpointInterface) ?? throw new Exception($"Could not find listen interface `{settings.EndpointInterface}`.");
            
            var port = settings.Port;
            var maxConnections = settings.MaxConnections;
            var bufferSize = settings.BufferSize;

            await Server.Instance
                .WithListenInterface(listenInterfaceAddress)
                .WithEndpointInterface(endpointInterfaceAddress)
                .WithPort(port)
                .WithMaxConnections(maxConnections)
                .WithBufferSize(bufferSize)
                .StartAsync();
        }
    }
}
