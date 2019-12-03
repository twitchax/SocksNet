using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SocksNet
{
    public class Settings
    {
        private Dictionary<string, string> _values;

        // [ARoney] NOTE: I know this is terrible, but I am lazy...

        public string ListenInterface => _values.GetValueOrDefault(nameof(ListenInterface)) ?? "*";
        public string EndpointInterface => _values.GetValueOrDefault(nameof(EndpointInterface)) ?? "*";
        public int Port => int.Parse(_values.GetValueOrDefault(nameof(Port)) ?? "1080");
        public int MaxConnections => int.Parse(_values.GetValueOrDefault(nameof(MaxConnections)) ?? "100");
        public int BufferSize => int.Parse(_values.GetValueOrDefault(nameof(BufferSize)) ?? $"{Environment.SystemPageSize}");
        
        private Settings(Dictionary<string, string> values)
        {
            _values = values;
        }

        public static Settings Default => new Settings(new Dictionary<string, string>());

        public static async Task<Settings> FromAsync(string filePath)
        {
            var values = (await File.ReadAllLinesAsync(filePath))
                .Select(l =>
                {
                    var splits = l.Split("=");
                    return (splits[0].Trim(), splits[1].Trim());
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
                
            return new Settings(values);
        }
    }
}