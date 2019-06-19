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
        private static bool EnableHistogram = true;
        private static readonly string[] TrueStrings = { "y", "yes", "true", "on", "1" };

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

            EnableMessage = TrueStrings.Contains(Environment.GetEnvironmentVariable("EnableMessage") ?? bool.TrueString, StringComparer.OrdinalIgnoreCase);
            Console.WriteLine($"{DateTime.Now:O} - EnableMessage:{EnableMessage}");
            EnableMethod = TrueStrings.Contains(Environment.GetEnvironmentVariable("EnableMethod") ?? bool.TrueString, StringComparer.OrdinalIgnoreCase);
            Console.WriteLine($"{DateTime.Now:O} - EnableMethod:{EnableMethod}");
            EnableHistogram = TrueStrings.Contains(Environment.GetEnvironmentVariable("EnableHistogram") ?? bool.TrueString, StringComparer.OrdinalIgnoreCase);
            Console.WriteLine($"{DateTime.Now:O} - EnableHistogram:{EnableHistogram}");

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

        private static readonly string[] binLables = { "<1000", "1000", "2000", "3000", "4000", "5000", "6000", "7000", "8000", "9000", ">10000" };
        private static async Task PrintStatistics(CancellationToken cancellationToken)
        {
            Func<(double min, double max, double avg), double, (double, double, double)> computeStats = (tuple, val) =>
            {
                tuple.min = Math.Min(tuple.min, val);
                tuple.max = Math.Max(tuple.max, val);
                tuple.avg += val;
                return tuple;
            };

            while (!cancellationToken.IsCancellationRequested)
            {
                var stats = (
                    Method: (Request: (min: double.MaxValue, max: double.MinValue, avg: 0.0d),
                            Response: (min: double.MaxValue, max: double.MinValue, avg: 0.0d),
                            Total: (min: double.MaxValue, max: double.MinValue, avg: 0.0d)),
                    Message: (Request: (min: double.MaxValue, max: double.MinValue, avg: 0.0d),
                            Response: (min: double.MaxValue, max: double.MinValue, avg: 0.0d),
                            Total: (min: double.MaxValue, max: double.MinValue, avg: 0.0d)));

                await Task.Delay((int)TimeSpan.FromMinutes(1).TotalMilliseconds, cancellationToken);
                var count = measurements.Count;
                var now = DateTime.Now;
                Console.WriteLine($"{now:O} - {count} items in 1 minute");
                if (count == 0) continue;
                int i = 0, messageCount = 0, methodCount = 0;
                int[] messageHg = new int[binLables.Length];
                int[] methodHg = new int[binLables.Length];
                while ((i++ < count) && measurements.TryDequeue(out var measure))
                {
                    if (measure.Method == nameof(GetTimeMessage))
                    {
                        messageCount++;
                        stats.Message.Request = computeStats(stats.Message.Request, (measure.Produced - measure.Begin).TotalMilliseconds);
                        stats.Message.Response = computeStats(stats.Message.Response, (measure.End - measure.Produced).TotalMilliseconds);
                        stats.Message.Total = computeStats(stats.Message.Total, (measure.End - measure.Begin).TotalMilliseconds);
                        messageHg[Math.Min((int)Math.Floor((measure.End - measure.Begin).TotalMilliseconds / 1000), messageHg.Length - 1)]++;
                    }
                    else
                    {
                        methodCount++;
                        stats.Method.Request = computeStats(stats.Method.Request, (measure.Produced - measure.Begin).TotalMilliseconds);
                        stats.Method.Response = computeStats(stats.Method.Response, (measure.End - measure.Produced).TotalMilliseconds);
                        stats.Method.Total = computeStats(stats.Method.Total, (measure.End - measure.Begin).TotalMilliseconds);
                        methodHg[Math.Min((int)Math.Floor((measure.End - measure.Begin).TotalMilliseconds / 1000), methodHg.Length - 1)]++;
                    }
                }
                if (EnableMessage)
                {
                    Console.WriteLine("Messages         | Minimum    | Maximum    | Avgerage   |");
                    Console.WriteLine("--------+--------+------------+------------+------------|");
                    Console.WriteLine("        |  C->P  | {0,10:F4} | {1,10:F4} | {2,10:F4} |", stats.Message.Request.min, stats.Message.Request.max, stats.Message.Request.avg / count);
                    Console.WriteLine("{3,7:G} |  P->C  | {0,10:F4} | {1,10:F4} | {2,10:F4} |", stats.Message.Response.min, stats.Message.Response.max, stats.Message.Response.avg / count, messageCount);
                    Console.WriteLine("        |  Total | {0,10:F4} | {1,10:F4} | {2,10:F4} |", stats.Message.Total.min, stats.Message.Total.max, stats.Message.Total.avg / count);
                    Console.WriteLine("--------+--------+------------+------------+------------|");
                    if (EnableHistogram)
                    {
                        Console.WriteLine("Count   | Millis | Percentage ");
                        Console.WriteLine("--------+--------+-------------");
                        for (i = 0; i < messageHg.Length; i++)
                        {
                            Console.WriteLine("{0,7:G} | {1,6} | {2}", messageHg[i], binLables[i], new String('█', (int)Math.Ceiling(((double)messageHg[i]) / ((double)messageCount) * (messageHg.Length + 1))));

                        }
                        Console.WriteLine("--------+--------+-------------");
                    }
                }
                if (EnableMethod)
                {
                    Console.WriteLine("Method           | Minimum    | Maximum    | Avgerage   |");
                    Console.WriteLine("--------+--------+------------+------------+------------|");
                    Console.WriteLine("        |  C->P  | {0,10:F4} | {1,10:F4} | {2,10:F4} |", stats.Method.Request.min, stats.Method.Request.max, stats.Method.Request.avg / count);
                    Console.WriteLine("{3,7:G} |  P->C  | {0,10:F4} | {1,10:F4} | {2,10:F4} |", stats.Method.Response.min, stats.Method.Response.max, stats.Method.Response.avg / count, methodCount);
                    Console.WriteLine("        |  Total | {0,10:F4} | {1,10:F4} | {2,10:F4} |", stats.Method.Total.min, stats.Method.Total.max, stats.Method.Total.avg / count);
                    Console.WriteLine("--------+--------+------------+------------+------------|");
                    if (EnableHistogram)
                    {
                        Console.WriteLine("Count   | Millis | Percentage ");
                        Console.WriteLine("--------+--------+-------------");
                        for (i = 0; i < methodHg.Length; i++)
                        {
                            Console.WriteLine("{0,7:G} | {1,6} | {2}", methodHg[i], binLables[i], new String('█', (int)Math.Ceiling(((double)methodHg[i]) / ((double)methodCount) * (methodHg.Length + 1))));
                        }
                        Console.WriteLine("--------+--------+-------------");
                    }
                }
            }
        }
    }
}
