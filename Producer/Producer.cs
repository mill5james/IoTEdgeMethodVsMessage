using System;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Transport.Mqtt;
using Newtonsoft.Json;

namespace IotEdge
{
    class Producer
    {
        private static int messageCount = 0;
        private static int methodCount = 0;
        public static async Task Main(string[] args)
        {
#if DEBUG
            int count = 60;
            do
            {
                if (0 == count % 10) Console.WriteLine($"Waiting for debugger. {count} seconds left before continuing");
                await Task.Delay(1000);
            }
            while (!System.Diagnostics.Debugger.IsAttached && (count-- > 0));
#endif

            Console.WriteLine("{0:O} - Starting", DateTime.Now);
            ITransportSettings[] settings = { new MqttTransportSettings(TransportType.Mqtt_Tcp_Only) };
            using (var moduleClient = await ModuleClient.CreateFromEnvironmentAsync(settings))
            using (var cts = new CancellationTokenSource())
            {
                await moduleClient.OpenAsync();
                await moduleClient.SetMethodHandlerAsync(nameof(GetTimeMethod), GetTimeMethod, moduleClient);
                await moduleClient.SetInputMessageHandlerAsync(nameof(GetTimeMessage), GetTimeMessage, moduleClient);

                // Wait until the app unloads or is cancelled
                AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
                Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
                await Task.WhenAll(
                    WhenCancelled(cts.Token),
                    Task.Run(async () => await PrintStatistics(cts.Token)));
            }
            Console.WriteLine("{0:O} - Exiting", DateTime.Now);
        }

        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        private static byte[] GetPayload() => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { UtcTime = DateTime.UtcNow }));

        private static Task<MethodResponse> GetTimeMethod(MethodRequest methodRequest, object userContext)
        {
            try
            {
                Interlocked.Increment(ref methodCount);
                var response = new MethodResponse(GetPayload(), 200);
                return Task.FromResult(response);
            }
            catch (Exception ex)
            {
                LogException(ex);
                return Task.FromResult(new MethodResponse(500));
            }
        }

        private static async Task<MessageResponse> GetTimeMessage(Message message, object userContext)
        {
            try
            {
                Interlocked.Increment(ref messageCount);
                var moduleClient = (ModuleClient)userContext;
                var response = new Message(GetPayload()) { CorrelationId = message.CorrelationId };
                await moduleClient.SendEventAsync(nameof(GetTimeMessage), response);
                return MessageResponse.Completed;
            }
            catch (Exception ex)
            {
                LogException(ex);
                return MessageResponse.Abandoned;
            }
        }

        private static void LogException(Exception ex, [CallerMemberName] string memberName = "")
        {
            Console.WriteLine($"{DateTime.Now} - {memberName}: Caught exception {ex.GetType().Name} - {ex.Message}");
            var inner = ex.InnerException;
            while (inner != null)
            {
                Console.WriteLine($"Inner exception {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                inner = inner.InnerException;
            }
            Console.WriteLine(ex.StackTrace);
        }

        private static async Task PrintStatistics(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay((int)TimeSpan.FromMinutes(1).TotalMilliseconds, cancellationToken);
                var mths = Interlocked.Exchange(ref methodCount, 0);
                var msgs = Interlocked.Exchange(ref messageCount, 0);
                Console.WriteLine("{0:O} - {1} Method calls, {2} Message calls ", DateTime.Now, mths, msgs);
            }
        }
    }
}
