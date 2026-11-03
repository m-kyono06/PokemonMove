using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

public sealed class Gen7PokemonCodec : IPokemonCodec
{
    public PokemonSummary Decode(byte[] data, string formatKey = "pk7")
    {
        if (formatKey != "pk7") throw new InvalidDataException("第7世代の個体データではありません。");
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != new PK7().SIZE_STORED)
            throw new InvalidDataException("第7世代のポケモンデータのサイズが不正です。");
        var pokemon = new PK7(data.ToArray());
        if (pokemon.Species is 0 or > 807 || !pokemon.ChecksumValid)
            throw new InvalidDataException("ポケモンデータの検証に失敗しました。");
        return PokemonCodec.Describe(pokemon, 0);
    }
}
