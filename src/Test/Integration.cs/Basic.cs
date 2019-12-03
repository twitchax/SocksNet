using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MihaZupan;
using Xunit;

namespace SocksNetTests.Integration
{
    public class Basic
    {
        [Fact]
        public async Task CanUseProxy()
        {
            var cancellationToken = new CancellationTokenSource();
            var server = Helpers.RunServers(cancellationToken.Token);

            var proxy = new HttpToSocks5Proxy("127.0.0.1", 5000);
            var handler = new HttpClientHandler { Proxy = proxy };
            HttpClient httpClient = new HttpClient(handler, true);
            
            var message = "lol";
            var expected = $"[{message}]";

            var content = new StringContent(message, Encoding.UTF8, "application/text");

            var response = await httpClient.PostAsync("http://localhost:5001/", content);
            response.EnsureSuccessStatusCode();

            Assert.Equal(expected, await response.Content.ReadAsStringAsync());

            cancellationToken.Cancel();
        }

        [Fact]
        public async Task CanTestThroughput()
        {
            var cancellationToken = new CancellationTokenSource();
            var server = Helpers.RunServers(cancellationToken.Token);

            var proxy = new HttpToSocks5Proxy("127.0.0.1", 5000);
            var handler = new HttpClientHandler { Proxy = proxy };
            HttpClient httpClient = new HttpClient(handler, true);
            
            var message = new String('b', 10000);
            var expected = $"[{message}]";

            var content = new StringContent(message, Encoding.UTF8, "application/text");

            var start = DateTime.Now;

            for(int k = 0; k < 50; k++)
            {
                var response = await httpClient.PostAsync("http://localhost:5001/", content);
                response.EnsureSuccessStatusCode();
            }

            var end = DateTime.Now;

            Console.WriteLine($"This test took {(end - start).TotalMilliseconds} ms.");

            //Assert.Equal(expected, await response.Content.ReadAsStringAsync());

            cancellationToken.Cancel();
        }
    }
}
