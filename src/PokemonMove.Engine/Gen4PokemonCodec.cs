using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

public sealed class Gen4PokemonCodec : IPokemonCodec
{
    private static readonly int StoredSize = new PK4().SIZE_STORED;
    public PokemonSummary Decode(byte[] data, string formatKey = "pk4")
    {
        if (formatKey != "pk4") throw new InvalidDataException("第4世代の個体データではありません。");
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != StoredSize)
            throw new InvalidDataException("第4世代のポケモンデータのサイズが不正です。");
        var pokemon = new PK4(data.ToArray());
        if (pokemon.Species is 0 or > 493 || !pokemon.ChecksumValid)
            throw new InvalidDataException("ポケモンデータの検証に失敗しました。");
        return PokemonCodec.Describe(pokemon, 0);
    }

}
