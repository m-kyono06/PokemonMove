using PKHeX.Core;

namespace PokemonMove.TestSupport;

internal static class Gen5Fixtures
{
    internal static byte[] Create(GameVersion version, bool dsv = false, PK5? boxPokemon = null, int unlocked = 24)
    {
        SAV5 save = version is GameVersion.B or GameVersion.W ? new SAV5BW() : new SAV5B2W2();
        save.Version = version; save.Language = 1; save.OT = "テスト"; save.BoxesUnlocked = unlocked;
        for (var i = 0; i < 2; i++)
        {
            var pk = new PK5 { Species = (ushort)(495 + i), CurrentLevel = 20, PID = (uint)(100 + i), Version = version };
            pk.ResetPartyStats(); save.SetPartySlotAtIndex(pk, i, EntityImportSettings.None);
        }
        var boxed = boxPokemon ?? new PK5 { Species = 649, Nickname = "ゲノ", CurrentLevel = 50, PID = 91,
            Version = version, TID16 = 42, IV_HP = 31, IV_ATK = 30, IV_SPA = 1, IV_SPD = 12, IV_DEF = 15, IV_SPE = 0,
            EV_SPE = 252, EV_ATK = 252, EV_HP = 6, Nature = Nature.Modest, Ability = 88, Move1 = 546 };
        save.SetBoxSlotAtIndex(boxed, 23, 29, EntityImportSettings.None); save.SetBoxName(23, "さいご");
        // Nonzero backup/download data lets tests detect accidental writes outside the selected primary block.
        var backup = version is GameVersion.B or GameVersion.W ? 0x24000 : 0x26000;
        save.Data[backup..].Fill(0xA5);
        var raw = save.Write().ToArray();
        if (!dsv) return raw;
        var result = new byte[raw.Length + 0x7A];raw.CopyTo(result,0);
        "|-DESMUME SAVE-|"u8.CopyTo(result.AsSpan(result.Length - "|-DESMUME SAVE-|"u8.Length));
        return result;
    }
}
