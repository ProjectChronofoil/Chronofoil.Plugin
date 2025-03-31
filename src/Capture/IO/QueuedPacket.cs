using System;

namespace Chronofoil.Capture.IO;

public class QueuedPacket
{
    public uint Source { get; set; }
    public int DataSize { get; set; }
    public byte[]? Header { get; set; }
    public byte[]? Data { get; set; }
    
    public QueuedPacket() {}

    public QueuedPacket(uint source, int dataSize, byte[] header, byte[] data)
    {
        Source = source;
        DataSize = dataSize;
        Header = header;
        Data = data;
    }

    public QueuedPacket(uint source, int dataSize, Span<byte> header, Span<byte> data)
    {
        Source = source;
        DataSize = dataSize;
        Header = header.ToArray();
        Data = data.ToArray();
    }

    public void Clear()
    {
        Source = 0;
        DataSize = 0;
        Header = null;
        Data = null;
    }
}