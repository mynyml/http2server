/**
 * Cerver
 * ßerver
 */

using System;                 // Console, BitConverter, InvalidOperationException, Buffer, Activator, Convert
using System.IO;              // BinaryReader
using System.Net;             // IPAddress
using System.Net.Sockets;     // TcpListener, Socket, NetworkStream
using System.Text;            // Encoding, StringBuilder
using System.Threading.Tasks; // Task

/**
 * Chapter references, frame format ascii graphics, etc are borrowed from the
 * respective RFCs.
 */
namespace HTTP2
{
  using StreamId     = System.UInt32;
  using StreamWeight = System.Byte;

  /**
   * 11.2. HTTP/2 Frame Type registry
   */
  enum Type : byte
  {
    Data         = 0x0,
    Headers      = 0x1,
    Priority     = 0x2,
    RstStream    = 0x3,
    Settings     = 0x4,
    PushPromise  = 0x5,
    Ping         = 0x6,
    Goaway       = 0x7,
    WindowUpdate = 0x8,
    Continuation = 0x9,
  };

  static class Preface
  {
    private const string PATTERN = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n";

    public static void Read(BinaryReader reader)
    {
        byte[] bytes   = reader.ReadBytes(24);
        string preface = Encoding.UTF8.GetString(bytes);

        if (preface != PATTERN)
        {
          throw new InvalidOperationException();
        }
    }

    public new static string ToString()
    {
      return PATTERN;
    }
  }

  /**
   * 4.1. Frame Format
   *
   * Frame Layout
   *
   *    +-----------------------------------------------+
   *    |                 Length (24)                   |
   *    +---------------+---------------+---------------+
   *    |   Type (8)    |   Flags (8)   |
   *    +-+-------------+---------------+-------------------------------+
   *    |R|                 Stream Identifier (31)                      |
   *    +=+=============================================================+
   *    |                   Frame Payload (0...)                      ...
   *    +---------------------------------------------------------------+
   */
  class Header
  {
    private const byte NO_FLAG = 0x0;

    public uint Length { get; } = 0;
    public Type Type { get; }
    public byte Flags { get; } = NO_FLAG;
    public StreamId StreamId { get; } = 0;

    public static Header Read(BinaryReader reader)
    {
      byte[] field1 = reader.ReadBytes(3);
      byte   field2 = reader.ReadByte();
      byte   field3 = reader.ReadByte();
      byte[] field4 = reader.ReadBytes(4);

      field1 = new byte[] {
        0x0,
        field1[0],
        field1[1],
        field1[2],
      };

      // TODO mask out reserved bit

      if (BitConverter.IsLittleEndian)
      {
        Array.Reverse(field1);
        Array.Reverse(field4);
      }

      var length   = BitConverter.ToUInt32(field1, 0);
      var type     = (Type)field2;
      var flags    = field3;
      var streamId = BitConverter.ToUInt32(field4, 0);

      return new Header(length, type, flags, streamId);
    }

    public Header(uint length, Type type, byte flags, StreamId id)
    {
      Length   = length;
      Type     = type;
      Flags    = flags;
      StreamId = id;
    }
  }

  class Payload
  {
    private int index = 0;
    // TODO switch back to private
    public readonly byte[] bytes;

    public int Length => bytes.Length - index;

    public Payload(byte[] bytes)
    {
      this.bytes = bytes;
    }

    public byte this[int i] => bytes[i];

    public UInt16 ReadUInt16()
    {
      return BitConverter.ToUInt16(Read(2), 0);
    }

    public UInt32 Read24AsUInt32()
    {
      var bytes = Read(3);
      bytes = new byte[]
      {
        0x0,
        bytes[0],
        bytes[1],
        bytes[2],
      };
      return BitConverter.ToUInt32(bytes, 0);
    }

    public UInt32 ReadUInt32()
    {
      return BitConverter.ToUInt32(Read(4), 0);
    }

    public byte ReadByte()
    {
      return bytes[index++];
    }

    public byte[] ReadBytes(int n)
    {
      return Read(n, false);
    }

    private byte[] Read(int n, bool networkToHostOrder = true)
    {
      var buffer = new byte[n];
      Buffer.BlockCopy(bytes, index, buffer, 0, n);
      index += n;

      if (networkToHostOrder && BitConverter.IsLittleEndian)
        Array.Reverse(buffer);

      return buffer;
    }
  }

  abstract class Frame
  {
    // TODO move stream consts to Stream class
    protected const StreamId     DEFAULT_STREAM_ID     = 0x0;
    protected const StreamWeight DEFAULT_STREAM_WEIGHT = 16;

    protected Header Header { get; }

