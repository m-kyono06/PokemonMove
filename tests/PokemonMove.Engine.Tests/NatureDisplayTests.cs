using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using Xunit;

namespace PokemonMove.Engine.Tests;

public sealed class NatureDisplayTests
{
    [Theory]
    [InlineData(0, "補正なし")]
    [InlineData(1, "攻撃↑ 防御↓")]
    [InlineData(2, "攻撃↑ 素早さ↓")]
    [InlineData(3, "攻撃↑ 特攻↓")]
    [InlineData(4, "攻撃↑ 特防↓")]
    [InlineData(5, "防御↑ 攻撃↓")]
    [InlineData(6, "補正なし")]
    [InlineData(7, "防御↑ 素早さ↓")]
    [InlineData(8, "防御↑ 特攻↓")]
    [InlineData(9, "防御↑ 特防↓")]
    [InlineData(10, "素早さ↑ 攻撃↓")]
    [InlineData(11, "素早さ↑ 防御↓")]
    [InlineData(12, "補正なし")]
    [InlineData(13, "素早さ↑ 特攻↓")]
    [InlineData(14, "素早さ↑ 特防↓")]
    [InlineData(15, "特攻↑ 攻撃↓")]
    [InlineData(16, "特攻↑ 防御↓")]
    [InlineData(17, "特攻↑ 素早さ↓")]
    [InlineData(18, "補正なし")]
    [InlineData(19, "特攻↑ 特防↓")]
    [InlineData(20, "特防↑ 攻撃↓")]
    [InlineData(21, "特防↑ 防御↓")]
    [InlineData(22, "特防↑ 素早さ↓")]
    [InlineData(23, "特防↑ 特攻↓")]
    [InlineData(24, "補正なし")]
    public void DisplaysAllNatureModifiersFromStoredPokemon(int nature, string expected)
    {
        var pk = new PK4 { Species = 25, PID = (uint)nature, CurrentLevel = 50 };
        var data = new byte[pk.SIZE_STORED]; pk.WriteEncryptedDataStored(data);
        var before = data.ToArray();
        var d = Assert.IsType<PokemonDetails>(new Gen4PokemonCodec().Decode(data).Details);
        Assert.Equal(expected, d.NatureModifiersText);
        Assert.Equal("", d.NatureArrow("HP"));
        if (d.NatureRaisedStat is { } up) Assert.Equal("↑", d.NatureArrow(up));
        if (d.NatureLoweredStat is { } down) Assert.Equal("↓", d.NatureArrow(down));
        Assert.Equal(before, data);
    }
}
