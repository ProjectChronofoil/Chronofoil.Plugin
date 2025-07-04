﻿using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Chronofoil.CaptureFile;
using Chronofoil.CaptureFile.Binary.Packet;
using Chronofoil.CaptureFile.Generated;
using Dalamud.Hooking;
using Chronofoil.Lobby;
using Chronofoil.Utility;
using Dalamud.Game;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using Unscrambler;
using Unscrambler.Constants;
using Unscrambler.Derivation;
using Unscrambler.Unscramble;
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

	private readonly IPluginLog _log;
	private readonly LobbyEncryptionProvider _encryptionProvider;
	private readonly INotificationManager _notificationManager;
	private readonly SimpleBuffer _buffer;

	private bool _enabled;
	private bool _obfuscationOverride;
	private readonly VersionConstants _versionConstants;
	private readonly IKeyGenerator _keyGenerator;
	private readonly IUnscrambler _unscrambler;

	public CaptureHookManager(
		IPluginLog log,
		LobbyEncryptionProvider encryptionProvider,
		INotificationManager notificationManager,
		MultiSigScanner multiScanner,
		ISigScanner sigScanner,
		IGameInteropProvider hooks)
	{
		_log = log;
		_encryptionProvider = encryptionProvider;
		_notificationManager = notificationManager;
		_buffer = new SimpleBuffer(1024 * 1024);

		var version = Util.GetRunningGameVersion();
		_versionConstants = VersionConstants.ForGameVersion(version);
		_keyGenerator = KeyGeneratorFactory.ForGameVersion(version);
		_unscrambler = UnscramblerFactory.ForGameVersion(version);
		
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

		Enable();
	}

	public void Enable()
	{
		var dispatcher = PacketDispatcher.GetInstance();
		if (dispatcher != null)
		{
			var gameRandom = dispatcher->GameRandom;
			var packetRandom = dispatcher->LastPacketRandom;
			
			_obfuscationOverride = dispatcher->Key0 >= gameRandom + packetRandom;

			if (_obfuscationOverride)
			{
				_keyGenerator.Keys[0] = (byte)(dispatcher->Key0 - gameRandom - packetRandom);
				_keyGenerator.Keys[1] = (byte)(dispatcher->Key1 - gameRandom - packetRandom);
				_keyGenerator.Keys[2] = (byte)(dispatcher->Key2 - gameRandom - packetRandom);	
			}
			else
			{
				_keyGenerator.Keys[0] = 0;
				_keyGenerator.Keys[1] = 0;
				_keyGenerator.Keys[2] = 0;
			}
			
			_log.Debug($"[Enable] obfuscation override is {_obfuscationOverride}");
			_log.Debug($"[Enable] keys {dispatcher->Key0}, {dispatcher->Key1}, {dispatcher->Key2}");
			_log.Debug($"[Enable] game random {dispatcher->GameRandom}, packet random {dispatcher->LastPacketRandom}");
		}
		else
		{
			_log.Debug("[Enable] Dispatcher was null, so not initializing keys");
		}
		
		_chatRxHook?.Enable();
		_zoneRxHook?.Enable();
		_lobbyRxHook?.Enable();
		_chatTxHook?.Enable();
		_zoneTxHook?.Enable();
		_lobbyTxHook?.Enable();
	}
	
	public void Disable()
	{
		_chatRxHook?.Disable();
		_zoneRxHook?.Disable();
		_lobbyRxHook?.Disable();
		_chatTxHook?.Disable();
		_zoneTxHook?.Disable();
		_lobbyTxHook?.Disable();
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
	}
	
    private nuint ChatRxDetour(byte* data, byte* a2, nuint a3, nuint a4, nuint a5)
    {
	    // _log.Debug($"ChatRxDetour: {(long)data:X} {(long)a2:X} {a3:X} {a4:X} {a5:X}");
	    var ret = _chatRxHook.Original(data, a2, a3, a4, a5);
	    
	    var packetOffset = *(uint*)(data + 28);
	    if (packetOffset != 0) return ret;
	    
        PacketsFromFrame(Protocol.Chat, Direction.Rx, (byte*) *(nint*)(data + 16));

        return ret;
    }
    
    private nuint LobbyRxDetour(byte* data, byte* a2, nuint a3, nuint a4, nuint a5)
    {
	    // _log.Debug($"LobbyRxDetour: {(long)data:X} {(long)a2:X} {a3:X} {a4:X} {a5:X}");

	    var packetOffset = *(uint*)(data + 28);
	    if (packetOffset != 0) return _lobbyRxHook.Original(data, a2, a3, a4, a5);
	    
        PacketsFromFrame(Protocol.Lobby, Direction.Rx, (byte*) *(nint*)(data + 16));

        return _lobbyRxHook.Original(data, a2, a3, a4, a5);
    }
    
    private nuint ZoneRxDetour(byte* data, byte* a2, nuint a3, nuint a4, nuint a5)
    {
	    // _log.Debug($"ZoneRxDetour: {(long)data:X} {(long)a2:X} {a3:X} {a4:X} {a5:X}");
	    var ret = _zoneRxHook.Original(data, a2, a3, a4, a5);

	    var packetOffset = *(uint*)(data + 28);
	    if (packetOffset != 0) return ret;
	    
        PacketsFromFrame(Protocol.Zone, Direction.Rx, (byte*) *(nint*)(data + 16));

        return ret;
    }
    
    private nuint ChatTxDetour(byte* data, nint a2)
    {
	    // _log.Debug($"ChatTxDetour: {(long)data:X} {(long)a2:X} {a3:X} {a4:X} {a5:X} {a6:X}");
	    var ptr = (nuint*)data;
        ptr += 4;
        PacketsFromFrame(Protocol.Chat, Direction.Tx, (byte*) *ptr);

        return _chatTxHook.Original(data, a2);
    }
    
    private void LobbyTxDetour(nuint data)
    {
	    // _log.Debug($"LobbyTxDetour: {data:X}");
        _lobbyTxHook.Original(data);
        
        var ptr = data + 32;
        ptr = *(nuint*)ptr;
        PacketsFromFrame(Protocol.Lobby, Direction.Tx, (byte*) ptr);
    }
    
    private nuint ZoneTxDetour(byte* data, nint a2)
    {
	    // _log.Debug($"ZoneTxDetour: {(long)data:X} {(long)a2:X} {a3:X} {a4:X} {a5:X} {a6:X}");
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
	    // _log.Debug($"PacketsFromFrame: {(long)framePtr:X} {proto} {direction}");
        if ((nuint)framePtr == 0)
        {
            _log.Error("null ptr");
            return;
        }
        _buffer.Clear();
        
        var headerSize = Unsafe.SizeOf<FrameHeader>();
        var headerSpan = new Span<byte>(framePtr, headerSize);
        _buffer.Write(headerSpan);
        
        var header = headerSpan.Cast<byte, FrameHeader>();
        // _log.Debug($"PacketsFromFrame: writing {header.Count} packets");
        var span = new Span<byte>(framePtr, (int)header.TotalSize);
        
        // _log.Debug($"[{(nuint)framePtr:X}] [{proto}{direction}] proto {header.Protocol} unk {header.Unknown}, {header.Count} pkts size {header.TotalSize} usize {header.DecompressedLength}");
        
        var data = span.Slice(headerSize, (int)header.TotalSize - headerSize);
        
        // Compression
        if (header.Compression != CompressionType.None)
        {
            _notificationManager.AddNotification(new Notification
	            {
		            Content = $"[{proto}{direction}] A frame was compressed.",
		            Title = "Chronofoil Error", 
	            });
            // _log.Debug($"frame compressed: {header.Compression} payload is {header.TotalSize - 40} bytes, decomp'd is {header.DecompressedLength}");
            return;
        }

        var offset = 0;
        for (int i = 0; i < header.Count; i++)
        {
	        var pktHdrSize = Unsafe.SizeOf<PacketElementHeader>();
            var pktHdrSlice = data.Slice(offset, pktHdrSize);
            _buffer.Write(pktHdrSlice); 
            var pktHdr = pktHdrSlice.Cast<byte, PacketElementHeader>();

            // _log.Debug($"packet: type {pktHdr.Type}, {pktHdr.Size} bytes, {proto} {direction}, {pktHdr.SrcEntity} -> {pktHdr.DstEntity}");
            
            var pktData = data.Slice(offset + pktHdrSize, (int)pktHdr.Size - pktHdrSize);
            var pktOpcode = BitConverter.ToUInt16(pktData[2..5]);

            // The server sends a keepalive packet to the client right after connection, indicating a new connection
            var isNetworkInit = proto == Protocol.Lobby && direction == Direction.Rx && pktHdr.Type is PacketType.KeepAlive;
            var canInitEncryption = proto == Protocol.Lobby && pktHdr.Type is PacketType.EncryptionInit;
            var needsDecryption = proto == Protocol.Lobby && pktHdr.Type is PacketType.Ipc or PacketType.Unknown_A;

            var canInitDeobfuscation = proto == Protocol.Zone &&
                                       direction == Direction.Rx &&
                                       (pktOpcode == _versionConstants.InitZoneOpcode || pktOpcode == _versionConstants.UnknownObfuscationInitOpcode);
            var needsDeobfuscation = proto == Protocol.Zone &&
                                     direction == Direction.Rx &&
                                     _versionConstants.ObfuscatedOpcodes.ContainsValue(pktOpcode);
            
            if (isNetworkInit)
	            NetworkInitialized?.Invoke();
            
            if (canInitEncryption)
	            _encryptionProvider.Initialize(pktData);

            if (canInitDeobfuscation)
            {
	            if (pktOpcode == _versionConstants.InitZoneOpcode)
	            {
					_keyGenerator.GenerateFromInitZone(pktData);    
	            }
	            else if (pktOpcode == _versionConstants.UnknownObfuscationInitOpcode)
	            {
		            _keyGenerator.GenerateFromUnknownInitializer(pktData);
	            }
	            
	            _obfuscationOverride = false;
            }
            
            if (_encryptionProvider.Initialized && needsDecryption)
            {
                var decoded = _encryptionProvider.DecryptPacket(pktData);
                pktData = new Span<byte>(decoded);
            }
            
            if (needsDeobfuscation && (_keyGenerator.ObfuscationEnabled || _obfuscationOverride))
            {
	            // This is a hotter path than lobby encryption so I care more about the allocations here
	            var pos = _buffer.Size;
	            _buffer.Write(pktData);
	            var slice = _buffer.Get(pos, pktData.Length);
	            _unscrambler.Unscramble(slice, _keyGenerator.Keys[0], _keyGenerator.Keys[1], _keyGenerator.Keys[2]);
            }
            else
            {
	            _buffer.Write(pktData);    
            }
            
            // _log.Debug($"packet: type {pktHdr.Type}, {pktHdr.Size} bytes, {pktHdr.SrcEntity} -> {pktHdr.DstEntity}");
            offset += (int)pktHdr.Size;
        }
        
        // _log.Debug($"[{proto}{direction}] invoking network event header size {header.TotalSize} usize {header.DecompressedLength} buffer size {_buffer.GetBuffer().Length}");
        NetworkEvent?.Invoke(proto, direction, _buffer.GetBuffer());
    }
}