    public Frame(Header header)
    {
      Header = header;
    }

    public static Frame Read(BinaryReader reader)
    {
      var header  = Header.Read(reader);
      var payload = new Payload(reader.ReadBytes((int)header.Length));

      System.Type type;

      switch (header.Type)
      {
        case HTTP2.Type.Settings:     type = typeof(Settings);     break;
        case HTTP2.Type.WindowUpdate: type = typeof(WindowUpdate); break;
        case HTTP2.Type.Headers:      type = typeof(Headers);      break;
        default:
          throw new InvalidOperationException($"Unknown frame type {header.Type}");
      }

      return (Frame)Activator.CreateInstance(type, new Object[] { header, payload });
    }

    public override string ToString()
    {
      var hd = new StringBuilder();

      // TODO move to Header.ToString()
      hd.AppendLine("Header: ");
      hd.AppendLine(String.Format("  Type:   {0}", Header.Type));
      hd.AppendLine(String.Format("  Id:     {0}", Header.StreamId));
      hd.AppendLine(String.Format("  Length: {0}", Header.Length));
      hd.AppendLine(String.Format("  Flags:  {0}", Header.Flags));

      return hd.ToString();
    }
  }

  /**
   * 6.5. SETTINGS
   *
   * +-------------------------------+
   * |       Identifier (16)         |
   * +-------------------------------+-------------------------------+
   * |                        Value (32)                             |
   * +---------------------------------------------------------------+
   */
  class Settings : Frame
  {
    /**
     * 6.5.2. Defined SETTINGS Parameters
     */
    public enum Identifier : byte
    {
      HeaderTableSize      = 0x1,
      EnablePush           = 0x2,
      MaxConcurrentStreams = 0x3,
      InitialWindowSize    = 0x4,
      MaxFrameSize         = 0x5,
      MaxHeaderListSize    = 0x6,
    }

    const int PARAMETER_BYTE_LENGTH = 6;

    public UInt32 HeaderTableSize      { get; }
    public UInt32 EnablePush           { get; }
    public UInt32 MaxConcurrentStreams { get; }
    public UInt32 InitialWindowSize    { get; }
    public UInt32 MaxFrameSize         { get; }
    public UInt32 MaxHeaderListSize    { get; }

    public Settings(Header header, Payload payload) : base(header)
    {
      // TODO ensure payload length is multiple of PARAMETER_BYTE_LENGTH
      var numParams = payload.Length / PARAMETER_BYTE_LENGTH;

      for (int i = 0; i < payload.Length; i += PARAMETER_BYTE_LENGTH)
      {
        var identifier = (Identifier)payload.ReadUInt16();
        var value = payload.ReadUInt32();

        switch (identifier)
        {
          case Identifier.HeaderTableSize:      HeaderTableSize      = value; break;
          case Identifier.EnablePush:           EnablePush           = value; break;
          case Identifier.MaxConcurrentStreams: MaxConcurrentStreams = value; break;
          case Identifier.InitialWindowSize:    InitialWindowSize    = value; break;
          case Identifier.MaxFrameSize:         MaxFrameSize         = value; break;
          case Identifier.MaxHeaderListSize:    MaxHeaderListSize    = value; break;
        }
      }
    }

    public override string ToString()
    {
      var sb = new StringBuilder();
      var hd = base.ToString();

      sb.AppendLine("Settings");
      sb.AppendLine("--------");
      sb.Append(hd);
      sb.AppendLine("Payload:");
      sb.AppendLine(String.Format("  HeaderTableSize:      {0}", HeaderTableSize));
      sb.AppendLine(String.Format("  EnablePush:           {0}", EnablePush));
      sb.AppendLine(String.Format("  MaxConcurrentStreams: {0}", MaxConcurrentStreams));
      sb.AppendLine(String.Format("  InitialWindowSize:    {0}", InitialWindowSize));
      sb.AppendLine(String.Format("  MaxFrameSize:         {0}", MaxFrameSize));
      sb.AppendLine(String.Format("  MaxHeaderListSize:    {0}", MaxHeaderListSize));
      sb.AppendLine();

      return sb.ToString();
    }
  }

  /**
   * 6.9. WINDOW_UPDATE
   *
   * +-+-------------------------------------------------------------+
   * |R|              Window Size Increment (31)                     |
   * +-+-------------------------------------------------------------+
   */
  class WindowUpdate : Frame
  {
    public uint WindowSizeIncrement { get; }

    public WindowUpdate(Header header, Payload payload) : base(header)
    {
      WindowSizeIncrement = payload.ReadUInt32();
    }

