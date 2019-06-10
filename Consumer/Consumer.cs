using System;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Transport.Mqtt;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IoTEdge
{
    public class Consumer
    {
        private static string DeviceId { get; } = Environment.GetEnvironmentVariable("IOTEDGE_DEVICEID");
        public static async Task Main(string[] args)
        {
#if DEBUG
            int count = 0;
            while (!System.Diagnostics.Debugger.IsAttached && (count++ < 60))
            {
                Thread.Sleep(1000);
                Console.WriteLine("Waiting for debugger");
            }
#endif
            Console.WriteLine("{0:O} - Starting", DateTime.Now);
            ITransportSettings[] settings = { new MqttTransportSettings(TransportType.Mqtt_Tcp_Only) };
            using (var moduleClient = await ModuleClient.CreateFromEnvironmentAsync(settings))
            using (var resetEvent = new ManualResetEventSlim(false))
            using (var cts = new CancellationTokenSource())
            {
                await moduleClient.OpenAsync();
                await moduleClient.SetInputMessageHandlerAsync(nameof(GetTimeMessage), GetTimeMessage, userContext: resetEvent, cts.Token);

                // Wait until the app unloads or is cancelled
                AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
                Console.CancelKeyPress += (sender, cpe) => cts.Cancel();

                Console.WriteLine("{0:O} - Waiting", DateTime.Now);
                await Task.WhenAll(
                    Task.Run(async () => await GetTimeMethod(moduleClient, cts.Token)),
                    Task.Run(() => RequestTimeMessage(moduleClient, resetEvent, cts.Token)),
                    WhenCancelled(cts.Token));
            }
            Console.WriteLine("{0:O} - Ending", DateTime.Now);
        }

        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        private static async Task GetTimeMethod(ModuleClient moduleClient, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var begin = DateTime.UtcNow;
                    var response = await moduleClient.InvokeMethodAsync(DeviceId, "producer", new MethodRequest("GetTimeMethod"), cancellationToken);
                    var end = DateTime.UtcNow;
                    var payload = JObject.Parse(response.ResultAsJson);
                    var produced = payload.Value<DateTime>("UtcTime");
                    LogTiming(begin, produced, end);
                }
                catch (System.Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        private static async Task RequestTimeMessage(ModuleClient moduleClient, ManualResetEventSlim resetEvent, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    resetEvent.Reset();
                    var message = new Message { CorrelationId = DateTime.UtcNow.ToString("O") };
                    await moduleClient.SendEventAsync("GetTimeMessage", message);
                    resetEvent.Wait(cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        private static Task<MessageResponse> GetTimeMessage(Message message, object userContext)
        {
            try
            {
                var end = DateTime.UtcNow;
                var begin = DateTime.Parse(message.CorrelationId);
                var payload = JObject.Parse(Encoding.UTF8.GetString(message.GetBytes()));
                var produced = payload.Value<DateTime>("UtcTime");
                LogTiming(begin, produced, end);
                ((ManualResetEventSlim)userContext).Set();

                return Task.FromResult(MessageResponse.Completed);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return Task.FromResult(MessageResponse.Abandoned);
            }
        }

        private static void LogTiming(DateTime begin, DateTime produced, DateTime end, [CallerMemberName] string memberName = "")
        {
            var requestLatency = produced - begin;
            var responseLatency = end - produced;
            var totalLatency = end - begin;
            Console.WriteLine("{0:O} {1} - Request Latency: {2:c}, Reponse Latency: {3:c}, Total Latency: {4:c}", DateTime.Now, memberName, requestLatency, responseLatency, totalLatency);
        }
    }
}
