using System.Buffers.Binary;
using PKHeX.Core;
using PokemonMove.Engine;

namespace PokemonMove.TestSupport;

internal static class Gen4Fixtures
{
    internal static (int General, int StorageStart, int StorageSize, int Footer) Layout(GameVersion version) => version switch
    {
        GameVersion.D or GameVersion.P => (0xC100, 0xC100, 0x121E0, 0x14),
        GameVersion.Pt => (0xCF2C, 0xCF2C, 0x121E4, 0x14),
        _ => (0xF628, 0xF700, 0x12310, 0x10),
    };
    internal static byte[] Create(GameVersion version, bool dsv = false, bool optionalDamage = false,
        int generalPartition = 0, int storagePartition = 0, PK4? boxPokemon = null)
    {
        SAV4 save = version switch
        {
            GameVersion.D or GameVersion.P => new SAV4DP(new byte[0x80000]),
            GameVersion.Pt => new SAV4Pt(new byte[0x80000]),
            _ => new SAV4HGSS(new byte[0x80000]) { Version = version },
        };
        var layout = Layout(version);
        save.Magic = SAV4.MAGIC_JAPAN_INTL;
        foreach (var partition in new[] { 0, 0x40000 })
            BinaryPrimitives.WriteInt32LittleEndian(save.Data.Slice(partition + layout.General - 0xC, 4), layout.General);
        save.OT = "テスト"; save.Language = 1;
        for (var slot = 0; slot < 2; slot++)
        {
            var pk = new PK4 { Species = (ushort)(393 + slot), CurrentLevel = 20, PID = (uint)(100 + slot), Version = version };
            pk.ResetPartyStats(); save.SetPartySlotAtIndex(pk, slot);
        }
        var boxed = boxPokemon ?? new PK4 { Species = 387, Nickname = "ナエ", CurrentLevel = 15, PID = 90,
            Version = version, TID16 = 42, IV_HP = 31, EV_SPE = 12, Move1 = 33 };
        save.SetBoxSlotAtIndex(boxed, 17, 29, EntityImportSettings.None);
        ((IBoxDetailName)save).SetBoxName(17, "さいご");
        var raw = save.Write().ToArray();
        raw.AsSpan(0, 0x40000).CopyTo(raw.AsSpan(0x40000));
        foreach (var (partition, start, length) in new[]
        {
            (generalPartition, 0, layout.General), (storagePartition, layout.StorageStart, layout.StorageSize),
        })
        {
            var block = raw.AsSpan(partition * 0x40000 + start, length);
            BinaryPrimitives.WriteUInt32LittleEndian(block[^0x14..], 10);
            BinaryPrimitives.WriteUInt16LittleEndian(block[^2..], Checksums.CRC16_CCITT(block[..^layout.Footer]));
        }
        if (optionalDamage) raw[version is GameVersion.HG or GameVersion.SS ? 0x27010 : 0x20010] ^= 0xFF;
        if (!dsv) return raw;
        var result = new byte[raw.Length + 0x7A]; raw.CopyTo(result, 0);
        "|-DESMUME SAVE-|"u8.CopyTo(result.AsSpan(result.Length - "|-DESMUME SAVE-|"u8.Length));
        return result;
    }
}
