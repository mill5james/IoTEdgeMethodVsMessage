# IoTEdgeMethodVsMessage

Demonstrates sending messages and calling direct methods between modules using the Azure IoT Edge runtime

## Producer

Implements direct method handler and an input message handler that just return the current time in UTC.

The latest image can be found in the Docker Hub at [mill5james/producer](https://hub.docker.com/r/mill5james/producer)

## Consumer

Calls the direct method on the Producer module and records the timestamps for the method call.

Sends a message to the Producer module and waits for a message response on an input mesage handler, then records  timestamps for the message exchange.

The latest image can be found in the Docker Hub at [mill5james/consumer](https://hub.docker.com/r/mill5james/consumer)

### Statistics from the Consumer

The statisitcs are collected by the Consumer module every minute in a background task. They capture:

* The total number messages send and received and method calls
* The latency for sending a message or invoking a method from the consumer to the producer (C->P)
* The latency for sending a message or handling the method invocation from the producer to the consumer (P->C)
* The total latency as seen at the consumer (Total)
* A histogram of the total latency in 1 second bins

#### Example of the consumer module output every minute:

```  txt
2019-06-19T19:32:16.4161739+00:00 - 2868 items in 1 minute
Messages         | Minimum    | Maximum    | Average    |
--------+--------+------------+------------+------------|
        |  C->P  |     1.0952 | 10019.3094 |    16.2191 |
    936 |  P->C  |     0.9817 |    51.9430 |     1.3836 |
        |  Total |     2.5858 | 10022.2115 |    17.6027 |
--------+--------+------------+------------+------------|
Count   | Millis | Percentage
--------+--------+-------------
    930 |  <1000 | ████████████
      0 |   1000 |
      0 |   2000 |
      2 |   3000 | █
      1 |   4000 | █
      0 |   5000 |
      0 |   6000 |
      0 |   7000 |
      0 |   8000 |
      0 |   9000 |
      3 | >10000 | █
--------+--------+-------------
Method           | Minimum    | Maximum    | Average    |
--------+--------+------------+------------+------------|
        |  C->P  |    14.1958 |  4504.8295 |    17.5319 |
   1932 |  P->C  |     0.8823 |   106.8569 |     1.5188 |
        |  Total |    15.5089 |  4508.3599 |    19.0508 |
--------+--------+------------+------------+------------|
Count   | Millis | Percentage
--------+--------+-------------
   1930 |  <1000 | ████████████
      0 |   1000 |
      0 |   2000 |
      1 |   3000 | █
      1 |   4000 | █
      0 |   5000 |
      0 |   6000 |
      0 |   7000 |
      0 |   8000 |
      0 |   9000 |
      0 | >10000 |
--------+--------+-------------
```

## Build

By default the Dockerfile build will produce a image for `Release` targeting the `stretch-slim` .NET Code 2.2 image. Invoke the `docker build` command from the root of the repo.

``` powershell
docker build --file "Consumer/Dockerfile" --tag consumer:latest $PWD
docker build --file "Producer/Dockerfile" --tag producer:latest $PWD
```

### Build for Debug

To build for debug, pass the build a build argumnet `CONFIG=Debug` which will build for Debug and include the debugger in the image

``` powershell
docker build --file "Consumer/Dockerfile" --tag consumer:debug --build-arg CONFIG=Debug $PWD
docker build --file "Producer/Dockerfile" --tag producer:debug --build-arg CONFIG=Debug $PWD
```

### Target a different base image

To target another base image, pass the `BASE_TAG` build argumnet.

For example, to target alpine base image:

``` powershell
docker build --file "Consumer/Dockerfile" --tag consumer:alpine --build-arg BASE_TAG=alpine $PWD
```

For example, to target an ARM32 base image:

``` powershell
docker build --file "Consumer/Dockerfile" --tag consumer:arm32v7 --build-arg BASE_TAG=arm32v7 $PWD
```

## IoT Edge Deployment

If you want to deploy just these modules to an IoT Edge for testing, you can simply download the [deployment.json](https://github.com/mill5james/IoTEdgeMethodVsMessage/blob/master/deployment.json) included in the repository.

If you want deploy these modules along other modules in your environment, the only deployment requirements are the message routes between the consumer and producer modules (assuming that your modules are named `producer` and `consumer` respectively.

``` json
   "routes": {
      "ConsumerToProducer": "FROM /messages/modules/consumer/outputs/GetTimeMessage INTO BrokeredEndpoint(\"/modules/producer/inputs/GetTimeMessage\")",
      "ProducerToConsumer": "FROM /messages/modules/producer/outputs/GetTimeMessage INTO BrokeredEndpoint(\"/modules/consumer/inputs/GetTimeMessage\")"
   },
```

### Customize your deployment

You can disable invoking methods or sending messages from consumer module the deployment by passing `false` to the `EnableMethod` or `EnableMessage` environment variables.

``` json
   "consumer": {
      "settings": {
            "image": "mill5james/consumer:latest",
            "createOptions": "{}"
      },
      "type": "docker",
      "env": {
            "EnableMethod": {
               "value": "true"
            },
            "EnableMessage": {
               "value": "true"
            }
      },
      "version": "1.0",
      "status": "running",
      "restartPolicy": "always"
   }
```

## Known Issues

As of the iotedge 1.0.7.1 release, these are the known issues.

### Exceptions seen in the Consumer module

Intermittently I see an `IotHubCommunicationException` with the message "The SSL connection could not be established, see inner exception." when calling `ModuleClient.InvokeMethodAsync(...)`. The module recovers and subsequent calls succeed.


``` txt
GetTimeMethod: Caught exception IotHubCommunicationException - The SSL connection could not be established, see inner exception.
Inner exception HttpRequestException - The SSL connection could not be established, see inner exception.
Inner exception HttpRequestException - The SSL connection could not be established, see inner exception.
   at Microsoft.Azure.Devices.Client.Transport.HttpClientHelper.ExecuteAsync(HttpMethod httpMethod, Uri requestUri, Func`3 modifyRequestMessageAsync, Func`2 isSuccessful, Func`3 processResponseMessageAsync, IDictionary`2 errorMappingOverrides, CancellationToken cancellationToken)
   at Microsoft.Azure.Devices.Client.Transport.HttpClientHelper.PostAsync[T1,T2](Uri requestUri, T1 entity, IDictionary`2 errorMappingOverrides, IDictionary`2 customHeaders, CancellationToken cancellationToken)
   at Microsoft.Azure.Devices.Client.ModuleClient.InvokeMethodAsync(Uri uri, MethodRequest methodRequest, CancellationToken cancellationToken)
   at IoTEdge.Consumer.GetTimeMethod(ModuleClient moduleClient, CancellationToken cancellationToken) in /src/Consumer/Consumer.cs:line 64
```

After running for some time, I see an `IotHubCommunicationException` with the message "Address already in use" when calling `ModuleClient.InvokeMethodAsync(...)`. Once we receive this exception, the module never recovers and every subsequent call returns the same exception.

``` txt
06/13/2019 17:54:17 - GetTimeMethod: Caught exception IotHubCommunicationException - Address already in use
Inner exception HttpRequestException - Address already in use
Inner exception HttpRequestException - Address already in use
   at Microsoft.Azure.Devices.Client.Transport.HttpClientHelper.ExecuteAsync(HttpMethod httpMethod, Uri requestUri, Func`3 modifyRequestMessageAsync, Func`2 isSuccessful, Func`3 processResponseMessageAsync, IDictionary`2 errorMappingOverrides, CancellationToken cancellationToken)
   at Microsoft.Azure.Devices.Client.Transport.HttpClientHelper.PostAsync[T1,T2](Uri requestUri, T1 entity, IDictionary`2 errorMappingOverrides, IDictionary`2 customHeaders, CancellationToken cancellationToken)
   at Microsoft.Azure.Devices.Client.ModuleClient.InvokeMethodAsync(Uri uri, MethodRequest methodRequest, CancellationToken cancellationToken)
   at IoTEdge.Consumer.GetTimeMethod(ModuleClient moduleClient, CancellationToken cancellationToken) in /src/Consumer/Consumer.cs:line 64
```

### Exceptions seen in the Edge Hub

When only sending messages between modules, we will repeatedly see unacknowledged messages in the Edge Hub without any error in the producer or consumer. These will be followed by exceptions from the MQTT stack. I have no clue as to the periodicity of these exceptions.

``` log
2019-06-16 21:06:13.114 +00:00 [WRN] - Error sending messages to module jamesp-iotedge2/producer
System.TimeoutException: Message completion response not received
   at Microsoft.Azure.Devices.Edge.Hub.Core.Device.DeviceMessageHandler.SendMessageAsync(IMessage message, String input) in /home/vsts/work/1/s/edge-hub/src/Microsoft.Azure.Devices.Edge.Hub.Core/device/DeviceMessageHandler.cs:line 363
   at Microsoft.Azure.Devices.Edge.Hub.Core.Routing.ModuleEndpoint.ModuleMessageProcessor.ProcessAsync(ICollection`1 routingMessages, IDeviceProxy dp, CancellationToken token) in /home/vsts/work/1/s/edge-hub/src/Microsoft.Azure.Devices.Edge.Hub.Core/routing/ModuleEndpoint.cs:line 164
2019-06-16 21:06:14.139 +00:00 [WRN] - Closing connection for device: jamesp-iotedge2/consumer, scope: ExceptionCaught, DotNetty.Codecs.DecoderException: [MQTT-2.3.1-1]
   at DotNetty.Codecs.Mqtt.MqttDecoder.DecodePacketIdVariableHeader(IByteBuffer buffer, PacketWithId packet, Int32& remainingLength)
   at DotNetty.Codecs.Mqtt.MqttDecoder.DecodePublishPacket(IByteBuffer buffer, PublishPacket packet, Int32& remainingLength)
   at DotNetty.Codecs.Mqtt.MqttDecoder.DecodePacketInternal(IByteBuffer buffer, Int32 packetSignature, Int32& remainingLength, IChannelHandlerContext context)
   at DotNetty.Codecs.Mqtt.MqttDecoder.TryDecodePacket(IByteBuffer buffer, IChannelHandlerContext context, Packet& packet)
   at DotNetty.Codecs.Mqtt.MqttDecoder.Decode(IChannelHandlerContext context, IByteBuffer input, List`1 output)
   at DotNetty.Codecs.ReplayingDecoder`1.CallDecode(IChannelHandlerContext context, IByteBuffer input, List`1 output)
   at DotNetty.Codecs.ByteToMessageDecoder.ChannelRead(IChannelHandlerContext context, Object message)
   at DotNetty.Transport.Channels.AbstractChannelHandlerContext.InvokeChannelRead(Object msg), 6cfe9aea
2019-06-16 21:06:14.140 +00:00 [INF] - Disposing MessagingServiceClient for device Id jamesp-iotedge2/consumer because of exception - DotNetty.Codecs.DecoderException: [MQTT-2.3.1-1]
   at DotNetty.Codecs.Mqtt.MqttDecoder.DecodePacketIdVariableHeader(IByteBuffer buffer, PacketWithId packet, Int32& remainingLength)
   at DotNetty.Codecs.Mqtt.MqttDecoder.DecodePublishPacket(IByteBuffer buffer, PublishPacket packet, Int32& remainingLength)
   at DotNetty.Codecs.Mqtt.MqttDecoder.DecodePacketInternal(IByteBuffer buffer, Int32 packetSignature, Int32& remainingLength, IChannelHandlerContext context)
   at DotNetty.Codecs.Mqtt.MqttDecoder.TryDecodePacket(IByteBuffer buffer, IChannelHandlerContext context, Packet& packet)
   at DotNetty.Codecs.Mqtt.MqttDecoder.Decode(IChannelHandlerContext context, IByteBuffer input, List`1 output)
   at DotNetty.Codecs.ReplayingDecoder`1.CallDecode(IChannelHandlerContext context, IByteBuffer input, List`1 output)
   at DotNetty.Codecs.ByteToMessageDecoder.ChannelRead(IChannelHandlerContext context, Object message)
   at DotNetty.Transport.Channels.AbstractChannelHandlerContext.InvokeChannelRead(Object msg)
```
