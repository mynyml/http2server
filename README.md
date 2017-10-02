### Build (Linux/Mono)

      $ mcs server.cs && mono server.exe

### Example Request

      $ curl -svX GET --http2 --output - http://localhost:3000
      * Rebuilt URL to: http://localhost:3000/
      *   Trying 127.0.0.1...
      * TCP_NODELAY set
      * Connected to localhost (127.0.0.1) port 3000 (#0)
      > GET / HTTP/1.1
      > Host: localhost:3000
      > User-Agent: curl/7.55.1
      > Accept: */*
      > Connection: Upgrade, HTTP2-Settings
      > Upgrade: h2c
      > HTTP2-Settings: AAMAAABkAARAAAAAAAIAAAAA
      >
      < HTTP/1.1 101 Switching Protocols
      < Connection: Upgrade
      < Upgrade: h2c
      * Received 101
      * Using HTTP2, server supports multi-use
      * Connection state changed (HTTP/2 confirmed)
      * Copying HTTP/2 data in stream buffer to connection buffer after upgrade: len=0
      * http2 error: Remote peer returned unexpected data while we expected SETTINGS frame.  Perhaps, peer does not support HTTP/2 properly.
      * Closing connection 0

### References

* [RFC7540](http://httpwg.org/specs/rfc7540.html)
* [RFC7541](http://httpwg.org/specs/rfc7541.html)
* [High Performance Browser Networking](https://hpbn.co/)
* [Tools](https://github.com/http2/http2-spec/wiki/Tools)
* [Specs](https://github.com/summerwind/h2spec)

