using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using Xunit;

namespace PokemonMove.Engine.Tests;

public sealed class CollectionDexTests
{
    private readonly CollectionDex dex = new();
    private static PokemonSummary Pokemon(ushort species, bool shiny = false, bool egg = false)
    {
        var pk = new PK4 { Species = species, CurrentLevel = 12, ID32 = 0, PID = shiny ? 0u : 0x100000u, IsEgg = egg };
        var data = new byte[pk.SIZE_STORED]; pk.WriteEncryptedDataStored(data);
        return new Gen4PokemonCodec().Decode(data);
    }
    private static BankItem Bank(PokemonSummary p) => new(new(Guid.NewGuid(), "SS", "TEST", DateTimeOffset.UtcNow, p.StoredData), p);
    private static SaveSnapshot Save(PokemonSummary[] party, params PokemonSummary[] box) => new("TEST", "SS", party, [new("BOX", box)]);

    [Fact]
    public void EmptyBankOnlyStillListsEveryNationalSpeciesAsMissing()
    {
        var result = dex.Build(null, []);
        Assert.False(result.IncludesSave);
        Assert.Equal(802, result.Entries.Count);
        Assert.Equal((ushort)1, result.Entries[0].Species);
        Assert.Equal("フシギダネ", result.Entries[0].Name);
        Assert.Equal((ushort)802, result.Entries[^1].Species);
        Assert.Equal("マーシャドー", result.Entries[^1].Name);
        Assert.Equal(0, result.Owned(DexVariant.All));
        Assert.Equal(802, dex.Filter(result, DexVariant.All, DexOwnership.Missing, "").Count);
        Assert.Empty(dex.Filter(result, DexVariant.All, DexOwnership.Owned, ""));
    }

    [Fact]
    public void RepeatedSpeciesAcrossPartyBoxesAndLegacyBankCountAsOneSpecies()
    {
        var p = Pokemon(152);
        var bank = Bank(p);
        Assert.False(bank.Entry.TransferEligible);
        var result = dex.Build(Save([p, p], p), [bank]);
        var entry = result.Entries[151];
        Assert.True(result.IncludesSave);
        Assert.Equal(1, result.Owned(DexVariant.All));
        Assert.Equal(2, entry.PartyCount);
        Assert.Equal(1, entry.BoxCount);
        Assert.Equal(1, entry.BankCount);
        Assert.Equal(4, entry.Count(DexVariant.All));
        Assert.Equal(1, result.Owned(DexVariant.Normal));
        Assert.Equal(0, result.Owned(DexVariant.Shiny));
    }

    [Fact]
    public void NormalAndShinyCollectionsAreIndependent()
    {
        var shinyOnly = Pokemon(25, shiny: true);
        Assert.True(shinyOnly.IsShiny);
        var result = dex.Build(Save([Pokemon(152)], Pokemon(152, shiny: true)), [Bank(shinyOnly)]);
        Assert.Equal(2, result.Owned(DexVariant.All));
        Assert.Equal(1, result.Owned(DexVariant.Normal));
        Assert.Equal(2, result.Owned(DexVariant.Shiny));
        Assert.Equal(1, result.Entries[151].Boxes(DexVariant.Shiny));
        Assert.Equal(0, result.Entries[24].Count(DexVariant.Normal));
        Assert.Equal(1, result.Entries[24].Bank(DexVariant.Shiny));
        Assert.Equal(801, dex.Filter(result, DexVariant.Normal, DexOwnership.Missing, "").Count);
    }

    [Fact]
    public void EggsEmptyInvalidAndOutOfGenerationDataAreNotCollected()
    {
        var egg = Pokemon(175, egg: true);
        Assert.True(egg.IsEgg);
        var invalid = new PokemonSummary(0, 25, "ピカチュウ", "", 1, "", [], false);
        var outOfRange = invalid with { Species = 494, StoredData = new byte[136] };
        var empty = invalid with { Species = 0 };
        var result = dex.Build(Save([egg, empty, invalid, outOfRange]), [Bank(egg), Bank(Pokemon(493))]);
        Assert.Equal(1, result.Owned(DexVariant.All));
        Assert.Equal(0, result.Entries[174].Count(DexVariant.All));
        Assert.Equal(1, result.Entries[492].BankCount);
    }

    [Theory]
    [InlineData("ちこり", 152)]
    [InlineData("チコリ", 152)]
    [InlineData("ﾁｺﾘ", 152)]
    [InlineData("１５２", 152)]
    [InlineData(" 001 ", 1)]
    [InlineData("493", 493)]
    public void SearchesJapaneseNamesAndNationalNumbers(string query, int expected)
    {
        var result = dex.Build(null, []);
        var found = Assert.Single(dex.Filter(result, DexVariant.All, DexOwnership.All, query));
        Assert.Equal(expected, found.Species);
    }

    [Fact]
    public void SearchAndOwnershipFiltersCombineWithoutChangingCountsOrSourceData()
    {
        var p = Pokemon(152);
        var original = p.StoredData.ToArray();
        var result = dex.Build(Save([p]), [Bank(p)]);
        Assert.Empty(dex.Filter(result, DexVariant.All, DexOwnership.Missing, "152"));
        Assert.Single(dex.Filter(result, DexVariant.All, DexOwnership.Owned, "152"));
        Assert.Empty(dex.Filter(result, DexVariant.All, DexOwnership.All, "803"));
        Assert.Empty(dex.Filter(result, DexVariant.All, DexOwnership.All, "見つからない名前"));
        Assert.Equal(1, result.Owned(DexVariant.All));
        Assert.Equal(original, p.StoredData);
    }

