using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

public sealed class Gen5PokemonCodec : IPokemonCodec
{
    public PokemonSummary Decode(byte[] data, string formatKey = "pk5")
    {
        if (formatKey != "pk5") throw new InvalidDataException("第5世代の個体データではありません。");
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != new PK5().SIZE_STORED)
            throw new InvalidDataException("第5世代のポケモンデータのサイズが不正です。");
        var pokemon = new PK5(data.ToArray());
        if (pokemon.Species is 0 or > 649 || !pokemon.ChecksumValid)
            throw new InvalidDataException("ポケモンデータの検証に失敗しました。");
        return PokemonCodec.Describe(pokemon, 0);
    }
}
