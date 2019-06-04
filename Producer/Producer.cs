using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace IotEdge
{
    class Producer : IHostedService
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
                    services.AddSingleton<IHostedService, Producer>();
                });
            await host.RunConsoleAsync();
        }

        private readonly CancellationTokenSource ctSource = new CancellationTokenSource();
        private readonly ILogger<Producer> logger;
        private ModuleClient moduleClient;
        private Stopwatch stopwatch = new Stopwatch();

        public Producer(ILogger<Producer> logger)
        {
            this.logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            moduleClient = await ModuleClient.CreateFromEnvironmentAsync();
            await moduleClient.OpenAsync(cancellationToken);

            await moduleClient.SetMethodHandlerAsync(nameof(GetTimeMethod), GetTimeMethod, ctSource.Token);
            await moduleClient.SetInputMessageHandlerAsync(nameof(GetTimeMessage), GetTimeMessage, null, ctSource.Token);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Stopping");
            ctSource.Cancel();
            moduleClient?.Dispose();

            return Task.CompletedTask;
        }

        private byte[] GetPayload() => Encoding.Unicode.GetBytes(JsonConvert.SerializeObject(new { UtcTime = DateTime.UtcNow }));

        private Task<MethodResponse> GetTimeMethod(MethodRequest methodRequest, object userContext)
        {
            var response = new MethodResponse(GetPayload(), 200);
            return Task.FromResult(response);
        }

        private async Task<MessageResponse> GetTimeMessage(Message message, object userContext)
        {
            try
            {
                stopwatch.Start();
                var response = new Message(GetPayload()) { CorrelationId = message.CorrelationId };
                await moduleClient.SendEventAsync(nameof(GetTimeMessage), response, (CancellationToken)userContext);
                return MessageResponse.Completed;
            }
            catch (OperationCanceledException)
            {
                return MessageResponse.Abandoned;
            }
            finally
            {
                stopwatch.Stop();
                logger.LogInformation("Sent message in {0} ms", stopwatch.Elapsed.ToString("g"));
                stopwatch.Reset();
            }
        }
    }
}
