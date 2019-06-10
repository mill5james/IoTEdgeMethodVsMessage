using System;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Transport.Mqtt;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IotEdge
{
    class Producer
    {
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
            using (var cts = new CancellationTokenSource())
            {
                await moduleClient.OpenAsync();
                await moduleClient.SetMethodHandlerAsync(nameof(GetTimeMethod), GetTimeMethod, moduleClient);
                await moduleClient.SetInputMessageHandlerAsync(nameof(GetTimeMessage), GetTimeMessage, moduleClient);

                // Wait until the app unloads or is cancelled
                AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
                Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
                await WhenCancelled(cts.Token);
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
            Console.WriteLine("{0:O} - GetTimeMethod", DateTime.Now);
            try
            {
                var response = new MethodResponse(GetPayload(), 200);
                return Task.FromResult(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return Task.FromResult(new MethodResponse(500));
            }
        }

        private static async Task<MessageResponse> GetTimeMessage(Message message, object userContext)
        {
            Console.WriteLine("{0:O} - GetTimeMessage", DateTime.Now);
            try
            {
                var moduleClient = (ModuleClient)userContext;
                var response = new Message(GetPayload()) { CorrelationId = message.CorrelationId };
                await moduleClient.SendEventAsync(nameof(GetTimeMessage), response);
                return MessageResponse.Completed;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return MessageResponse.Abandoned;
            }
        }
    }
}