    [Fact]
    public void AllRegisteredSavesAndBankAreAggregatedWithPerSaveLocations()
    {
        var ss = Save([Pokemon(152)], Pokemon(25, shiny: true)) with { Game = "SS", TrainerName = "SS-PLAYER" };
        var hg = Save([Pokemon(155)], Pokemon(152)) with { Game = "HG", TrainerName = "HG-PLAYER" };
        var result = dex.BuildAll([ss, hg], [Bank(Pokemon(158)), Bank(Pokemon(152))]);
        Assert.Equal(2, result.SaveCount);
        Assert.Equal(4, result.Owned(DexVariant.All));
        Assert.Equal(1, result.Owned(DexVariant.Shiny));
        var chikorita = result.Entries[151];
        Assert.Equal(3, chikorita.Count(DexVariant.All));
        Assert.Equal(2, chikorita.Saves.Count);
        Assert.Equal("SS", chikorita.Saves[0].Game);
        Assert.Equal(1, chikorita.Saves[0].PartyCount);
        Assert.Equal("HG", chikorita.Saves[1].Game);
        Assert.Equal(1, chikorita.Saves[1].BoxCount);
        Assert.Equal(1, result.Entries[24].Saves[0].ShinyBoxCount);
        // Repeated updates are recomputed, never appended to an old collection.
        Assert.Equal(result.Owned(DexVariant.All), dex.BuildAll([ss, hg], [Bank(Pokemon(158)), Bank(Pokemon(152))]).Owned(DexVariant.All));
    }

    [Fact]
    public void AggregatesGen4Gen5AndBankIntoOne802SpeciesCollection()
    {
        var old = new GameSaveReader().Read(PokemonMove.TestSupport.Gen4Fixtures.Create(GameVersion.SS));
        var bw = new GameSaveReader().Read(PokemonMove.TestSupport.Gen5Fixtures.Create(GameVersion.B));
        var pk5 = bw.Boxes[23].Slots[29];
        var result = dex.BuildAll([old, bw], [new(new(Guid.NewGuid(), "W2", "TEST", DateTimeOffset.UtcNow, pk5.StoredData) { FormatKey = "pk5" }, pk5)]);
        Assert.Equal(802, result.Entries.Count); Assert.Equal(2, result.SaveCount);
        Assert.Equal(1, result.Entries[386].BoxCount);
        Assert.Equal(1, result.Entries[648].BoxCount); Assert.Equal(1, result.Entries[648].BankCount);
        Assert.Equal("ブラック", Assert.Single(result.Entries[648].Saves).Game);
        Assert.Single(dex.Filter(result, DexVariant.All, DexOwnership.Owned, "げのせくと"));
    }

    [Fact]
    public void KeepsXYAndDSCollectionTogetherAndExcludesSpeciesFromWrongFormats()
    {
        var xy = new GameSaveReader().Read(PokemonMove.TestSupport.Gen6Fixtures.Create(GameVersion.X));
        var bw = new GameSaveReader().Read(PokemonMove.TestSupport.Gen5Fixtures.Create(GameVersion.B));
        var ss = new GameSaveReader().Read(PokemonMove.TestSupport.Gen4Fixtures.Create(GameVersion.SS));
        var result = dex.BuildAll([xy, bw, ss], []);
        Assert.Equal(3, result.SaveCount); Assert.Equal(802, result.Entries.Count);
        Assert.Equal(1, result.Entries[649].BoxCount); Assert.Equal("X", Assert.Single(result.Entries[649].Saves).Game);
        Assert.Equal(1, result.Entries[648].BoxCount); Assert.Equal(1, result.Entries[386].BoxCount);
        var wrong = xy.Boxes[30].Slots[29] with { FormatKey = "pk5" };
        Assert.Equal(0, dex.Build(null, [Bank(wrong)]).Owned(DexVariant.All));
    }

    [Fact]
    public void OrasAndXYAreRetainedAlongsideDSAndBankInTheGlobalCollection()
    {
        var saves=new[]{GameVersion.X,GameVersion.Y,GameVersion.OR,GameVersion.AS}.Select(v=>new GameSaveReader().Read(PokemonMove.TestSupport.Gen6Fixtures.Create(v))).ToArray();
        var collection=dex.BuildAll(saves,[]);
        Assert.Equal(4,collection.SaveCount);Assert.Equal(802,collection.Entries.Count);
        Assert.Equal(4,collection.Entries[649].BoxCount);
        Assert.Equal(new[]{"X","Y","オメガルビー","アルファサファイア"},collection.Entries[649].Saves.Select(s=>s.Game));
    }

    [Fact]
    public void SunMoonAndPreviousGamesShareAn802SpeciesCollection()
    {
        var sm=new GameSaveReader().Read(PokemonMove.TestSupport.Gen7Fixtures.Create(GameVersion.SN));
        var oras=new GameSaveReader().Read(PokemonMove.TestSupport.Gen6Fixtures.Create(GameVersion.OR));
        var ss=new GameSaveReader().Read(PokemonMove.TestSupport.Gen4Fixtures.Create(GameVersion.SS));
        var result=dex.BuildAll([sm,oras,ss],[]);
        Assert.Equal(3,result.SaveCount);Assert.Equal(802,result.Entries.Count);
        Assert.Equal(1,result.Entries[721].BoxCount);Assert.Equal("サン",Assert.Single(result.Entries[721].Saves).Game);
        Assert.Equal(1,result.Entries[649].BoxCount);Assert.Equal(1,result.Entries[386].BoxCount);
        var wrong=sm.Boxes[31].Slots[29] with {FormatKey="pk6"};Assert.Equal(0,dex.Build(null,[Bank(wrong)]).Owned(DexVariant.All));
    }

}
