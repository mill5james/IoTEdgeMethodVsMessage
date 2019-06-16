using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Transport.Mqtt;
using Newtonsoft.Json.Linq;

namespace IoTEdge
{
    public class Consumer
    {
        private static string DeviceId { get; } = Environment.GetEnvironmentVariable("IOTEDGE_DEVICEID");
        private static bool EnableMethod = true;
        private static bool EnableMessage = true;
        private static readonly string[] TrueStrings = { "y", "yes", "true", "on", "1" };

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

            EnableMessage = TrueStrings.Contains(Environment.GetEnvironmentVariable("EnableMessage") ?? bool.TrueString, StringComparer.OrdinalIgnoreCase);
            EnableMethod = TrueStrings.Contains(Environment.GetEnvironmentVariable("EnableMethod") ?? bool.TrueString, StringComparer.OrdinalIgnoreCase);

            Console.WriteLine("{0:O} - EnableMessage:{1} EnableMethod:{2}", DateTime.Now, EnableMessage, EnableMethod);
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
                    EnableMethod ? Task.Run(async () => await GetTimeMethod(moduleClient, cts.Token)) : Task.CompletedTask,
                    EnableMessage ? Task.Run(async () => await RequestTimeMessage(moduleClient, resetEvent, cts.Token)) : Task.CompletedTask,
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
                    LogException(ex);
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
                    LogException(ex);
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
                LogException(ex);
                return Task.FromResult(MessageResponse.Abandoned);
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

        private static ConcurrentQueue<(string Method, DateTime Begin, DateTime Produced, DateTime End)> measurements = new ConcurrentQueue<(string Method, DateTime Begin, DateTime Produced, DateTime End)>();

        private static void LogTiming(DateTime begin, DateTime produced, DateTime end, [CallerMemberName] string memberName = "") => measurements.Enqueue((memberName, begin, produced, end));

        private static void PrintStatistics(CancellationToken cancellationToken)
        {
            Func<(long min, long max, long avg), long, (long, long, long)> computeStats = (tuple, val) =>
            {
                tuple.min = Math.Min(tuple.min, val);
                tuple.max = Math.Max(tuple.max, val);
                tuple.avg += val;
                return tuple;
            };

            while (!cancellationToken.IsCancellationRequested)
            {
                var stats = (
                    Method: (Request: (min: long.MaxValue, max: long.MinValue, avg: 0L),
                            Response: (min: long.MaxValue, max: long.MinValue, avg: 0L),
                            Total: (min: long.MaxValue, max: long.MinValue, avg: 0L)),
                    Message: (Request: (min: long.MaxValue, max: long.MinValue, avg: 0L),
                            Response: (min: long.MaxValue, max: long.MinValue, avg: 0L),
                            Total: (min: long.MaxValue, max: long.MinValue, avg: 0L)));

                Thread.Sleep((int)TimeSpan.FromMinutes(1).TotalMilliseconds);
                var count = measurements.Count;
                var now = DateTime.Now;
                int i = 0, messageCount = 0, methodCount = 0;
                while ((i++ < count) && measurements.TryDequeue(out var measure))
                {
                    if (measure.Method == nameof(GetTimeMessage))
                    {
                        messageCount++;
                        stats.Message.Request = computeStats(stats.Message.Request, (measure.Produced - measure.Begin).Ticks);
                        stats.Message.Response = computeStats(stats.Message.Response, (measure.End - measure.Produced).Ticks);
                        stats.Message.Total = computeStats(stats.Message.Total, (measure.End - measure.Begin).Ticks);
                    }
                    else
                    {
                        methodCount++;
                        stats.Method.Request = computeStats(stats.Method.Request, (measure.Produced - measure.Begin).Ticks);
                        stats.Method.Response = computeStats(stats.Method.Response, (measure.End - measure.Produced).Ticks);
                        stats.Method.Total = computeStats(stats.Method.Total, (measure.End - measure.Begin).Ticks);
                    }
                }
                Console.WriteLine($"{now:O} - {count} items in 1 minute");
                Console.WriteLine("                | Min           | Max           | Avg           |");
                Console.WriteLine("--------+-------+---------------+---------------+---------------|");
                if (EnableMessage)
                {
                    Console.WriteLine("        | C->P  | {0:mm\\:ss\\.fffffff} | {1:mm\\:ss\\.fffffff} | {2:%mm\\:ss\\.fffffff} |", new TimeSpan(stats.Message.Request.min), new TimeSpan(stats.Message.Request.max), new TimeSpan(stats.Message.Request.avg / count));
                    Console.WriteLine("Message | P->C  | {0:mm\\:ss\\.fffffff} | {1:mm\\:ss\\.fffffff} | {2:%mm\\:ss\\.fffffff} |", new TimeSpan(stats.Message.Response.min), new TimeSpan(stats.Message.Response.max), new TimeSpan(stats.Message.Response.avg / count));
                    Console.WriteLine("{3,7:G} | Total | {0:mm\\:ss\\.fffffff} | {1:mm\\:ss\\.fffffff} | {2:%mm\\:ss\\.fffffff} |", new TimeSpan(stats.Message.Total.min), new TimeSpan(stats.Message.Total.max), new TimeSpan(stats.Message.Total.avg / count), messageCount);
                    Console.WriteLine("--------+-------+---------------+---------------+---------------|");
                }
                if (EnableMethod)
                {
                    Console.WriteLine("        | C->P  | {0:mm\\:ss\\.fffffff} | {1:mm\\:ss\\.fffffff} | {2:%mm\\:ss\\.fffffff} |", new TimeSpan(stats.Method.Request.min), new TimeSpan(stats.Method.Request.max), new TimeSpan(stats.Method.Request.avg / count));
                    Console.WriteLine("Method  | P->C  | {0:mm\\:ss\\.fffffff} | {1:mm\\:ss\\.fffffff} | {2:%mm\\:ss\\.fffffff} |", new TimeSpan(stats.Method.Response.min), new TimeSpan(stats.Method.Response.max), new TimeSpan(stats.Method.Response.avg / count));
                    Console.WriteLine("{3,7:G} | Total | {0:mm\\:ss\\.fffffff} | {1:mm\\:ss\\.fffffff} | {2:%mm\\:ss\\.fffffff} |", new TimeSpan(stats.Method.Total.min), new TimeSpan(stats.Method.Total.max), new TimeSpan(stats.Method.Total.avg / count), methodCount);
                    Console.WriteLine("--------+-------+---------------+---------------+---------------|");
                }

            }
        }
    }
}
