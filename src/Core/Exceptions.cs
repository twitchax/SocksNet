using System;

namespace SocksNet
{
    public class SocksException : Exception
    {
        public SocksException(string message) : base(message) {}
    }
}