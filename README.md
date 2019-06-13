# IoTEdgeMethodVsMessage

Demonstrates a problems with the Azure IoT Edge runtime

## Producer

Implements direct method handler and an input message handler that just return the current time in UTC.

## Consumer

Calls the direct method on the Producer module and records the timestamps for the method call.

Sends a message to the Producer module and waits for a message response on an input mesage handler, then records  timestamps for the message exchange.

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

## Exceptions seen

Intermitently I see an `IotHubCommunicationException` with the message "The SSL connection could not be established, see inner exception." when calling `ModuleClient.InvokeMethodAsync`. The module recovers and subsequent calls succeed.


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
