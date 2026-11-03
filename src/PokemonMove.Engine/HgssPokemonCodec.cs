using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

public sealed class HgssPokemonCodec : IPokemonCodec
{
    private static readonly int StoredSize = new PK4().SIZE_STORED;
    public PokemonSummary Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != StoredSize)
            throw new InvalidDataException("HGSSのポケモンデータのサイズが不正です。");
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
        return new(slot, p.Species, At(strings.specieslist, p.Species), p.Nickname,
            p.CurrentLevel, string.Join(" / ", types),
            new[] { p.Move1, p.Move2, p.Move3, p.Move4 }.Where(m => m != 0)
                .Select(m => At(strings.movelist, m)).ToArray(), p.IsShiny) { StoredData = data, IsEgg = p.IsEgg };
    }
}
