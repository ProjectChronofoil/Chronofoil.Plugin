using System;
using System.Security.Cryptography;

namespace Chronofoil.Lobby;

public class LobbyEncryptionProvider
{
    public bool Initialized { get; private set; }
    private ushort _gameVersion;
    private Blowfish _blowfish;
    private byte[] _initPacket;

    public void SetGameVersion(ushort gameVersion)
    {
        _gameVersion = gameVersion;
    }

    public void Initialize(Span<byte> initPacket)
    {
        _initPacket = new byte[initPacket.Length];
        initPacket.CopyTo(_initPacket);
        _blowfish = new Blowfish(MakeKey());
        Initialized = true;
    }

    public byte[] DecryptPacket(Span<byte> data)
    {
        var output = new byte[data.Length];
        data.CopyTo(output);
        _blowfish.Decipher(output, 0, output.Length);
        return output;
    }

    private byte[] MakeKey()
    {
        var encKey = new byte[0x2C];

        encKey[0] = 0x78;
        encKey[1] = 0x56;
        encKey[2] = 0x34;
        encKey[3] = 0x12;
        Array.Copy(_initPacket, 0x64, encKey, 4, 4); // timestamp
        encKey[8] = (byte)_gameVersion;
        encKey[9] = (byte)(_gameVersion >> 8);

        {
            const int keyPhaseStartIdx = 0x24;

            var keyPhaseEndIdx = Array.IndexOf<byte>(_initPacket, 0, 36);
            if (keyPhaseEndIdx == -1)
            {
                keyPhaseEndIdx = _initPacket.Length - 1;
            }
            var keyPhaseLength = keyPhaseEndIdx - keyPhaseStartIdx;

            Array.Copy(_initPacket, keyPhaseStartIdx, encKey, 0x0C, keyPhaseLength);
        }
        
        return MD5.HashData(encKey);
    }
}