using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SocksNet;

namespace SocksNetTests.Integration
{
    public static class Helpers
    {
        public static Task RunServers(CancellationToken token)
        {
            var endpointServer = WebHost.CreateDefaultBuilder()
                .SuppressStatusMessages(true)
                .ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureKestrel(options =>
                {
                    options.ListenLocalhost(5001);
                })
                .Configure(app => app.Run(context =>
                {
                    return context.HttpBoomerang();
                }))
                .Build().RunAsync(token);

            var proxyServer = Server.Instance.WithPort(5000).StartAsync(token);

            return Task.WhenAll(endpointServer, proxyServer);
        }

        public static async Task HttpBoomerang(this HttpContext context)
        {
            var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
            await context.Response.WriteAsync($"[{message}]");
        }
    }
}