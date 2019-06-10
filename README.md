# IoTEdgeMethodVsMessage

Demonstrates a problem with the Azure IoT Edge runtime

## Producer

Implements direct method handler and an input message handler that just return the current time in UTC.

## Consumer

Calls the direct method on the Producer module and then writes to the console the latency for the direct method call.

Sends a message to the producer module and waits for a message response on an input mesage handler, then writes to the console the latency for the message exchange.
