using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using Xunit;

namespace PokemonMove.Engine.Tests;

public sealed class TradeEvolutionTests
{
    private static PokemonSummary Pokemon(int generation, ushort species, int item = 0, byte form = 0, bool nicknamed = true, int slot = 0)
    {
        PKM pk = generation switch { 4 => new PK4(), 5 => new PK5(), 6 => new PK6(), _ => new PK7() };
        pk.Species = species; pk.Form = form; pk.Version = generation switch { 4 => GameVersion.SS, 5 => GameVersion.B2, 6 => GameVersion.AS, _ => GameVersion.UM };
        pk.Language = 1; pk.ID32 = 0; pk.PID = slot == 1 ? (generation == 5 ? 1u << 16 : 1u) : 0; pk.EncryptionConstant = 123;
        pk.Nickname = nicknamed ? "ニック" : SpeciesName.GetSpeciesNameGeneration(species, 1, (byte)generation); pk.IsNicknamed = nicknamed;
        pk.OriginalTrainerName = "テスト"; pk.Gender = 0; pk.HeldItem = item; pk.CurrentLevel = 50;
        pk.RefreshAbility(slot); pk.IV_HP = 31; pk.EV_SPE = 252; pk.Move1 = 33; pk.Move1_PP = 1;
        var data = new byte[pk.SIZE_STORED]; pk.WriteEncryptedDataStored(data);
        return new PokemonCodec().Decode(data, $"pk{generation}");
    }
    private static PKM Decode(PokemonSummary p) => p.FormatKey switch
    { "pk4" => new PK4(p.StoredData.ToArray()), "pk5" => new PK5(p.StoredData.ToArray()), "pk6" => new PK6(p.StoredData.ToArray()), _ => new PK7(p.StoredData.ToArray()) };
    public static IEnumerable<object[]> Common => from g in new[] { 4, 5, 6, 7 } from x in new (ushort From, ushort To, int Item)[]
    {
        (64,65,0),(67,68,0),(75,76,0),(93,94,0),
        (61,186,221),(79,199,221),(95,208,233),(123,212,233),(117,230,235),(137,233,252),
        (366,367,226),(366,368,227),(112,464,321),(125,466,322),(126,467,323),(233,474,324),(356,477,325),
    } select new object[] { g, x.From, x.To, x.Item };
    [Theory]
    [MemberData(nameof(Common))]
    public void EvolvesAllSharedTradeLinesAndKeepsIdentityAndTraining(int generation, ushort from, ushort to, int item)
    {
        var source = Pokemon(generation, from, item); var before = source.StoredData.ToArray(); var original = Decode(source);
        var plan = new TradeEvolution().Prepare(source); var pk = Decode(plan.Pokemon);
        Assert.Equal(to, pk.Species); Assert.Equal(original.PID, pk.PID); Assert.Equal(original.ID32, pk.ID32);
        Assert.Equal(original.OriginalTrainerName, pk.OriginalTrainerName); Assert.Equal(original.Version, pk.Version);
        Assert.Equal(original.Nature, pk.Nature); Assert.Equal(original.Gender, pk.Gender); Assert.Equal(original.IsShiny, pk.IsShiny);
        Assert.Equal(original.EXP, pk.EXP); Assert.Equal(original.CurrentLevel, pk.CurrentLevel); Assert.Equal(original.Nickname, pk.Nickname);
        Assert.Equal(original.Move1, pk.Move1); Assert.Equal(original.Move1_PP, pk.Move1_PP);
        Assert.Equal(source.Details!.IndividualValues, plan.Pokemon.Details!.IndividualValues);
        Assert.Equal(source.Details.EffortValues, plan.Pokemon.Details.EffortValues);
        Assert.Equal(0, pk.HeldItem); Assert.True(pk.ChecksumValid); Assert.Null(plan.Partner); Assert.Equal(before, source.StoredData);
        Assert.Equal(plan.Pokemon.StoredData, new TradeEvolution().Prepare(source).Pokemon.StoredData);
    }
    [Theory]
    [InlineData(5,525,526,0)][InlineData(6,525,526,0)][InlineData(7,525,526,0)]
    [InlineData(5,533,534,0)][InlineData(6,533,534,0)][InlineData(7,533,534,0)]
    [InlineData(5,349,350,537)][InlineData(6,349,350,537)][InlineData(7,349,350,537)]
    [InlineData(6,682,683,647)][InlineData(7,682,683,647)]
    [InlineData(6,684,685,646)][InlineData(7,684,685,646)]
    [InlineData(6,708,709,0)][InlineData(7,708,709,0)]
    [InlineData(6,710,711,0)][InlineData(7,710,711,0)]
    public void HandlesLaterGenerationTradeLines(int generation, ushort from, ushort to, int item)
    {
        var p = new TradeEvolution().Prepare(Pokemon(generation, from, item)); Assert.Equal(to, p.Pokemon.Species);
    }
    [Theory]
    [InlineData(5)][InlineData(6)][InlineData(7)]
    public void PairedEvolutionRequiresBothPokemonAndIsSymmetric(int generation)
    {
        var a = Pokemon(generation, 588); var b = Pokemon(generation, 616);
        var plan = new TradeEvolution().Prepare(a, b);
        Assert.Equal(589, plan.Pokemon.Species); Assert.Equal(617, plan.Partner!.Species);
        Assert.Equal(617, new TradeEvolution().Prepare(b, a).Pokemon.Species);
        Assert.Throws<InvalidDataException>(() => new TradeEvolution().Prepare(a));
        Assert.Throws<InvalidDataException>(() => new TradeEvolution().Prepare(a, Pokemon(generation, 25)));
        Assert.Throws<InvalidDataException>(() => new TradeEvolution().Prepare(a, Pokemon(generation, 616, 229)));
        Assert.Throws<InvalidDataException>(() => new TradeEvolution().Prepare(a, Pokemon(generation == 5 ? 6 : 5, 616)));
    }
    [Theory]
    [InlineData(6,710,0)][InlineData(6,710,1)][InlineData(6,710,2)][InlineData(6,710,3)]
    [InlineData(7,710,0)][InlineData(7,710,1)][InlineData(7,710,2)][InlineData(7,710,3)]
    [InlineData(7,75,1)]
    public void KeepsSizeAndRegionalForms(int generation, ushort species, byte form)
    {
        var result = new TradeEvolution().Prepare(Pokemon(generation, species, form: form));
        Assert.Equal(form, result.Pokemon.Form);
    }
    [Fact]
    public void DefaultNameChangesButNicknameIsRetainedAndGengarAbilityFollowsGeneration()
    {
        foreach (var generation in new[] { 4,5,6,7 })
        {
            var p = new TradeEvolution().Prepare(Pokemon(generation,93,nicknamed:false)).Pokemon;
            Assert.Equal("ゲンガー", p.Nickname); Assert.False(Decode(p).IsNicknamed);
            Assert.Equal(generation == 7 ? 130 : 26, Decode(p).Ability);
        }
    }
    [Theory]
    [InlineData(4,1)][InlineData(5,1)][InlineData(5,2)][InlineData(6,1)][InlineData(6,2)][InlineData(7,1)][InlineData(7,2)]
    public void AbilitySlotIsInheritedIncludingHiddenAbilities(int generation,int slot)
    {
        var source = Pokemon(generation,64,slot:slot);
        var original = Decode(source); var evolved = Decode(new TradeEvolution().Prepare(source).Pokemon);
        Assert.Equal(original.AbilityNumber, evolved.AbilityNumber);
        Assert.Equal(evolved.PersonalInfo.GetAbilityAtIndex(slot), evolved.Ability);
        Assert.Equal(original.PID, evolved.PID);
    }
    [Fact]
    public void EggsCannotEvolveAndSourceNeverChangesWhenRefused()
    {
        var source = Pokemon(7,93); var pk = Decode(source); pk.IsEgg = true;
        var data = new byte[pk.SIZE_STORED]; pk.WriteEncryptedDataStored(data);
        var summary = new PokemonCodec().Decode(data,"pk7");var before = summary.StoredData.ToArray();
        Assert.Throws<InvalidDataException>(() => new TradeEvolution().Prepare(summary));
        Assert.Equal(before,summary.StoredData);
    }

    [Fact]
    public void EverstoneBlocksExceptKadabraAndUnrelatedHeldItemsAreNotConsumed()
    {
        Assert.Throws<InvalidDataException>(() => new TradeEvolution().Prepare(Pokemon(7,93,229)));
        Assert.Throws<InvalidDataException>(() => new TradeEvolution().Prepare(Pokemon(7,95)));
        Assert.Throws<InvalidDataException>(() => new TradeEvolution().Prepare(Pokemon(4,349,537)));
        Assert.Throws<InvalidDataException>(() => new TradeEvolution().Prepare(Pokemon(7,25)));
        Assert.Equal(229, Decode(new TradeEvolution().Prepare(Pokemon(4,64,229)).Pokemon).HeldItem);
        Assert.Equal(1, Decode(new TradeEvolution().Prepare(Pokemon(7,93,1)).Pokemon).HeldItem);
    }
}
