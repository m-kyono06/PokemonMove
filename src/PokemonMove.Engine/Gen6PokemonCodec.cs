using PKHeX.Core;
using PokemonMove.Domain;

namespace PokemonMove.Engine;

public sealed class Gen6PokemonCodec : IPokemonCodec
{
    public PokemonSummary Decode(byte[] data, string formatKey = "pk6")
    {
        if (formatKey != "pk6") throw new InvalidDataException("第6世代の個体データではありません。");
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != new PK6().SIZE_STORED)
            throw new InvalidDataException("第6世代のポケモンデータのサイズが不正です。");
        var pokemon = new PK6(data.ToArray());
        if (pokemon.Species is 0 or > 721 || !pokemon.ChecksumValid)
            throw new InvalidDataException("ポケモンデータの検証に失敗しました。");
        return PokemonCodec.Describe(pokemon, 0);
    }
}
