using System;
using System.Collections.Concurrent;
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
            int count = 60;
            while (!System.Diagnostics.Debugger.IsAttached && (count-- > 0))
            {
                if (0 == count % 10) Console.WriteLine($"Waiting for debugger. {count} seconds left before continuing");
                await Task.Delay(1000);
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

                await Task.WhenAll(
                    Task.Run(async () => await GetTimeMethod(moduleClient, cts.Token)),
                    Task.Run(async () => await RequestTimeMessage(moduleClient, resetEvent, cts.Token)),
                    Task.Run(() => PrintStatistics(cts.Token)),
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
                    Console.WriteLine($"GetTimeMethod caught exception {ex.GetType().Name} - {ex.Message}");
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
                    Console.WriteLine($"RequestTimeMessage caught exception {ex.GetType().Name} - {ex.Message}");
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
                Console.WriteLine($"GetTimeMessage caught exception {ex.GetType().Name} - {ex.Message}");
                if (ex.InnerException != null) {
                    Console.WriteLine($"Inner exception {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                }
                return Task.FromResult(MessageResponse.Abandoned);
            }
        }

        private static ConcurrentQueue<(string Method, DateTime Begin, DateTime Produced, DateTime End)> measurements = new ConcurrentQueue<(string Method, DateTime Begin, DateTime Produced, DateTime End)>();

        private static void LogTiming(DateTime begin, DateTime produced, DateTime end, [CallerMemberName] string memberName = "")
        {
            measurements.Enqueue((memberName, begin, produced, end));
        }

        private static void PrintStatistics(CancellationToken cancellationToken)
        {
            Func<(long min, long max, long avg), long, (long,long,long)> computeStats = (tuple, val) => { 
                tuple.min = Math.Min(tuple.min, val);
                tuple.max = Math.Max(tuple.max, val);
                tuple.avg += val;
                return tuple;
            };

            while (!cancellationToken.IsCancellationRequested)
            {
                var stats = (
                    Method: (Request:   (min: long.MaxValue, max: long.MinValue, avg: 0L),
                            Response:   (min: long.MaxValue, max: long.MinValue, avg: 0L),
                            Total:      (min: long.MaxValue, max: long.MinValue, avg: 0L)),
                    Message:(Request:   (min: long.MaxValue, max: long.MinValue, avg: 0L),
                            Response:   (min: long.MaxValue, max: long.MinValue, avg: 0L),
                            Total:      (min: long.MaxValue, max: long.MinValue, avg: 0L)));

                //await Task.Delay((int)TimeSpan.FromMinutes(1).TotalMilliseconds, cancellationToken);
                Thread.Sleep((int)TimeSpan.FromMinutes(1).TotalMilliseconds);
                var count = measurements.Count;
                var now = DateTime.Now;
                int i = 0, messageCount = 0, methodCount = 0;
                while ((i++ < count) && measurements.TryDequeue(out var measure))
                {
                    if (measure.Method == nameof(GetTimeMessage)) {
                        messageCount++;
                        stats.Message.Request = computeStats(stats.Message.Request, (measure.Produced - measure.Begin).Ticks);
                        stats.Message.Response = computeStats(stats.Message.Response, (measure.End - measure.Produced).Ticks);
                        stats.Message.Total = computeStats(stats.Message.Total, (measure.End - measure.Begin).Ticks);
                    }
                    else {
                        methodCount++;
                        stats.Method.Request = computeStats(stats.Method.Request, (measure.Produced - measure.Begin).Ticks);
                        stats.Method.Response = computeStats(stats.Method.Response, (measure.End - measure.Produced).Ticks);
                        stats.Method.Total = computeStats(stats.Method.Total, (measure.End - measure.Begin).Ticks);
                    }
                }
                Console.WriteLine($"{now:O} - Processed {count} items in 1 minute");
                Console.WriteLine("                   | Min           | Max           | Avg           |");
                Console.WriteLine("--------+----------+---------------+---------------+---------------|");
                Console.WriteLine("        | Request  | {0:mm\\:ss\\.fffffff} | {1:mm\\:ss\\.fffffff} | {2:%mm\\:%ss\\.fffffff} |", new TimeSpan(stats.Message.Request.min), new TimeSpan(stats.Message.Request.max), new TimeSpan(stats.Message.Request.avg / count));
                Console.WriteLine("Message | Response | {0:mm\\:ss\\.fffffff} | {1:mm\\:ss\\.fffffff} | {2:%mm\\:%ss\\.fffffff} |", new TimeSpan(stats.Message.Response.min), new TimeSpan(stats.Message.Response.max), new TimeSpan(stats.Message.Response.avg / count));
                Console.WriteLine("{3,7:G} | Total    | {0:mm\\:ss\\.fffffff} | {1:mm\\:ss\\.fffffff} | {2:%mm\\:%ss\\.fffffff} |", new TimeSpan(stats.Message.Total.min), new TimeSpan(stats.Message.Total.max), new TimeSpan(stats.Message.Total.avg / count), messageCount);
                Console.WriteLine("--------+----------+---------------+---------------+---------------|");
                Console.WriteLine("        | Request  | {0:mm\\:ss\\.fffffff} | {1:mm\\:ss\\.fffffff} | {2:%mm\\:%ss\\.fffffff} |", new TimeSpan(stats.Method.Request.min), new TimeSpan(stats.Method.Request.max), new TimeSpan(stats.Method.Request.avg / count));
                Console.WriteLine("Method  | Response | {0:mm\\:ss\\.fffffff} | {1:mm\\:ss\\.fffffff} | {2:%mm\\:%ss\\.fffffff} |", new TimeSpan(stats.Method.Response.min), new TimeSpan(stats.Method.Response.max), new TimeSpan(stats.Method.Response.avg / count));
                Console.WriteLine("{3,7:G} | Total    | {0:mm\\:ss\\.fffffff} | {1:mm\\:ss\\.fffffff} | {2:%mm\\:%ss\\.fffffff} |", new TimeSpan(stats.Method.Total.min), new TimeSpan(stats.Method.Total.max), new TimeSpan(stats.Method.Total.avg / count), methodCount);
                Console.WriteLine("--------+----------+---------------+---------------+---------------|");
                
            }

        }
    }
}