    public override string ToString()
    {
      var sb = new StringBuilder();
      var hd = base.ToString();

      sb.AppendLine("WindowUpdate");
      sb.AppendLine("------------");
      sb.Append(hd);
      sb.AppendLine("Payload:");
      sb.AppendLine(String.Format("  WindowSizeIncrement: {0}", WindowSizeIncrement));
      sb.AppendLine();

      return sb.ToString();
    }
  }

  /**
   * 6.2. HEADERS
   *
   * +---------------+
   * |Pad Length? (8)|
   * +-+-------------+-----------------------------------------------+
   * |E|                 Stream Dependency? (31)                     |
   * +-+-------------+-----------------------------------------------+
   * |  Weight? (8)  |
   * +-+-------------+-----------------------------------------------+
   * |                   Header Block Fragment (*)                 ...
   * +---------------------------------------------------------------+
   * |                           Padding (*)                       ...
   * +---------------------------------------------------------------+
   */
  class Headers : Frame
  {
    [FlagsAttribute]
    enum Flags : byte
    {
      None       = 0x0,
      EndStream  = 0x1,
      EndHeaders = 0x4,
      Padded     = 0x8,
      Priority   = 0x20,
    }

    public bool StreamDependencyExclusive { get; } = false;
    public StreamId StreamDependency      { get; } = DEFAULT_STREAM_ID;
    public byte StreamWeight              { get; } = DEFAULT_STREAM_WEIGHT;
    public byte[] Fragment                { get; }
    public bool IsStreamEnd               { get; }
    public bool IsHeadersEnd              { get; }

    public Headers(Header frameHeader, Payload payload) : base(frameHeader)
    {
      byte padLength     = 0;
      bool isStreamEnd   = Convert.ToBoolean(frameHeader.Flags & (byte)Flags.EndStream);
      bool isHeadersEnd  = Convert.ToBoolean(frameHeader.Flags & (byte)Flags.EndHeaders);
      bool isPadded      = Convert.ToBoolean(frameHeader.Flags & (byte)Flags.Padded);
      bool isPrioritized = Convert.ToBoolean(frameHeader.Flags & (byte)Flags.Priority);

      if (isPadded)
      {
        padLength = payload.ReadByte();
      }

      if (isPrioritized)
      {
        StreamDependency          = (StreamId)payload.ReadUInt32();
        StreamWeight              = payload.ReadByte();
        StreamDependencyExclusive = Convert.ToBoolean(StreamDependency & (0x1 << 31));
      }

      Fragment     = payload.ReadBytes(payload.Length - padLength);
      IsStreamEnd  = isStreamEnd;
      IsHeadersEnd = isHeadersEnd;
    }

    public override string ToString()
    {
      var sb = new StringBuilder();
      var hd = base.ToString();

      sb.AppendLine("Headers");
      sb.AppendLine("-------");
      sb.Append(hd);
      sb.AppendLine(String.Format("  Flags:  {0:G}", (Flags)Header.Flags ));
      sb.AppendLine("Payload:");
      sb.AppendLine(String.Format("  StreamDependency:          {0}", StreamDependency));
      sb.AppendLine(String.Format("  StreamWeight:              {0}", StreamWeight));
      sb.AppendLine(String.Format("  StreamDependencyExclusive: {0}", StreamDependencyExclusive));
      sb.AppendLine(String.Format("  Fragment:                  {0}", BitConverter.ToString(Fragment)));
      sb.AppendLine();

      return sb.ToString();
    }
  }


  class Server
  {
    private TcpListener tcpListener { get; }

    public Server(IPAddress address, int port)
    {
        this.tcpListener = new TcpListener(address, port);
        this.tcpListener.Start();
    }

    public async Task Listen()
    {
      var socket = await this.tcpListener.AcceptSocketAsync();
      var stream = new NetworkStream(socket);
      var reader = new BinaryReader(stream, Encoding.UTF8);

      try
      {
        Preface.Read(reader);
        Console.WriteLine(Preface.ToString());

        Frame frame;

        frame = Frame.Read(reader);
        Console.WriteLine(frame.ToString());

        frame = Frame.Read(reader);
        Console.WriteLine(frame.ToString());

        frame = Frame.Read(reader);
        Console.WriteLine(frame.ToString());
      }
      catch (Exception e)
      {
        Console.WriteLine(e.ToString());
      }
      finally
      {
        socket?.Dispose();
        stream?.Dispose();
        reader?.Dispose();
      }
    }
  }

  class Program
  {
    public static void Main()
    {
      var port   = 3000;
      var server = new Server(IPAddress.Any, port);

      var task = Task.Run(() => server.Listen());
      Console.WriteLine("http2 server listening on port {0}", port);

      task.Wait();
    }
  }
}
