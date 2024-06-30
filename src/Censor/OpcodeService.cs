using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Chronofoil.CaptureFile.Censor;
using Chronofoil.Common.Censor;
using Chronofoil.Utility;
using Chronofoil.Web;
using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;

namespace Chronofoil.Censor;

/// <summary>
/// Responsible for managing censorable opcodes and crowdsourcing censorable opcodes at runtime.
/// </summary>
public class OpcodeService : IDisposable
{
    private readonly IPluginLog _log;
    private readonly IDalamudPluginInterface _pi;
    private readonly ChronofoilClient _chronofoilClient;
    
    private readonly OpcodeStore _store;
    private readonly string _gameVer;
    
    private string StorePath => Path.Combine(_pi.GetPluginConfigDirectory(), "OpcodeStore.json");

    private const string ZoneChatUpSig = "C7 45 ?? ?? ?? ?? ?? 0F C6 C0 27";
    private const string ZoneLetterUpSig = "C7 44 24 ?? ?? ?? ?? ?? 48 C7 44 24 ?? ?? ?? ?? ?? 0F 10 00 48 89 74 24";

    private const string ZoneChatDownSig = "E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 85 C0 74 78";
    private const string ZoneLetterListDownSig = "48 89 5C 24 ?? 55 56 57 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 48 8B E9 33 DB";
    private const string ZoneLetterDownSig = "48 89 5C 24 ?? 56 41 56 41 57 48 83 EC 30 8B 81";
    
    private delegate void GenericPacketHandlerDelegate(nuint thisPtr, nuint packet);
    
    private Hook<GenericPacketHandlerDelegate>? _zoneChatDownHook;
    private Hook<GenericPacketHandlerDelegate>? _zoneLetterListDownHook;
    private Hook<GenericPacketHandlerDelegate>? _zoneLetterDownHook;

    public OpcodeService(IPluginLog log, IDalamudPluginInterface pi, ChronofoilClient chronofoilClient, ISigScanner sigScanner, IGameInteropProvider hooks)
    {
        _log = log;
        _pi = pi;
        _chronofoilClient = chronofoilClient;
        _store = OpcodeStore.FromFile(StorePath);
        _gameVer = Util.GetRunningGameVersion();

        HandleUpOpcodes(sigScanner);
        HandleDownOpcodes(sigScanner, hooks);
    }
    
    public void Dispose()
    {
        _zoneChatDownHook?.Dispose();
        _zoneLetterListDownHook?.Dispose();
        _zoneLetterDownHook?.Dispose();
    }
    
    private void HandleUpOpcodes(ISigScanner sigScanner)
    {
        if (!_store.HaveOpcode(_gameVer, KnownCensoredOpcode.ZoneChatUp))
        {
            var zoneChatUpPtr = sigScanner.ScanText(ZoneChatUpSig);
            var zoneChatUpOpcode = Marshal.ReadInt32(zoneChatUpPtr + 3);
            _log.Debug($"[ZoneChatUp] opcode detected: {zoneChatUpOpcode}");
            _store.AddOpcode(_gameVer, KnownCensoredOpcode.ZoneChatUp, zoneChatUpOpcode);
            _store.Save();
        }

        if (!_store.HaveOpcode(_gameVer, KnownCensoredOpcode.ZoneLetterUp))
        {
            var zoneLetterUpPtr = sigScanner.ScanText(ZoneLetterUpSig);
            var zoneLetterUpOpcode = Marshal.ReadInt32(zoneLetterUpPtr + 4);
            _log.Debug($"[ZoneLetterUp] opcode detected: {zoneLetterUpOpcode}");
            _store.AddOpcode(_gameVer, KnownCensoredOpcode.ZoneLetterUp, zoneLetterUpOpcode);
            _store.Save();
        }
    }

    private void HandleDownOpcodes(ISigScanner sigScanner, IGameInteropProvider hooks)
    {
        if (!_store.HaveOpcode(_gameVer, KnownCensoredOpcode.ZoneLetterDown))
        {
            var zoneLetterDownPtr = sigScanner.ScanText(ZoneLetterDownSig);
            _zoneLetterDownHook = hooks.HookFromAddress<GenericPacketHandlerDelegate>(zoneLetterDownPtr, ZoneLetterDownDetour);
            _zoneLetterDownHook.Enable();
        }

        if (!_store.HaveOpcode(_gameVer, KnownCensoredOpcode.ZoneLetterListDown))
        {
            var zoneLetterListDownPtr = sigScanner.ScanText(ZoneLetterListDownSig);
            _zoneLetterListDownHook = hooks.HookFromAddress<GenericPacketHandlerDelegate>(zoneLetterListDownPtr, ZoneLetterListDownDetour);
            _zoneLetterListDownHook.Enable();
        }

        if (!_store.HaveOpcode(_gameVer, KnownCensoredOpcode.ZoneChatDown))
        {
            var zoneChatDownPtr = sigScanner.ScanText(ZoneChatDownSig);
            _zoneChatDownHook = hooks.HookFromAddress<GenericPacketHandlerDelegate>(zoneChatDownPtr, ZoneChatDownDetour);
            _zoneChatDownHook.Enable();
        }
    }

