using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.TestSupport;
using Xunit;

namespace PokemonMove.Engine.Tests;

public sealed class BackwardTransferTests
{
    private static readonly DateOnly Date = new(2026, 11, 16);
    internal static PKM Create(int generation, int nature = 13, bool shiny = false)
    {
        PKM pk = generation switch { 5 => new PK5(), 6 => new PK6(), _ => new PK7() };
        pk.Species = 25; pk.Language = 1; pk.Version = generation switch { 5 => GameVersion.B2, 6 => GameVersion.AS, _ => GameVersion.UM };
        pk.ID32 = 0; pk.PID = shiny ? 8u << 16 : 0xDEADBEEF; pk.Nature = (Nature)nature;
        pk.Gender = 0; pk.Ability = 9; pk.AbilityNumber = 1; pk.Ball = 4;
        pk.OriginalTrainerName = "テスト"; pk.Nickname = "ピカ"; pk.IsNicknamed = true; pk.OriginalTrainerGender = 1;
        pk.CurrentLevel = 50; pk.IV_HP = 31; pk.IV_ATK = 30; pk.IV_SPE = 15;
        pk.EV_HP = 6; pk.EV_ATK = 252; pk.EV_SPE = 252;
        pk.Move1 = 85; pk.Move2 = 33; pk.Move1_PP = 1; pk.Move1_PPUps = 2;
        pk.RelearnMove1 = 85; pk.OriginalTrainerFriendship = 123;
        pk.MetLocation = 5; pk.MetDate = new(2020, 1, 1); pk.MetLevel = 5;
        return pk;
    }
    internal static PokemonSummary Summary(PKM pk)
    {
        var data = new byte[pk.SIZE_STORED]; pk.WriteEncryptedDataStored(data);
        return new PokemonCodec().Decode(data, $"pk{pk.Format}");
    }
    private static PKM Decode(PokemonSummary p) => p.FormatKey switch
    { "pk4" => new PK4(p.StoredData.ToArray()), "pk5" => new PK5(p.StoredData.ToArray()), _ => new PK6(p.StoredData.ToArray()) };
    public static IEnumerable<object[]> Routes => from generation in new[] { 5, 6, 7 }
        from target in new[] { "dp", "pt", "hgss", "bw", "b2w2", "xy", "oras" }
        let targetGeneration = target is "dp" or "pt" or "hgss" ? 4 : target is "bw" or "b2w2" ? 5 : 6
        where generation > targetGeneration select new object[] { generation, target, targetGeneration };

    [Theory]
    [MemberData(nameof(Routes))]
    public void NativeNewerPokemonCanMoveBackwardsWhilePreservingPromisedFields(int generation, string target, int targetGeneration)
    {
        var original = Create(generation); var summary = Summary(original); var before = summary.StoredData.ToArray();
        var plan = new PokemonTransfer().Prepare(summary, target, Date); var pk = Decode(plan.Pokemon);
        Assert.True(plan.IsGenerationTransfer); Assert.Equal(targetGeneration, pk.Format);
        Assert.Equal(original.Species, pk.Species); Assert.Equal(original.Form, pk.Form);
        Assert.Equal(original.Nature, pk.Nature); Assert.Equal(original.Gender, pk.Gender); Assert.True(pk.IsGenderValid());
        Assert.Equal(original.IsShiny, pk.IsShiny); Assert.Equal(original.ID32, pk.ID32);
        Assert.Equal(original.OriginalTrainerName, pk.OriginalTrainerName); Assert.Equal(original.OriginalTrainerGender, pk.OriginalTrainerGender);
        Assert.Equal(original.Nickname, pk.Nickname); Assert.Equal(original.IsNicknamed, pk.IsNicknamed);
        Assert.Equal(original.Ability, pk.Ability); Assert.Equal(original.CurrentLevel, pk.CurrentLevel);
        Assert.Equal(summary.Details!.IndividualValues, plan.Pokemon.Details!.IndividualValues);
        Assert.Equal(summary.Details.EffortValues, plan.Pokemon.Details.EffortValues);
        Assert.Equal(original.Move1, pk.Move1); Assert.Equal(original.Move2, pk.Move2); Assert.Equal(2, pk.Move1_PPUps); Assert.True(pk.Move1_PP > 1);
        Assert.Equal(Date, pk.MetDate); Assert.Equal(0, pk.MetLocation); Assert.Equal(50, pk.MetLevel); Assert.True(pk.ChecksumValid);
        Assert.Equal(targetGeneration, pk.Generation); Assert.Contains(plan.Notices, n => n.Contains("非公式"));
        Assert.Contains(plan.Notices, n => n.Contains("リボン")); Assert.Equal(before, summary.StoredData);
        for (var i = 0; i < 3; i++) Assert.Equal(plan.Pokemon.StoredData, new PokemonTransfer().Prepare(summary, target, Date).Pokemon.StoredData);
    }

    public static IEnumerable<object[]> Natures => from nature in Enumerable.Range(0, 25) from shiny in new[] { false, true } select new object[] { nature, shiny };
    [Theory]
    [MemberData(nameof(Natures))]
    public void AllNaturesAndNewShinyThresholdAreReconciledWithGen4Pid(int nature, bool shiny)
    {
        var original = Create(7, nature, shiny); var plan = new PokemonTransfer().Prepare(Summary(original), "hgss", Date);
        var pk = new PK4(plan.Pokemon.StoredData.ToArray());
        Assert.Equal(original.Nature, pk.Nature); Assert.Equal(shiny, pk.IsShiny); Assert.Equal(original.Gender, pk.Gender);
        Assert.True(pk.IsGenderValid()); Assert.Equal(0u, pk.PID & 1); Assert.Equal(original.ID32, pk.ID32);
        if (shiny) Assert.NotEqual(original.PID, pk.PID); // XOR 8 is shiny in Gen7 but not Gen4.
    }

