using System.Collections.Generic;
using Chronofoil.CaptureFile.Generated;
using Microsoft.Extensions.ObjectPool;

namespace Chronofoil.Capture.IO;

public class QueuedFrame
{
    public Protocol Protocol { get; set; }
    public Direction Direction { get; set; }
    
    public byte[]? Header { get; set; }
    public List<QueuedPacket> Packets { get; init; }

    public QueuedFrame()
    {
        Packets = [];
    }

    public QueuedFrame(byte[] header)
    {
        Header = header;
        Packets = [];
    }

    public void Clear(ObjectPool<QueuedPacket> pool)
    {
        Header = null;
        foreach (var packet in Packets)
        {
            packet.Clear();
            pool.Return(packet);
        }
        Packets.Clear();
    }

    public void Write(SimpleBuffer buffer)
    {
        buffer.Write(Header);
        foreach (var packet in Packets)
        {
            buffer.Write(packet.Header);
            buffer.Write(packet.Data);
        }
    }
}