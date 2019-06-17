# IoTEdgeMethodVsMessage

Demonstrates a problems with the Azure IoT Edge runtime

## Producer

Implements direct method handler and an input message handler that just return the current time in UTC.

The latest image can be found in the Docker Hub at [mill5james/producer](https://hub.docker.com/r/mill5james/producer)

## Consumer

Calls the direct method on the Producer module and records the timestamps for the method call.

Sends a message to the Producer module and waits for a message response on an input mesage handler, then records  timestamps for the message exchange.

The latest image can be found in the Docker Hub at [mill5james/consumer](https://hub.docker.com/r/mill5james/consumer)

### Statistics

The statisitcs are collected by the Consumer module every minute in a background task. They capture:

* The total number of message and messages send and received
* The latency for sending a message or invoking a method from the consumer to the producer (C->P)
* The latency for sending a message or handling the method invocation from the producer to the consumer (P->C)
* The total latency as seen at the consumer (Total).

```  txt
2019-06-01T18:00:00.0000000+00:00 - 2336 items in 1 minute
                | Min           | Max           | Avg           |
--------+-------+---------------+---------------+---------------|
        | C->P  | 00:00.0011081 | 00:16.0559861 | 00:00.0102317 |
Message | P->C  | 00:00.0010394 | 01:30.7039632 | 00:00.0506138 |
   1008 | Total | 00:00.0024657 | 01:46.7599493 | 00:00.0608455 |
--------+-------+---------------+---------------+---------------|
        | C->P  | 00:00.0136449 | 00:04.2036823 | 00:00.0155379 |
Method  | P->C  | 00:00.0012644 | 00:07.4075739 | 00:00.0062872 |
   1713 | Total | 00:00.0157815 | 00:07.4268208 | 00:00.0218252 |
--------+-------+---------------+---------------+---------------|
```

## Exceptions seen in the Consumer module

Intermittently I see an `IotHubCommunicationException` with the message "The SSL connection could not be established, see inner exception." when calling `ModuleClient.InvokeMethodAsync`. The module recovers and subsequent calls succeed.


``` txt
GetTimeMethod: Caught exception IotHubCommunicationException - The SSL connection could not be established, see inner exception.
Inner exception HttpRequestException - The SSL connection could not be established, see inner exception.
Inner exception HttpRequestException - The SSL connection could not be established, see inner exception.
   at Microsoft.Azure.Devices.Client.Transport.HttpClientHelper.ExecuteAsync(HttpMethod httpMethod, Uri requestUri, Func`3 modifyRequestMessageAsync, Func`2 isSuccessful, Func`3 processResponseMessageAsync, IDictionary`2 errorMappingOverrides, CancellationToken cancellationToken)
   at Microsoft.Azure.Devices.Client.Transport.HttpClientHelper.PostAsync[T1,T2](Uri requestUri, T1 entity, IDictionary`2 errorMappingOverrides, IDictionary`2 customHeaders, CancellationToken cancellationToken)
   at Microsoft.Azure.Devices.Client.ModuleClient.InvokeMethodAsync(Uri uri, MethodRequest methodRequest, CancellationToken cancellationToken)
   at IoTEdge.Consumer.GetTimeMethod(ModuleClient moduleClient, CancellationToken cancellationToken) in /src/Consumer/Consumer.cs:line 64
```

After running for some time, I see an `IotHubCommunicationException` with the message "Address already in use" when calling `ModuleClient.InvokeMethodAsync`. Once we receive this exception, the module never recovers and every subsequent call returns the same exception.

``` txt
06/13/2019 17:54:17 - GetTimeMethod: Caught exception IotHubCommunicationException - Address already in use
Inner exception HttpRequestException - Address already in use
Inner exception HttpRequestException - Address already in use
   at Microsoft.Azure.Devices.Client.Transport.HttpClientHelper.ExecuteAsync(HttpMethod httpMethod, Uri requestUri, Func`3 modifyRequestMessageAsync, Func`2 isSuccessful, Func`3 processResponseMessageAsync, IDictionary`2 errorMappingOverrides, CancellationToken cancellationToken)
   at Microsoft.Azure.Devices.Client.Transport.HttpClientHelper.PostAsync[T1,T2](Uri requestUri, T1 entity, IDictionary`2 errorMappingOverrides, IDictionary`2 customHeaders, CancellationToken cancellationToken)
   at Microsoft.Azure.Devices.Client.ModuleClient.InvokeMethodAsync(Uri uri, MethodRequest methodRequest, CancellationToken cancellationToken)
   at IoTEdge.Consumer.GetTimeMethod(ModuleClient moduleClient, CancellationToken cancellationToken) in /src/Consumer/Consumer.cs:line 64
```

## Exceptions seen in the Edge Hub

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

## Build

By default the Dockerfile build will produce a image for `Release` targeting the `stretch-slim` .NET Code 2.2 image. Invoke the `docker build` command from the root of the repo.

``` powershell
docker build --file "Consumer/Dockerfile" --tag consumer:latest $PWD
docker build --file "Producer/Dockerfile" --tag producer:latest $PWD
```

To build for debug, pass the build a build argumnet `CONFIG=Debug` which will build for Debug and include the debugger in the image

``` powershell
docker build --file "Consumer/Dockerfile" --tag consumer:latest --build-arg CONFIG=Debug $PWD
docker build --file "Producer/Dockerfile" --tag producer:latest --build-arg CONFIG=Debug $PWD
```

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

The only deployment requirements are the message routes between the consumer and producer modules

``` json
   "routes": {
      "ConsumerToProducer": "FROM /messages/modules/consumer/outputs/GetTimeMessage INTO BrokeredEndpoint(\"/modules/producer/inputs/GetTimeMessage\")",
      "ProducerToConsumer": "FROM /messages/modules/producer/outputs/GetTimeMessage INTO BrokeredEndpoint(\"/modules/consumer/inputs/GetTimeMessage\")"
   },
```

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

