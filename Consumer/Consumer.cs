using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace IoTEdge
{
    class Consumer : IHostedService
    {
        public static async Task Main(string[] args)
        {
            var host = new HostBuilder()
                .ConfigureHostConfiguration(configHost =>
                {
                    configHost.AddJsonFile("appsettings.json", optional: true);
                    configHost.AddEnvironmentVariables();
                })
                .ConfigureLogging((hostContext, configLogging) =>
                {
                    configLogging.AddConfiguration(hostContext.Configuration.GetSection("Logging"));
                    configLogging.AddConsole();
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddSingleton<IHostedService, Consumer>();
                });
            await host.RunConsoleAsync();
        }

        private readonly CancellationTokenSource ctSource = new CancellationTokenSource();
        private readonly ILogger<Consumer> logger;
        private ModuleClient moduleClient;

        public Consumer(ILogger<Consumer> logger)
        {
            this.logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            moduleClient = await ModuleClient.CreateFromEnvironmentAsync(); 
            await moduleClient.OpenAsync(cancellationToken);

            

        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Stopping");
            ctSource.Cancel();
            moduleClient?.Dispose();

            return Task.CompletedTask;
        }
    }
}
