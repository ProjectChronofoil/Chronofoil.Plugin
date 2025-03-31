using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Chronofoil.CaptureFile.Binary.Packet;
using Chronofoil.CaptureFile.Generated;
using Dalamud.Hooking;
using Chronofoil.Lobby;
using Chronofoil.Utility;
using Dalamud.Game;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.ObjectPool;
using Reloaded.Hooks.Definitions.X64;
using static Chronofoil.CaptureFile.Binary.SpanExtensions;
using Direction = Chronofoil.CaptureFile.Generated.Direction;

namespace Chronofoil.Capture.IO;

public unsafe class CaptureHookManager : IDisposable
{
	private const string LobbyKeySignature = "C7 46 ?? ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 3B F3";
	// private const string NetworkInitSignature = "E8 ?? ?? ?? ?? 48 8D 4C 24 ?? E8 ?? ?? ?? ?? 48 8D 8C 24 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 8F";
	private const string GenericRxSignature = "E8 ?? ?? ?? ?? 4C 8B 4F 10 8B 47 1C 45";
	private const string GenericTxSignature = "40 57 41 56 48 83 EC 38 48 8B F9 4C 8B F2";
	private const string LobbyTxSignature = "40 53 48 83 EC 20 44 8B 41 28";

	private const string OtherCreateTargetCaller =
		"48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 50 48 8B FA 48 8B F1 0F B7 12";
	private const string CreateTargetSignature = "3B 0D ?? ?? ?? ?? 74 0E";

	public delegate void NetworkInitializedDelegate();
	public event NetworkInitializedDelegate? NetworkInitialized;

	public delegate void NetworkEventDelegate(Protocol proto, Direction direction, ReadOnlySpan<byte> data);
	public event NetworkEventDelegate? NetworkEvent;
	
	private delegate nuint RxPrototype(byte* data, byte* a2, nuint a3, nuint a4, nuint a5);
	private delegate nuint TxPrototype(byte* data, nint a2);
	private delegate void LobbyTxPrototype(nuint data);
	
	private readonly Hook<RxPrototype> _chatRxHook;
	private readonly Hook<RxPrototype> _lobbyRxHook;
	private readonly Hook<RxPrototype> _zoneRxHook;
	private readonly Hook<TxPrototype> _chatTxHook;
	private readonly Hook<LobbyTxPrototype> _lobbyTxHook;
	private readonly Hook<TxPrototype> _zoneTxHook;

	private delegate byte OtherCreateTargetCallerPrototype(void* a1, void* a2, void* a3);
	private readonly Hook<OtherCreateTargetCallerPrototype> _otherCreateTargetCallerHook;

	[Function([FunctionAttribute.Register.rcx, FunctionAttribute.Register.rsi], FunctionAttribute.Register.rax, false)]
	private delegate byte CreateTargetPrototype(int entityId, nint packetPtr);
	private readonly Hook<CreateTargetPrototype> _createTargetHook;

	private readonly IPluginLog _log;
	private readonly LobbyEncryptionProvider _encryptionProvider;
	private readonly INotificationManager _notificationManager;
	private readonly IChatGui _chatGui;
	private readonly SimpleBuffer _buffer;

	private bool _ignoreCreateTarget;

	/// <summary>
	/// Queue for frames observed prior to receiving all data from a previous Zone Rx frame with IPC packets.
	/// </summary>
	private readonly Queue<QueuedFrame> _frameQueue;
	
	/// <summary>
	/// Queue for Zone Rx IPC packets that have yet to have their data filled in via CreateTarget.
	/// </summary>
	private readonly Queue<QueuedPacket> _zoneRxIpcQueue;
	
	/// <summary>
	/// A pool of QueuedFrames to reduce GC pressure.
	/// </summary>
	private readonly ObjectPool<QueuedFrame> _framePool;
	
	/// <summary>
	/// A pool of QueuedPackets to reduce GC pressure.
	/// </summary>
	private readonly ObjectPool<QueuedPacket> _packetPool;

