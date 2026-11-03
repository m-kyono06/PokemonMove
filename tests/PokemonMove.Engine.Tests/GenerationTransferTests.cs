using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.TestSupport;
using Xunit;

namespace PokemonMove.Engine.Tests;

public sealed class GenerationTransferTests
{
    private static readonly DateOnly Date = new(2026, 11, 3);
    private static PokemonSummary Pokemon(PK4 pk)
    {
        var bytes = new byte[pk.SIZE_STORED]; pk.WriteEncryptedDataStored(bytes);
        return new Gen4PokemonCodec().Decode(bytes);
    }
    [Fact]
    public void ForwardTransferPreservesIdentityTrainingNatureAndShininessAndIsDeterministic()
    {
        var pk = new PK4 { Species = 152, CurrentLevel = 50, PID = 0, ID32 = 0, Version = GameVersion.SS,
            OriginalTrainerName = "コトネ", Nickname = "チコ", Language = 1, MetDate = new(2010, 1, 1),
            IV_HP = 31, IV_ATK = 30, IV_SPE = 15, EV_ATK = 252, EV_SPE = 252, EV_HP = 6,
            Move1 = 33, Move2 = 45, Move1_PP = 1, OriginalTrainerFriendship = 200 };
        var source = Pokemon(pk); var original = source.StoredData.ToArray();
        var plan = new PokemonTransfer().Prepare(source, "bw", Date);
        Assert.True(plan.IsGenerationTransfer); Assert.NotEmpty(plan.Notices); Assert.Equal("pk5", plan.Pokemon.FormatKey);
        var converted = new PK5(plan.Pokemon.StoredData.ToArray());
        Assert.Equal(pk.Species, converted.Species); Assert.Equal(pk.PID, converted.PID); Assert.Equal(pk.ID32, converted.ID32);
        Assert.Equal(pk.Nature, converted.Nature); Assert.Equal(pk.IsShiny, converted.IsShiny);
        Assert.Equal(pk.Nickname, converted.Nickname); Assert.Equal(pk.OriginalTrainerName, converted.OriginalTrainerName);
        Assert.Equal(pk.IV_HP, converted.IV_HP); Assert.Equal(pk.IV_ATK, converted.IV_ATK); Assert.Equal(pk.IV_SPE, converted.IV_SPE);
        Assert.Equal(pk.EV_ATK, converted.EV_ATK); Assert.Equal(pk.EV_SPE, converted.EV_SPE);
        Assert.Equal(pk.Move1, converted.Move1); Assert.Equal(pk.Move2, converted.Move2);
        Assert.Equal(70, converted.OriginalTrainerFriendship); Assert.Equal(Date, converted.MetDate);
        Assert.Equal(pk.CurrentLevel, converted.MetLevel); Assert.True(converted.ChecksumValid);
        Assert.True(converted.Move1_PP > 1);
        Assert.Equal(plan.Pokemon.StoredData, new PokemonTransfer().Prepare(source, "b2w2", Date).Pokemon.StoredData);
        Assert.Equal(original, source.StoredData);
        Assert.Throws<InvalidDataException>(() => new PokemonTransfer().Prepare(plan.Pokemon, "hgss"));
        Assert.Throws<InvalidDataException>(() => new GameSaveEditor().Insert(Gen4Fixtures.Create(GameVersion.SS), new(false, 0, 0), plan.Pokemon.StoredData, "pk5"));
    }

    [Theory]
    [InlineData("egg")]
    [InlineData("pichu")]
    [InlineData("item")]
    [InlineData("hm")]
    [InlineData("date")]
    public void DisallowedConversionNeverMutatesSourceOrDestination(string condition)
    {
        var pk = new PK4 { Species = 152, CurrentLevel = 20, Version = GameVersion.SS, Move1 = 33 };
        if (condition == "egg") pk.IsEgg = true;
        if (condition == "pichu") { pk.Species = 172; pk.Form = 1; }
        if (condition == "item") pk.HeldItem = 234;
        if (condition == "hm") pk.Move1 = 57;
        var source = Pokemon(pk); var original = source.StoredData.ToArray();
        var target = Gen5Fixtures.Create(GameVersion.B); var before = target.ToArray();
        Assert.Throws<InvalidDataException>(() => new GameSaveEditor().Insert(target, new(false, 0, 0), source.StoredData, "pk4", condition == "date" ? null : Date));
        Assert.Equal(original, source.StoredData); Assert.Equal(before, target);
    }
}
