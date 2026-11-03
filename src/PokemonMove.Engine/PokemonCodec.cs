using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

/// <summary>PK4 and PK5 are both 136 bytes; always use an explicit format, never guess from length.</summary>
public sealed class PokemonCodec : IPokemonCodec
{
    public PokemonSummary Decode(byte[] data, string formatKey = "pk4") => formatKey switch
    {
        "pk4" => new Gen4PokemonCodec().Decode(data),
        "pk5" => new Gen5PokemonCodec().Decode(data),
        "pk6" => new Gen6PokemonCodec().Decode(data),
        _ => throw new InvalidDataException("対応していないポケモンのデータ形式です。"),
    };

    internal static PokemonSummary Describe(PKM p, int slot)
    {
        if (p.Species == 0) return new(slot, 0, "空き", "", 0, "", [], false);
        var strings = GameInfo.GetStrings("ja");
        string At(string[] values, int index) => index >= 0 && index < values.Length ? values[index] : $"#{index}";
        var personal = p.PersonalInfo;
        var types = new[] { At(strings.types, personal.Type1), At(strings.types, personal.Type2) }.Distinct();
        var data = Array.Empty<byte>();
        // Check before writing: PKHeX refreshes the entity checksum during serialization.
        if (p.Species <= (p.Format switch { 6 => 721, 5 => 649, _ => 493 }) && p.ChecksumValid)
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
                .Select(m => At(strings.movelist, m)).ToArray(), p.IsShiny) { StoredData = data, IsEgg = p.IsEgg, Form = p.Form, Details = details, FormatKey = p.Format switch { 6 => "pk6", 5 => "pk5", _ => "pk4" } };
    }
}
