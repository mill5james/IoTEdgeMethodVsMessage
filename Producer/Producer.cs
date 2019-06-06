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
            using (var moduleClient = await ModuleClient.CreateFromEnvironmentAsync())
            using (var cts = new CancellationTokenSource())
            {
                await moduleClient.OpenAsync();
                await moduleClient.SetMethodHandlerAsync(nameof(GetTimeMethod), GetTimeMethod, moduleClient);
                await moduleClient.SetInputMessageHandlerAsync(nameof(GetTimeMessage), GetTimeMessage, moduleClient);

                AssemblyLoadContext.Default.Unloading += (_) => cts.Cancel();
                Console.CancelKeyPress += (_, __) => cts.Cancel();
                await Task.Factory.StartNew((t) =>
                {
                    var tcs = new TaskCompletionSource<bool>();
                    ((CancellationToken)t).Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
                    return tcs.Task;
                }, cts.Token);
            }
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
