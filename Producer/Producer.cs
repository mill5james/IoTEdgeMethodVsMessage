using System;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;

namespace IotEdge
{
    class Producer
    {
        public static async Task Main(string[] args)
        {
            var moduleClient = await ModuleClient.CreateFromEnvironmentAsync();
            await moduleClient.OpenAsync();

            await moduleClient.SetMethodHandlerAsync(nameof(GetTimeMethod), GetTimeMethod, moduleClient);
            await moduleClient.SetInputMessageHandlerAsync(nameof(GetTimeMessage), GetTimeMessage, moduleClient);

            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (_) => cts.Cancel();
            Console.CancelKeyPress += (_, __) => cts.Cancel();
            await WhenCancelled(cts.Token);
        }

        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        private static byte[] GetPayload() => Encoding.Unicode.GetBytes(JsonConvert.SerializeObject(new { UtcTime = DateTime.UtcNow }));

        private static Task<MethodResponse> GetTimeMethod(MethodRequest methodRequest, object userContext)
        {
            var response = new MethodResponse(GetPayload(), 200);
            return Task.FromResult(response);
        }

        private static async Task<MessageResponse> GetTimeMessage(Message message, object userContext)
        {
            var moduleClient = (ModuleClient)userContext;
            var response = new Message(GetPayload()) { CorrelationId = message.CorrelationId };
            await moduleClient.SendEventAsync(nameof(GetTimeMessage), response);
            return MessageResponse.Completed;
        }
    }
}
