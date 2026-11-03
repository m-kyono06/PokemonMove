using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

public sealed class Gen4PokemonCodec : IPokemonCodec
{
    private static readonly int StoredSize = new PK4().SIZE_STORED;
    public PokemonSummary Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != StoredSize)
            throw new InvalidDataException("第4世代のポケモンデータのサイズが不正です。");
        var pokemon = new PK4(data.ToArray());
        if (pokemon.Species is 0 or > 493 || !pokemon.ChecksumValid)
            throw new InvalidDataException("ポケモンデータの検証に失敗しました。");
        return Describe(pokemon, 0);
    }

    internal static PokemonSummary Describe(PKM p, int slot)
    {
        if (p.Species == 0) return new(slot, 0, "空き", "", 0, "", [], false);
        var strings = GameInfo.GetStrings("ja");
        string At(string[] values, int index) => index >= 0 && index < values.Length ? values[index] : $"#{index}";
        var personal = p.PersonalInfo;
        var types = new[] { At(strings.types, personal.Type1), At(strings.types, personal.Type2) }.Distinct();
        var data = Array.Empty<byte>();
        // Check before writing: PKHeX refreshes the entity checksum during serialization.
        if (p.Species <= 493 && p.ChecksumValid)
        {
            data = new byte[p.SIZE_STORED];
            p.WriteEncryptedDataStored(data);
        }
        PokemonDetails? details = null;
        if (data.Length != 0 && !p.IsEgg)
        {
            var calculated = p.Clone();
            calculated.ResetPartyStats();
            details = new(p.Gender switch { 0 => "♂", 1 => "♀", _ => "性別不明" },
                At(strings.natures, (int)p.Nature), At(strings.abilitylist, p.Ability),
                p.HeldItem == 0 ? "なし" : At(strings.itemlist, p.HeldItem),
                new(calculated.Stat_HPMax, calculated.Stat_ATK, calculated.Stat_DEF, calculated.Stat_SPA, calculated.Stat_SPD, calculated.Stat_SPE),
                p.PartyStatsPresent ? p.Stat_HPCurrent : null,
                new(p.EV_HP, p.EV_ATK, p.EV_DEF, p.EV_SPA, p.EV_SPD, p.EV_SPE),
                new(p.IV_HP, p.IV_ATK, p.IV_DEF, p.IV_SPA, p.IV_SPD, p.IV_SPE), p.TID16);
        }
        return new(slot, p.Species, At(strings.specieslist, p.Species), p.Nickname,
            p.CurrentLevel, string.Join(" / ", types),
            new[] { p.Move1, p.Move2, p.Move3, p.Move4 }.Where(m => m != 0)
                .Select(m => At(strings.movelist, m)).ToArray(), p.IsShiny) { StoredData = data, IsEgg = p.IsEgg, Form = p.Form, Details = details };
    }
}
