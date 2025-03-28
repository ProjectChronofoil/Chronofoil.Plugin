using System;

namespace Chronofoil.Capture.IO;

public class PacketMetadata
{
    public uint Source { get; init; }
    public int DataSize { get; init; }
    public byte[] Header { get; init; }

    public PacketMetadata(uint source, int dataSize, byte[] header)
    {
        Source = source;
        DataSize = dataSize;
        Header = header;
    }

    public PacketMetadata(uint source, int dataSize, Span<byte> header)
    {
        Source = source;
        DataSize = dataSize;
        Header = header.ToArray();
    }
}