    private void ZoneLetterDownDetour(nuint thisPtr, nuint packet)
    {
        var zoneLetterDownOpcode = Marshal.ReadInt16((IntPtr)(packet - 14));
        _log.Debug($"[ZoneLetterDownDetour] opcode detected: {zoneLetterDownOpcode}");
        _store.AddOpcode(_gameVer, KnownCensoredOpcode.ZoneLetterDown, zoneLetterDownOpcode);
        _store.Save();
        _zoneLetterDownHook!.Original(thisPtr, packet);
        _zoneLetterDownHook.Disable();
    }
    
    private void ZoneLetterListDownDetour(nuint thisPtr, nuint packet)
    {
        var zoneLetterListDownOpcode = Marshal.ReadInt16((IntPtr)(packet - 14));
        _log.Debug($"[ZoneLetterListDownDetour] opcode detected: {zoneLetterListDownOpcode}");
        _store.AddOpcode(_gameVer, KnownCensoredOpcode.ZoneLetterListDown, zoneLetterListDownOpcode);
        _store.Save();
        _zoneLetterListDownHook!.Original(thisPtr, packet);
        _zoneLetterListDownHook.Disable();
    }
    
    private void ZoneChatDownDetour(nuint thisPtr, nuint packet)
    {
        var zoneChatDownOpcode = Marshal.ReadInt16((IntPtr)(packet - 14));
        _log.Debug($"[ZoneChatDownDetour] opcode detected: {zoneChatDownOpcode}");
        _store.AddOpcode(_gameVer, KnownCensoredOpcode.ZoneChatDown, zoneChatDownOpcode);
        _store.Save();
        _zoneChatDownHook!.Original(thisPtr, packet);
        _zoneChatDownHook.Disable();
    }
    
    public void UpdateIfNeeded(string gameVersion)
    {
        SendOpcodes(gameVersion);
        if (_store.IsExhaustive(gameVersion)) return;
        UpdateCensoredOpcodes(gameVersion);
    }

    private void SendOpcodes(string gameVersion)
    {
        var foundRequest = new FoundOpcodesRequest
        {
            GameVersion = gameVersion,
            Opcodes = _store.GetOpcodesForGameVersion(gameVersion)
        };

        _chronofoilClient.TrySendOpcodes(foundRequest);   
    }

    private void UpdateCensoredOpcodes(string gameVersion)
    {
        _chronofoilClient.TryGetCensoredOpcodes(gameVersion, out var censoredOpcodes);

        if (censoredOpcodes.GameVersion != gameVersion)
            throw new Exception("What");

        foreach (var opcode in censoredOpcodes.Opcodes)
            _store.AddOpcode(gameVersion, Enum.Parse<KnownCensoredOpcode>(opcode.Key), opcode.Value);
        _store.Save();
    }

    public bool HaveAllOpcodes(string gameVersion)
    {
        return _store.IsExhaustive(gameVersion);
    }
    
    public Dictionary<string, int> GetCensoredOpcodes(string gameVersion)
    {
        return _store.GetOpcodesForGameVersion(gameVersion);
    }
}

internal class OpcodeStore
{
    [JsonIgnore] private string _path = "";

    [JsonProperty]
    private readonly Dictionary<string, List<CensorTarget>> _store;

    public OpcodeStore() { }
    
    public OpcodeStore(string path)
    {
        _path = path;
        _store = new Dictionary<string, List<CensorTarget>>();
        Save();
    }

    public static OpcodeStore FromFile(string path)
    {
        if (!File.Exists(path)) return new OpcodeStore(path);
        var text = File.ReadAllText(path);
        var store = JsonConvert.DeserializeObject<OpcodeStore>(text);
        store._path = path;
        return store;
    }

    public void Save()
    {
        var text = JsonConvert.SerializeObject(this);
        Dalamud.Utility.Util.WriteAllTextSafe(_path, text);
    }
    
    public void AddOpcode(string gameVersion, KnownCensoredOpcode opcodeType, int value)
    {
        if (HaveOpcode(gameVersion, opcodeType)) return;
        if (!_store.ContainsKey(gameVersion))
            _store[gameVersion] = [];
        var opcode = CensorRegistry.GetCensorTarget(opcodeType) with { Opcode = value };
        _store[gameVersion].Add(opcode);
    }

    public bool HaveOpcode(string gameVersion, KnownCensoredOpcode opcodeType)
    {
        return _store.TryGetValue(gameVersion, out var versionDict) && versionDict.Any(c => c.Descriptor == opcodeType);
    }

    public bool IsExhaustive(string gameVersion)
    {
        if (!_store.TryGetValue(gameVersion, out var opcodes)) return false;
        return opcodes.Select(o => o.Descriptor).Distinct().Count() == Enum.GetValues<KnownCensoredOpcode>().Length;
    }

    public Dictionary<string, int> GetOpcodesForGameVersion(string gameVersion)
    {
        // deep copy
        return _store[gameVersion].ToDictionary(x => x.Descriptor.ToString(), x => x.Opcode);
    }
}