	public CaptureHookManager(
		IPluginLog log,
		LobbyEncryptionProvider encryptionProvider,
		INotificationManager notificationManager,
		MultiSigScanner multiScanner,
		ISigScanner sigScanner,
		IChatGui chatGui,
		IGameInteropProvider hooks)
	{
		_log = log;
		_encryptionProvider = encryptionProvider;
		_notificationManager = notificationManager;
		_chatGui = chatGui;
		
		_buffer = new SimpleBuffer(1024 * 1024);
		_frameQueue = new Queue<QueuedFrame>();
		_zoneRxIpcQueue = new Queue<QueuedPacket>();
		
		_framePool = new DefaultObjectPool<QueuedFrame>(new DefaultPooledObjectPolicy<QueuedFrame>(), 200);
		_packetPool = new DefaultObjectPool<QueuedPacket>(new DefaultPooledObjectPolicy<QueuedPacket>(), 1000);
		
		var lobbyKeyPtr = sigScanner.ScanText(LobbyKeySignature);
		var lobbyKey = (ushort) *(int*)(lobbyKeyPtr + 3); // skip instructions and register offset
		_encryptionProvider.SetGameVersion(lobbyKey);
		_log.Debug($"[CaptureHooks] Lobby key is {lobbyKey}.");
		
		var rxPtrs = multiScanner.ScanText(GenericRxSignature, 3);

		_chatRxHook = hooks.HookFromAddress<RxPrototype>(rxPtrs[0], ChatRxDetour);
		_lobbyRxHook = hooks.HookFromAddress<RxPrototype>(rxPtrs[1], LobbyRxDetour);
		_zoneRxHook = hooks.HookFromAddress<RxPrototype>(rxPtrs[2], ZoneRxDetour);
		
		var txPtrs = multiScanner.ScanText(GenericTxSignature, 2);
		_chatTxHook = hooks.HookFromAddress<TxPrototype>(txPtrs[0], ChatTxDetour);
		_zoneTxHook = hooks.HookFromAddress<TxPrototype>(txPtrs[1], ZoneTxDetour);
		
		var lobbyTxPtr = multiScanner.ScanText(LobbyTxSignature, 1);
		_lobbyTxHook = hooks.HookFromAddress<LobbyTxPrototype>(lobbyTxPtr[0], LobbyTxDetour);
		
		var createTargetPtr = sigScanner.ScanText(CreateTargetSignature);
		_createTargetHook = hooks.HookFromAddress<CreateTargetPrototype>(createTargetPtr, CreateTargetDetour);
		
		var otherCreateTargetCallerPtr = sigScanner.ScanText(OtherCreateTargetCaller);
		_otherCreateTargetCallerHook = hooks.
			HookFromAddress<OtherCreateTargetCallerPrototype>(otherCreateTargetCallerPtr, OtherCreateTargetCallerDetour);

		Enable();
	}

	public void Enable()
	{
		_chatRxHook?.Enable();
		_zoneRxHook?.Enable();
		_lobbyRxHook?.Enable();
		_chatTxHook?.Enable();
		_zoneTxHook?.Enable();
		_lobbyTxHook?.Enable();
		
		_createTargetHook?.Enable();
		_otherCreateTargetCallerHook?.Enable();
	}
	
	public void Disable()
	{
		_chatRxHook?.Disable();
		_zoneRxHook?.Disable();
		_lobbyRxHook?.Disable();
		_chatTxHook?.Disable();
		_zoneTxHook?.Disable();
		_lobbyTxHook?.Disable();
		
		_createTargetHook?.Disable();
		_otherCreateTargetCallerHook?.Disable();
		_zoneRxIpcQueue.Clear();
	}
	
	public void Dispose()
	{
		Disable();
		_chatRxHook?.Dispose();
		_lobbyRxHook?.Dispose();
		_zoneRxHook?.Dispose();
		_chatTxHook?.Dispose();
		_lobbyTxHook?.Dispose();
		_zoneTxHook?.Dispose();
		
		_createTargetHook?.Dispose();
		_otherCreateTargetCallerHook?.Dispose();
	}

	// I know this is very silly, but I don't have access to the return address like Deucalion so uhh
	// let me know if you know of a way I can get the return address in CreateTargetDetour and I'll fix it!
	private byte OtherCreateTargetCallerDetour(void* a1, void* a2, void* a3)
	{
		_ignoreCreateTarget = true;
		_log.Verbose($"[OtherCreateTargetCaller]: ignoring next CreateTarget");
		return _otherCreateTargetCallerHook.Original(a1, a2, a3);
	}
	
