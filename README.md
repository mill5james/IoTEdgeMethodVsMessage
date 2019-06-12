# IoTEdgeMethodVsMessage

Demonstrates a problem with the Azure IoT Edge runtime

## Producer

Implements direct method handler and an input message handler that just return the current time in UTC.

## Consumer

Calls the direct method on the Producer module and records the timestamps for the method call.

Sends a message to the Producer module and waits for a message response on an input mesage handler, then records  timestamps for the message exchange.

### Statistics

The statisitcs are collected every minute in a background task. They capture:

* The total number of message and messages send and received
* The latency for sending a message to the producer or invoking a method from the consumer to the producer (C->P)
* The latency for sending a message response or handling the method invocation from the producer to the consumer (P->C)
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
