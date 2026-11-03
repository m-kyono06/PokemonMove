using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using Xunit;

namespace PokemonMove.Engine.Tests;

public sealed class InspectionTests
{
    [Fact]
    public void CalculatesBoxStatsWithoutModifyingPokemonData()
    {
        var pk = new PK4 { Species = 152, CurrentLevel = 50, PID = 0, Ability = 65,
            IV_HP = 31, IV_ATK = 31, IV_DEF = 31, IV_SPA = 31, IV_SPD = 31, IV_SPE = 31 };
        var data = new byte[pk.SIZE_STORED]; pk.WriteEncryptedDataStored(data);
        var before = data.ToArray();
        var summary = new HgssPokemonCodec().Decode(data);
        var details = Assert.IsType<PokemonDetails>(summary.Details);
        Assert.Equal(new PokemonBattleStats(120, 69, 85, 69, 85, 65), details.Stats);
        Assert.Null(details.CurrentHP); // Box entities have no current battle HP.
        Assert.Equal("しんりょく", details.Ability);
        Assert.Equal("なし", details.HeldItem);
        Assert.Equal(before, data);
        Assert.Equal(before, summary.StoredData);
    }

    [Fact]
    public void SelectionPublishesDisplayOnlyCopyAndCanSwitchToDex()
    {
        var inspection = new PokemonInspection();
        var pokemon = new PokemonSummary(0, 25, "ピカチュウ", "TEST", 10, "でんき", ["でんきショック"], false)
            { StoredData = new byte[] { 1, 2, 3 }, Form = 1 };
        var changed = 0; inspection.Changed += (_, _) => changed++;
        inspection.SelectPokemon(pokemon, "SS / ボックス1");
        var selected = Assert.IsType<PokemonSummary>(inspection.Current.Pokemon);
        Assert.Equal("ピカチュウ", selected.Name);
        Assert.Equal(1, selected.Form);
        Assert.Empty(selected.StoredData);
        Assert.Equal(new byte[] { 1, 2, 3 }, pokemon.StoredData);
        Assert.Null(inspection.Current.Species);
        var species = new DexEntry(152, "チコリータ", 1, 0, 0, 0, 0, 0);
        inspection.SelectSpecies(species, DexVariant.Shiny);
        Assert.Null(inspection.Current.Pokemon);
        Assert.Equal(species, inspection.Current.Species);
        Assert.Equal(DexVariant.Shiny, inspection.Current.Variant);
        inspection.Clear();
        Assert.Null(inspection.Current.Pokemon); Assert.Null(inspection.Current.Species);
        Assert.Equal(3, changed);
    }

    [Fact]
    public void SelectingEmptySlotClearsPreviousInspection()
    {
        var inspection = new PokemonInspection();
        inspection.SelectPokemon(new(0, 25, "ピカチュウ", "", 1, "", [], false), "SS");
        inspection.SelectPokemon(new(0, 0, "空き", "", 0, "", [], false), "SS");
        Assert.Null(inspection.Current.Pokemon);
    }

    [Theory]
    [InlineData(640, 480, 640, 480)]
    [InlineData(1920, 1080, 1080, 1240)]
    public void DefaultRoutingUsesLowerForControlsAndMainForInspector(int w0, int h0, int w1, int h1)
    {
        ScreenPanel[] screens = [new(1, "lower", w1, h1), new(0, "main", w0, h0)];
        Assert.Equal(new ScreenRoute(1, 0), DualScreenRouting.Default(screens));
        Assert.True(DualScreenRouting.IsValid(new(0, 1), screens)); // User can swap roles.
    }

    [Fact]
    public void RoutingRejectsSingleMissingOrSameDisplay()
    {
        ScreenPanel[] one = [new(0, "one", 640, 480)];
        Assert.Null(DualScreenRouting.Default(one)); Assert.Null(DualScreenRouting.Default([]));
        ScreenPanel[] two = [new(2, "first", 640, 480), new(4, "second", 640, 480)];
        Assert.Equal(new ScreenRoute(4, 2), DualScreenRouting.Default(two));
        Assert.False(DualScreenRouting.IsValid(new(2, 2), two));
        Assert.False(DualScreenRouting.IsValid(new(2, 99), two));
    }

    [Fact]
    public void NestedOperationsAllowScreenSwitchOnlyAfterAllWorkAndConfirmationsFinish()
    {
        var operations = new UserOperations(); var idle = 0;
        operations.BecameIdle += (_, _) => idle++;
        var picker = operations.Begin(); var transfer = operations.Begin();
        picker.Dispose(); Assert.True(operations.IsBusy); Assert.Equal(0, idle);
        transfer.Dispose(); Assert.False(operations.IsBusy); Assert.Equal(1, idle);
        transfer.Dispose(); Assert.Equal(1, idle);
    }

}
