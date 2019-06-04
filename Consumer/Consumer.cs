using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace IoTEdge
{
    public class Consumer : IHostedService
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

        private readonly string deviceId = Environment.GetEnvironmentVariable("IOTEDGE_DEVICEID");
        private readonly CancellationTokenSource ctSource = new CancellationTokenSource();
        private readonly ILogger<Consumer> logger;
        private ModuleClient moduleClient;
        private ManualResetEventSlim resetEvent = new ManualResetEventSlim(false);

        public Consumer(ILogger<Consumer> logger)
        {
            this.logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            moduleClient = await ModuleClient.CreateFromEnvironmentAsync();
            await moduleClient.OpenAsync(cancellationToken);
            await moduleClient.SetInputMessageHandlerAsync(nameof(GetTimeMessage), GetTimeMessage, null, ctSource.Token);

            await Task.Factory.StartNew(() => RequestTimeMethod(ctSource.Token), TaskCreationOptions.LongRunning);
            await Task.Factory.StartNew(() => RequestTimeMessage(ctSource.Token), TaskCreationOptions.LongRunning);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Stopping");
            ctSource.Cancel();
            moduleClient?.Dispose();
            resetEvent?.Dispose();

            return Task.CompletedTask;
        }

        private async Task RequestTimeMethod(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var requested = DateTime.UtcNow;
                var response = await moduleClient.InvokeMethodAsync(deviceId, new MethodRequest("GetTimeMethod"), cancellationToken);
                var received = DateTime.UtcNow;
                var payload = JObject.Parse(response.ResultAsJson);
                var created = payload.Value<DateTime>("UtcTime");
                LogTiming(requested, created, received);
            }
        }

        private async Task RequestTimeMessage(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                resetEvent.Reset();
                await moduleClient.SendEventAsync("GetTimeMessage", new Message { CorrelationId = DateTime.UtcNow.ToString("O") });
                resetEvent.Wait(cancellationToken);
            }
        }

        private Task<MessageResponse> GetTimeMessage(Message message, object userContext)
        {
            var received = DateTime.UtcNow;
            var requested = DateTime.Parse(message.CorrelationId);
            var payload = JObject.Parse(Encoding.Unicode.GetString(message.GetBytes()));
            var created = payload.Value<DateTime>("UtcTime");
            LogTiming(requested, created, received);
            resetEvent.Set();

            return Task.FromResult(MessageResponse.Completed);
        }

        private void LogTiming(DateTime requested, DateTime created, DateTime received)
        {
            var requestLatency = created - requested;
            var responseLatency = received - created;
            var totalLatency = received - requested;
            logger.LogInformation("Request Latency: {0}, Reponse Latency: {1}, Total Latency: {2}", requestLatency, responseLatency, totalLatency);
        }
    }
}
