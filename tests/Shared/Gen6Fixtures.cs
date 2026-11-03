using System.Buffers.Binary;
using PKHeX.Core;

namespace PokemonMove.TestSupport;

internal static class Gen6Fixtures
{
    internal static byte[] Create(GameVersion version, PK6? boxPokemon = null, int unlocked = 31)
    {
        SAV6 save = version is GameVersion.X or GameVersion.Y ? new SAV6XY() : new SAV6AO();
        save.Version = version; save.OT = "カルム"; save.Gender = 0; save.Country = 1; save.Region = 2;
        save.ConsoleRegion = 0; save.Language = 1; save.BoxesUnlocked = unlocked;
        save.TimeStampCurrent = 123456789;save.TimeStampPrevious = 987654;
        var metadata = save.Data.Length - 0x200;
        BinaryPrimitives.WriteUInt32LittleEndian(save.Data[(metadata + 0x10)..], 0x42454546);
        foreach (var block in save.AllBlocks)
        {
            var entry = save.Data.Slice(metadata + 0x14 + (int)block.ID * 8, 8);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, (uint)block.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], (ushort)block.ID);
        }
        for (var i = 0; i < 2; i++)
        {
            var pk = new PK6 { Species = (ushort)(656 + i), CurrentLevel = 20, PID = (uint)(100 + i), EncryptionConstant = (uint)(777 + i), Version = version };
            pk.ResetPartyStats(); save.SetPartySlotAtIndex(pk, i, EntityImportSettings.None);
        }
        var boxed = boxPokemon ?? new PK6 { Species = 650, Nickname = "ハリ", CurrentLevel = 50, PID = 91, EncryptionConstant = 333,
            Version = version, TID16 = 42, IV_HP = 31, IV_ATK = 31, IV_DEF = 31, IV_SPA = 31, IV_SPD = 31, IV_SPE = 31,
            EV_SPE = 252, EV_ATK = 252, EV_HP = 6, Nature = Nature.Adamant, Ability = 65, Move1 = 33 };
        save.SetBoxSlotAtIndex(boxed, 30, 29, EntityImportSettings.None); ((IBoxDetailName)save).SetBoxName(30, "さいご");
        save.Data.Slice(save is SAV6AO ? 0x67AD0 : 0x570D0, 0x130).Fill(0xA5); // Padding outside box data, next to the photograph.
        save.Data.Slice(save is SAV6AO ? 0x67C00 : 0x57200, 0xE058).Fill(0x6B); // Opaque photograph bytes must be preserved.
        return save.Write().ToArray();
    }
}
