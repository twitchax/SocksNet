using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SocksNet
{
    public static class Helpers
    {
        // Super lazy about logging right now.  I will fix this at some point...
        public static void LogLine(string s)
        {
            Console.WriteLine(s);
        }

        public static int GetPortFromBytes(Span<byte> port)
        {
            if(port.Length != 2)
                throw new Exception("Ports should be exactly two bytes in this context.");

            return ((int)port[0] << 8) + port[1];
        }

        public static (byte high, byte low) GetBytesFromPort(int port)
        {
            if(port < 0 || port > ushort.MaxValue)
                throw new Exception($"Ports should range from 0 to {ushort.MaxValue}, inclusive.");

            return ((byte)(port >> 8), (byte)(port & 0xff));
        }

        public static byte GetSocksReply(SocketError? error)
        {
            byte reply = 0x00;

            switch (error)
            {
                case null:
                    reply = 0x00; // succeeded
                    break;
                case SocketError.NetworkDown:
                case SocketError.NetworkUnreachable:
                    reply = 0x03; // Network unreachable
                    break;
                case SocketError.HostDown:
                case SocketError.HostNotFound:
                case SocketError.HostUnreachable:
                    reply = 0x04; // Host unreachable
                    break;
                case SocketError.ConnectionRefused:
                    reply = 0x05; // Connection refused
                    break;
                case SocketError.TimedOut:
                    reply = 0x06; // TTL expired... [ARoney] Is this right?
                    break;
                default:
                    reply = 0x01; // general SOCKS server failure
                    break;
            }

            return reply;
        }

        public static IReadOnlyCollection<NetworkInterface> Interfaces { get; } = NetworkInterface.GetAllNetworkInterfaces().ToList();

        public static IReadOnlyDictionary<string, (IPAddress? Ipv4, IPAddress? Ipv6)> AddressByInterface { get; } = 
            Interfaces
                .ToDictionary(
                    iface => iface.Name, 
                    iface => 
                    {
                        var addresses = iface.GetIPProperties().UnicastAddresses;
                        var v4 = addresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address;
                        var v6 = addresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)?.Address;
                        return (v4, v6);
                    }
                );

        public static IReadOnlyDictionary<string, IPAddress?> Ipv4AddressByInterface { get; } = 
            AddressByInterface.Where(kvp => kvp.Value.Ipv4 != null).ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Ipv4);

        public static IReadOnlyDictionary<string, IPAddress?> Ipv6AddressByInterface { get; } = 
            AddressByInterface.Where(kvp => kvp.Value.Ipv6 != null).ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Ipv6);
    }
}