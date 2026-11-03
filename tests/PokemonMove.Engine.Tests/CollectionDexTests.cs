using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using Xunit;

namespace PokemonMove.Engine.Tests;

public sealed class CollectionDexTests
{
    private readonly HgssCollectionDex dex = new();
    private static PokemonSummary Pokemon(ushort species, bool shiny = false, bool egg = false)
    {
        var pk = new PK4 { Species = species, CurrentLevel = 12, ID32 = 0, PID = shiny ? 0u : 0x100000u, IsEgg = egg };
        var data = new byte[pk.SIZE_STORED]; pk.WriteEncryptedDataStored(data);
        return new HgssPokemonCodec().Decode(data);
    }
    private static BankItem Bank(PokemonSummary p) => new(new(Guid.NewGuid(), "SS", "TEST", DateTimeOffset.UtcNow, p.StoredData), p);
    private static SaveSnapshot Save(PokemonSummary[] party, params PokemonSummary[] box) => new("TEST", "SS", party, [new("BOX", box)]);

    [Fact]
    public void EmptyBankOnlyStillListsEveryNationalSpeciesAsMissing()
    {
        var result = dex.Build(null, []);
        Assert.False(result.IncludesSave);
        Assert.Equal(493, result.Entries.Count);
        Assert.Equal((ushort)1, result.Entries[0].Species);
        Assert.Equal("フシギダネ", result.Entries[0].Name);
        Assert.Equal((ushort)493, result.Entries[^1].Species);
        Assert.Equal("アルセウス", result.Entries[^1].Name);
        Assert.Equal(0, result.Owned(DexVariant.All));
        Assert.Equal(493, dex.Filter(result, DexVariant.All, DexOwnership.Missing, "").Count);
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
        Assert.Equal(492, dex.Filter(result, DexVariant.Normal, DexOwnership.Missing, "").Count);
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
        Assert.Equal(1, result.Entries[^1].BankCount);
    }

    [Theory]
    [InlineData("ちこ", 152)]
    [InlineData("チコ", 152)]
    [InlineData("ﾁｺ", 152)]
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
        Assert.Empty(dex.Filter(result, DexVariant.All, DexOwnership.All, "494"));
        Assert.Empty(dex.Filter(result, DexVariant.All, DexOwnership.All, "見つからない名前"));
        Assert.Equal(1, result.Owned(DexVariant.All));
        Assert.Equal(original, p.StoredData);
    }
}
