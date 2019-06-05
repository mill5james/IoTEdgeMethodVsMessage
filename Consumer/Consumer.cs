using System;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json.Linq;

namespace IoTEdge
{
    public class Consumer
    {
        private static string DeviceId { get; } = Environment.GetEnvironmentVariable("IOTEDGE_DEVICEID");
        public static async Task Main(string[] args)
        {
            using (var moduleClient = await ModuleClient.CreateFromEnvironmentAsync())
            using (var resetEvent = new ManualResetEventSlim(false))
            {
                await moduleClient.OpenAsync();

                var cts = new CancellationTokenSource();

                await moduleClient.SetInputMessageHandlerAsync(nameof(GetTimeMessage), GetTimeMessage, resetEvent, cts.Token);
                await Task.Factory.StartNew(() => RequestTimeMessage(moduleClient, resetEvent, cts.Token), TaskCreationOptions.LongRunning);
                await Task.Factory.StartNew(() => GetTimeMethod(moduleClient, cts.Token), TaskCreationOptions.LongRunning);

                AssemblyLoadContext.Default.Unloading += (_) => cts.Cancel();
                Console.CancelKeyPress += (_, __) => cts.Cancel();
                await WhenCancelled(cts.Token);
            }
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
                var begin = DateTime.UtcNow;
                var response = await moduleClient.InvokeMethodAsync(DeviceId, new MethodRequest("GetTimeMethod"), cancellationToken);
                var end = DateTime.UtcNow;
                var payload = JObject.Parse(response.ResultAsJson);
                var produced = payload.Value<DateTime>("UtcTime");
                LogTiming(begin, produced, end);
            }
        }

        private static async Task RequestTimeMessage(ModuleClient moduleClient, ManualResetEventSlim resetEvent, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                resetEvent.Reset();
                var message = new Message { CorrelationId = DateTime.UtcNow.ToString("O") };
                await moduleClient.SendEventAsync("GetTimeMessage", message);
                resetEvent.Wait(cancellationToken);
            }
        }

        private static Task<MessageResponse> GetTimeMessage(Message message, object userContext)
        {
            var end = DateTime.UtcNow;
            var begin = DateTime.Parse(message.CorrelationId);
            var payload = JObject.Parse(Encoding.Unicode.GetString(message.GetBytes()));
            var produced = payload.Value<DateTime>("UtcTime");
            LogTiming(begin, produced, end);
            ((ManualResetEventSlim)userContext).Set();

            return Task.FromResult(MessageResponse.Completed);
        }

        private static void LogTiming(DateTime begin, DateTime produced, DateTime end, [CallerMemberName] string memberName = "")
        {
            var requestLatency = produced - begin;
            var responseLatency = end - produced;
            var totalLatency = end - begin;
            Console.WriteLine("{0} - Request Latency: {1}, Reponse Latency: {2}, Total Latency: {3}", memberName, requestLatency, responseLatency, totalLatency);
        }
    }
}