    [Theory]
    [InlineData("species")][InlineData("form")][InlineData("move")][InlineData("relearn")]
    [InlineData("ability")][InlineData("hidden")][InlineData("item")][InlineData("egg")]
    [InlineData("ball")][InlineData("language")][InlineData("name")][InlineData("date")]
    public void IncompatibleFeaturesAreRejectedWithoutChangingSourceOrSave(string condition)
    {
        var pk = Create(7);
        switch (condition)
        {
            case "species": pk.Species = 722; break;
            case "form": pk.Species = 26; pk.Form = 1; break;
            case "move": pk.Move1 = 600; break;
            case "relearn": pk.RelearnMove1 = 600; break;
            case "ability": pk.Ability = 233; break;
            case "hidden": pk.Ability = 31; pk.AbilityNumber = 4; break;
            case "item": pk.HeldItem = 1; break;
            case "egg": pk.IsEgg = true; break;
            case "ball": pk.Ball = 26; break;
            case "language": pk.Language = 9; break;
            case "name": pk.Nickname = "abcdefghijkl"; break;
        }
        var summary = Summary(pk); var before = summary.StoredData.ToArray();
        var save = Gen4Fixtures.Create(GameVersion.SS); var original = save.ToArray();
        Assert.Throws<InvalidDataException>(() => new GameSaveEditor().Insert(save, new(false, 0, 0), summary.StoredData, summary.FormatKey, condition == "date" ? null : Date));
        Assert.Equal(before, summary.StoredData); Assert.Equal(original, save);
    }

    [Theory]
    [InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)][InlineData(6)]
    public void PikachuCapsCannotBecomeUnrelatedCosplayForms(int form)
    {
        var pk = Create(7); pk.Form = (byte)form;
        Assert.Throws<InvalidDataException>(() => new PokemonTransfer().Prepare(Summary(pk), "oras", Date));
    }

    [Fact]
    public void ApricornBallIsNotSilentlyReplacedWhenReturningToDiamond()
    {
        var pk = Create(7); pk.Ball = 17;
        Assert.Throws<InvalidDataException>(() => new PokemonTransfer().Prepare(Summary(pk), "dp", Date));
        var plan = new PokemonTransfer().Prepare(Summary(pk), "hgss", Date);
        Assert.Equal(17, new PK4(plan.Pokemon.StoredData.ToArray()).Ball);
    }

    [Fact]
    public void PersistentFormIsPreservedOnlyWhereThatGameSupportsIt()
    {
        var pk = Create(7); pk.Species = 479; pk.Form = 1; pk.Gender = 2; pk.Ability = 26;
        Assert.Throws<InvalidDataException>(() => new PokemonTransfer().Prepare(Summary(pk), "dp", Date));
        var plan = new PokemonTransfer().Prepare(Summary(pk), "hgss", Date);
        var converted = new PK4(plan.Pokemon.StoredData.ToArray());
        Assert.Equal(479, converted.Species); Assert.Equal(1, converted.Form); Assert.Equal(2, converted.Gender);
    }

    [Theory]
    [InlineData(0, false)][InlineData(1, false)][InlineData(0, true)][InlineData(1, true)]
    public void FemalePokemonCanKeepEitherNormalAbilityAndShininess(int ability, bool shiny)
    {
        var pk = Create(7, 3, shiny); pk.Species = 133; pk.Gender = 1;
        pk.Ability = ability == 0 ? 50 : 91; pk.AbilityNumber = 1 << ability;
        var result = new PK4(new PokemonTransfer().Prepare(Summary(pk), "hgss", Date).Pokemon.StoredData.ToArray());
        Assert.Equal(pk.Ability, result.Ability); Assert.Equal(ability, result.PIDAbility);
        Assert.Equal(1, result.Gender); Assert.True(result.IsGenderValid());
        Assert.Equal(pk.Nature, result.Nature); Assert.Equal(pk.IsShiny, result.IsShiny);
    }

    [Theory]
    [InlineData(81, 2, 42)][InlineData(32, 0, 38)][InlineData(29, 1, 38)]
    public void GenderlessAndSingleGenderSpeciesKeepTheirGender(int species, byte gender, int ability)
    {
        var pk = Create(7); pk.Species = (ushort)species; pk.Gender = gender; pk.Ability = ability;
        var result = new PK4(new PokemonTransfer().Prepare(Summary(pk), "hgss", Date).Pokemon.StoredData.ToArray());
        Assert.Equal(gender, result.Gender); Assert.True(result.IsGenderValid());
    }

    [Fact]
    public void HyperTrainingEffectsAreNotForgedAsRawIvsAndAreDisclosed()
    {
        var pk = (PK7)Create(7); pk.HyperTrainFlags = 63;
        var plan = new PokemonTransfer().Prepare(Summary(pk), "xy", Date);
        var result = new PK6(plan.Pokemon.StoredData.ToArray());
        Assert.Equal(15, result.IV_SPE); Assert.Equal(30, result.IV_ATK);
        Assert.Contains(plan.Notices, n => n.Contains("すごいとっくん")); Assert.Equal(0, plan.Pokemon.Details!.HyperTrainingCount);
    }
}
