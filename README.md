# LetsTalk
Playing with sending tcp messages with different decoders

Borrowing from Project Bedrock [https://github.com/davidfowl/BedrockFramework] so I can understand how it works.

This project includes a server and client talking over TCP using a protocol with the header containing the length of the message.
It should be easy to change the message protocol.

The server will allow multiple clients to connect and will respond to each one