	private byte CreateTargetDetour(int entityId, nint packetPtr)
	{
		if (_ignoreCreateTarget)
		{
			_ignoreCreateTarget = false;
			return _createTargetHook.Original(entityId, packetPtr);
		}
		
		if (_zoneRxIpcQueue.Count == 0)
		{
			SendNotification($"[CreateTarget]: Please report this problem: no packets in queue");
			return _createTargetHook.Original(entityId, packetPtr);
		}
		
		var queuedPacket = _zoneRxIpcQueue.Dequeue();
		_log.Verbose($"[CreateTarget]: entity {entityId} meta source {queuedPacket.Source} size {queuedPacket.DataSize}");

		if (queuedPacket.Source != entityId)
		{
			SendNotification($"[CreateTarget]: Please report this problem: srcId {entityId} | queuedSrcId {queuedPacket.Source}");
		}
		
		// Set the packet's data
		var data = new Span<byte>((byte*)packetPtr, queuedPacket.DataSize);
		queuedPacket.Data = data.ToArray();

		// We dequeued the final packet in the zone rx ipc queue, we can commit all data now
		if (_zoneRxIpcQueue.Count == 0)
		{
			while (_frameQueue.TryDequeue(out var frame))
			{
				if (frame.Packets.All(p => p.Data != null))
				{
					WriteFrameAndReturn(frame);
				}
				else
				{
					// Show an error if there are any frames in which a packet does not have data
					// This is significant because the zone rx IPC queue is empty - everything should have data
					SendNotification("Zone RX IPC Queue is empty, but not all packets have data.");
				}
			}
		}
		
		return _createTargetHook.Original(entityId, packetPtr);
	}

	private void SendNotification(string content)
	{
		_chatGui.Print(content, "Chronofoil");
		_notificationManager.AddNotification(new Notification
		{
			Content = content,
			Title = "Chronofoil", 
		});
		_log.Debug($"[SendNotification] {content}");
	}
	
    private nuint ChatRxDetour(byte* data, byte* a2, nuint a3, nuint a4, nuint a5)
    {
	    // _log.Verbose($"ChatRxDetour: {(long)data:X} {(long)a2:X} {a3:X} {a4:X} {a5:X}");
	    var ret = _chatRxHook.Original(data, a2, a3, a4, a5);
	    
	    var packetOffset = *(uint*)(data + 28);
	    if (packetOffset != 0) return ret;
	    
        PacketsFromFrame(Protocol.Chat, Direction.Rx, (byte*) *(nint*)(data + 16));

        return ret;
    }
    
    private nuint LobbyRxDetour(byte* data, byte* a2, nuint a3, nuint a4, nuint a5)
    {
	    // _log.Verbose($"LobbyRxDetour: {(long)data:X} {(long)a2:X} {a3:X} {a4:X} {a5:X}");
	    var packetOffset = *(uint*)(data + 28);
	    if (packetOffset != 0) return _lobbyRxHook.Original(data, a2, a3, a4, a5);
	    
        PacketsFromFrame(Protocol.Lobby, Direction.Rx, (byte*) *(nint*)(data + 16));

        return _lobbyRxHook.Original(data, a2, a3, a4, a5);
    }
    
    private nuint ZoneRxDetour(byte* data, byte* a2, nuint a3, nuint a4, nuint a5)
    {
	    // _log.Verbose($"ZoneRxDetour: {(long)data:X} {(long)a2:X} {a3:X} {a4:X} {a5:X}");
	    var ret = _zoneRxHook.Original(data, a2, a3, a4, a5);

	    var packetOffset = *(uint*)(data + 28);
	    if (packetOffset != 0) return ret;
	    
        PacketsFromFrame(Protocol.Zone, Direction.Rx, (byte*) *(nint*)(data + 16));

        return ret;
    }
    
    private nuint ChatTxDetour(byte* data, nint a2)
    {
	    // _log.Verbose($"ChatTxDetour: {(long)data:X} {(long)a2:X} {a3:X} {a4:X} {a5:X} {a6:X}");
	    var ptr = (nuint*)data;
        ptr += 4;
        PacketsFromFrame(Protocol.Chat, Direction.Tx, (byte*) *ptr);

        return _chatTxHook.Original(data, a2);
    }
    
    private void LobbyTxDetour(nuint data)
    {
	    // _log.Verbose($"LobbyTxDetour: {data:X}");
        _lobbyTxHook.Original(data);
        
        var ptr = data + 32;
        ptr = *(nuint*)ptr;
        PacketsFromFrame(Protocol.Lobby, Direction.Tx, (byte*) ptr);
    }
    
    private nuint ZoneTxDetour(byte* data, nint a2)
    {
	    // _log.Verbose($"ZoneTxDetour: {(long)data:X} {(long)a2:X} {a3:X} {a4:X} {a5:X} {a6:X}");
	    var ptr = (nuint*)data;
        ptr += 4;
        PacketsFromFrame(Protocol.Zone, Direction.Tx, (byte*) *ptr);
        
        return _zoneTxHook.Original(data, a2);
    }

    private void PacketsFromFrame(Protocol proto, Direction direction, byte* framePtr)
    {
        try
        {
            PacketsFromFrame2(proto, direction, framePtr);
        }
        catch (Exception e)
        {
            _log.Error(e, "[PacketsFromFrame] Error!!!!!!!!!!!!!!!!!!");
        }
    }
    
