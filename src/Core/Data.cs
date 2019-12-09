using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace SocksNet
{
    public class Handshake
    {
        public byte Version { get; }
        public byte NumMethods { get; }
        public Memory<byte> Methods { get; }

        public Handshake(byte version, byte numMethods, Span<byte> methods)
        {
            Version = version;
            NumMethods = numMethods;
            Methods = new Memory<byte>(new byte[numMethods]);
            methods.CopyTo(Methods.Span);
        }

        public static Handshake From(Span<byte> data)
        {
            var version = data[0];
            var numMethods = data[1];
            var methods = data.Slice(2, numMethods);

            return new Handshake(version, numMethods, methods);
        }
    }

    public abstract class Request
    {
        public byte Version { get; }
        public byte Command { get; }
        public byte Reserved { get; }
        public byte AddressType { get; }
        public int Port { get; }

        public abstract string Destination { get; }
        
        public Request(byte version, byte command, byte reserved, byte addressType, int port)
        {
            Version = version;
            Command = command;
            Reserved = reserved;
            AddressType = addressType;
            Port = port;
        }

        public static Request From(Span<byte> data)
        {
            var version = data[0];
            var command = data[1];
            var rsv = data[2];
            var addressType = data[3];

            if(addressType == 0x01 /* IPv4 */)
            {
                var address = data.Slice(4, 4);
                var port = Helpers.GetPortFromBytes(data.Slice(8, 2));

                return new Ipv4Request(version, command, rsv, addressType, port, address);
            }

            if(addressType == 0x03 /* Domain Name */)
            {
                var nameLength = data[4];
                var name = data.Slice(5, nameLength);
                var port = Helpers.GetPortFromBytes(data.Slice(5 + nameLength, 2));

                return new DomainNameRequest(version, command, rsv, addressType, port, name);
            }

            if(addressType == 0x04 /* IPv6 */)
            {
                var address = data.Slice(4, 16);
                var port = Helpers.GetPortFromBytes(data.Slice(20, 2));

                return new Ipv6Request(version, command, rsv, addressType, port, address);
            }

            throw new Exception("Unknown request address type.");
        }
    }

    public abstract class IpRequest : Request
    {
        public IPAddress Address { get; }

        public override string Destination => Address.ToString();

        public IpRequest(byte version, byte command, byte reserved, byte addressType, int port, Span<byte> address) : 
            base(version, command, reserved, addressType, port)
        {
            Address = new IPAddress(address);
        }
    }

    public class Ipv4Request : IpRequest
    {
        public Ipv4Request(byte version, byte command, byte reserved, byte addressType, int port, Span<byte> address) : 
            base(version, command, reserved, addressType, port, address)
        {
        }
    }

    public class Ipv6Request : IpRequest
    {
        public Ipv6Request(byte version, byte command, byte reserved, byte addressType, int port, Span<byte> address) : 
            base(version, command, reserved, addressType, port, address)
        {
        }
    }

    public class DomainNameRequest : Request
    {
        public string DomainName { get; }

        public override string Destination => DomainName;

        public DomainNameRequest(byte version, byte command, byte reserved, byte addressType, int port, Span<byte> domainName) : 
            base(version, command, reserved, addressType, port)
        {
            DomainName = Encoding.UTF8.GetString(domainName);
        }
    }
}