    private void PacketsFromFrame2(Protocol proto, Direction direction, byte* framePtr)
    {
        if ((nuint)framePtr == 0)
        {
            _log.Error("null ptr");
            return;
        }
        
        var headerSize = Unsafe.SizeOf<FrameHeader>();
        var headerSpan = new Span<byte>(framePtr, headerSize);
        var header = headerSpan.Cast<byte, FrameHeader>();
        var span = new Span<byte>(framePtr, (int)header.TotalSize);
        var data = span.Slice(headerSize, (int)header.TotalSize - headerSize);
        
        var queuedFrame = GetFrameFromPool();
        queuedFrame.Protocol = proto;
        queuedFrame.Direction = direction;
        queuedFrame.Header = headerSpan.ToArray();
        
        _log.Verbose($"[{(nuint)framePtr:X}] [{proto}{direction}] proto {header.Protocol} unk {header.Unknown}, {header.Count} pkts size {header.TotalSize} usize {header.DecompressedLength}");
        
        // Compression
        if (header.Compression != CompressionType.None)
        {
	        SendNotification($"[{proto}{direction}] A frame was compressed.");
            return;
        }
        
        var offset = 0;
        for (int i = 0; i < header.Count; i++)
        {
	        var pktHdrSize = Unsafe.SizeOf<PacketElementHeader>();
            var pktHdrSlice = data.Slice(offset, pktHdrSize);
            var pktHdr = pktHdrSlice.Cast<byte, PacketElementHeader>();
            var pktData = data.Slice(offset + pktHdrSize, (int)pktHdr.Size - pktHdrSize);
            
            var queuedPacket = GetPacketFromPool();
            queuedFrame.Packets.Add(queuedPacket);
            queuedPacket.Source = pktHdr.SrcEntity;
            queuedPacket.DataSize = pktData.Length;
            queuedPacket.Header = pktHdrSlice.ToArray();

            // The server sends a keepalive packet to the client right after connection, indicating a new connection
            var isNetworkInit = proto == Protocol.Lobby && direction == Direction.Rx && pktHdr.Type is PacketType.KeepAlive;
            var canInitEncryption = proto == Protocol.Lobby && pktHdr.Type is PacketType.EncryptionInit;
            var needsDecryption = proto == Protocol.Lobby && pktHdr.Type is PacketType.Ipc or PacketType.Unknown_A;
            
            if (isNetworkInit)
	            NetworkInitialized?.Invoke();
            
            if (canInitEncryption)
	            _encryptionProvider.Initialize(pktData);
            
            if (_encryptionProvider.Initialized && needsDecryption)
            {
                var decoded = _encryptionProvider.DecryptPacket(pktData);
                pktData = new Span<byte>(decoded);
            }
            
            if (proto == Protocol.Zone && direction == Direction.Rx && pktHdr.Type == PacketType.Ipc)
            {
	            // Zone rx - we don't have the data yet, so don't fill out the data
	            _zoneRxIpcQueue.Enqueue(queuedPacket);
            }
            else
            {
	            // Any other packet - add the data to our queued packet, in the queued frame 
	            queuedPacket.Data = pktData.ToArray();
            }
            
            _log.Verbose($"packet: type {pktHdr.Type}, {pktHdr.Size} bytes, {pktHdr.SrcEntity} -> {pktHdr.DstEntity}");
            offset += (int)pktHdr.Size;
        }

        // If the zone rx IPC queue is empty, we can write this packet without worrying about ordering
        if (_zoneRxIpcQueue.Count == 0)
        {
	        WriteFrameAndReturn(queuedFrame);
        }
        else
        {
	        _log.Verbose($"[PacketsFromFrame] queueing {queuedFrame.Protocol} {queuedFrame.Direction} frame with {queuedFrame.Packets.Count(p => p.Data != null)}/{queuedFrame.Packets.Count}");
	        _frameQueue.Enqueue(queuedFrame);
        }
    }

    private void WriteFrameAndReturn(QueuedFrame frame)
    {
	    _log.Verbose($"[WriteFrameAndReturn] Writing {frame.Protocol} {frame.Direction} frame with {frame.Packets.Count(p => p.Data != null)}/{frame.Packets.Count}");
	    
	    _buffer.Clear();
	    frame.Write(_buffer);
	    NetworkEvent?.Invoke(frame.Protocol, frame.Direction, _buffer.GetBuffer());
	    _framePool.Return(frame);
    }

    private QueuedFrame GetFrameFromPool()
    {
	    var frame = _framePool.Get();
	    frame.Clear(_packetPool);
	    return frame;
    }

    private QueuedPacket GetPacketFromPool()
    {
	    var packet = _packetPool.Get();
	    packet.Clear();
	    return packet;
    }
}