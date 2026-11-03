using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.TestSupport;
using Xunit;

namespace PokemonMove.Engine.Tests;

public sealed class Gen5SaveTests
{
    [Theory]
    [InlineData(GameVersion.B, false, "ブラック", "bw")]
    [InlineData(GameVersion.B, true, "ブラック", "bw")]
    [InlineData(GameVersion.W, false, "ホワイト", "bw")]
    [InlineData(GameVersion.W, true, "ホワイト", "bw")]
    [InlineData(GameVersion.B2, false, "ブラック2", "b2w2")]
    [InlineData(GameVersion.B2, true, "ブラック2", "b2w2")]
    [InlineData(GameVersion.W2, false, "ホワイト2", "b2w2")]
    [InlineData(GameVersion.W2, true, "ホワイト2", "b2w2")]
    public void ReadsAllBoxesDetailsAndExplicitPokemonFormatWithoutChangingInput(GameVersion version, bool dsv, string title, string format)
    {
        var data = Gen5Fixtures.Create(version, dsv); var original = data.ToArray();
        var view = new DsSaveReader().Read(data);
        Assert.Equal(title, view.Game); Assert.Equal(format, view.FormatKey); Assert.Equal("テスト", view.TrainerName);
        Assert.Equal(2, view.Party.Count); Assert.Equal(24, view.Boxes.Count); Assert.Equal(24, view.UnlockedBoxCount);
        Assert.All(view.Boxes, b => Assert.Equal(30, b.Slots.Count));
        Assert.Equal("さいご", view.Boxes[23].Name);
        var pokemon = view.Boxes[23].Slots[29]; Assert.Equal("ゲノセクト", pokemon.Name);
        Assert.Equal("pk5", pokemon.FormatKey); Assert.True(pokemon.CanStore);
        var d = Assert.IsType<PokemonDetails>(pokemon.Details);
        Assert.Equal("ひかえめ", d.Nature); Assert.Equal((ushort)42, d.OriginalTrainerId);
        Assert.Equal(1, d.PerfectIVCount); Assert.Equal(510, d.TotalEffortValues);
        Assert.Equal(new PokemonBattleStats(147, 153, 107, 137, 106, 135), d.Stats);
        Assert.Equal(new PokemonBattleStats(6, 252, 0, 0, 0, 252), d.EffortValues);
        Assert.Null(d.CurrentHP); Assert.Equal(original, data);
        Assert.Equal(pokemon.StoredData, new PokemonCodec().Decode(pokemon.StoredData, "pk5").StoredData);
    }

    [Theory]
    [InlineData(GameVersion.B, false)]
    [InlineData(GameVersion.W, true)]
    [InlineData(GameVersion.B2, false)]
    [InlineData(GameVersion.W2, true)]
    public void EditsOnlyChosenBoxAndChecksumTableAndPreservesBackupAndFooter(GameVersion version, bool dsv)
    {
        var data = Gen5Fixtures.Create(version, dsv); var original = data.ToArray();
        var editor = new DsSaveEditor(); var reader = new DsSaveReader();
        var pokemon = reader.Read(data).Boxes[23].Slots[29];
        var removed = editor.Remove(data, new(false, 23, 29));
        Assert.Equal((ushort)0, reader.Read(removed.After).Boxes[23].Slots[29].Species);
        Assert.Equal(pokemon.StoredData, removed.Pokemon.StoredData); Assert.Equal("pk5", removed.Pokemon.FormatKey);
        var table = version is GameVersion.B or GameVersion.W ? 0x23F00 : 0x25F00;
        for (var i = 0; i < original.Length; i++)
            if (!(i >= 0x17400 && i < 0x18400) && !(i >= table && i < table + 0x100)) Assert.Equal(original[i], removed.After[i]);
        var inserted = editor.Insert(removed.After, new(false, 23, 29), pokemon.StoredData, pokemon.FormatKey);
        Assert.Equal(original, inserted.After); Assert.Equal(original, data);
    }

    [Theory]
    [InlineData(GameVersion.B, 0x19410)]
    [InlineData(GameVersion.B, 0x18E20)]
    [InlineData(GameVersion.B, 0x17410)]
    [InlineData(GameVersion.B2, 0x19410)]
    [InlineData(GameVersion.B2, 0x18E20)]
    [InlineData(GameVersion.B2, 0x17410)]
    [InlineData(GameVersion.B2, 0x18410)]
    public void CorruptPrimaryBlocksAreNeverRepairedOrWritten(GameVersion version, int offset)
    {
        var data = Gen5Fixtures.Create(version); data[offset] ^= 1; var before = data.ToArray();
        Assert.Throws<InvalidDataException>(() => new DsSaveReader().Read(data));
        Assert.Throws<InvalidDataException>(() => new DsSaveEditor().Remove(data, new(false, 23, 29)));
        Assert.Equal(before, data);
    }

    [Theory]
    [InlineData(GameVersion.B)]
    [InlineData(GameVersion.W2)]
    public void PartyRemovalCompactsButKeepsOneUsablePokemon(GameVersion version)
    {
        var editor = new DsSaveEditor();
        var result = editor.Remove(Gen5Fixtures.Create(version), new(true, -1, 0));
        Assert.Equal((ushort)496, Assert.Single(new DsSaveReader().Read(result.After).Party).Species);
        Assert.Throws<InvalidDataException>(() => editor.Remove(result.After, new(true, -1, 0)));
    }

    [Theory]
    [InlineData(GameVersion.B, 641, 1)] // Therian formes are B2W2-only.
    [InlineData(GameVersion.B, 645, 1)]
    [InlineData(GameVersion.W, 647, 1)] // Resolute Keldeo.
    [InlineData(GameVersion.B2, 646, 1)] // Fusion partner is stored outside the PK5.
    [InlineData(GameVersion.W2, 646, 2)]
    public void UnsupportedAndFusedFormsAreRejected(GameVersion version, int species, byte form)
    {
        var pk = new PK5 { Species = (ushort)species, Form = form, CurrentLevel = 50 };
        var bytes = new byte[pk.SIZE_STORED]; pk.WriteEncryptedDataStored(bytes);
        var data = Gen5Fixtures.Create(version); var before = data.ToArray();
        Assert.Throws<InvalidDataException>(() => new DsSaveEditor().Insert(data, new(false, 0, 0), bytes, "pk5"));
        Assert.Equal(before, data);
        if (species == 646)
            Assert.Throws<InvalidDataException>(() => new DsSaveEditor().Remove(Gen5Fixtures.Create(version, boxPokemon: pk), new(false, 23, 29)));
    }

    [Fact]
    public void CompatibleTherianFormAndHiddenAbilitySurviveB2W2Transfer()
    {
        var pk = new PK5 { Species = 645, Form = 1, Ability = 22, HiddenAbility = true, CurrentLevel = 50 };
        var bytes = new byte[pk.SIZE_STORED];pk.WriteEncryptedDataStored(bytes);
        var result = new DsSaveEditor().Insert(Gen5Fixtures.Create(GameVersion.B2), new(false, 0, 0), bytes, "pk5");
        Assert.Equal(bytes, new DsSaveReader().Read(result.After).Boxes[0].Slots[0].StoredData);
    }

    [Fact]
    public void LockedBoxesAndUnknownPokemonFormatsAreRejected()
    {
        var data = Gen5Fixtures.Create(GameVersion.B, unlocked: 8);
        var pokemon = new DsSaveReader().Read(data).Boxes[23].Slots[29]; var original = data.ToArray();
        Assert.Throws<InvalidDataException>(() => new DsSaveEditor().Insert(data, new(false, 8, 0), pokemon.StoredData, "pk5"));
        Assert.Throws<InvalidDataException>(() => new DsSaveEditor().Insert(data, new(false, 0, 0), pokemon.StoredData, "unknown"));
        Assert.Throws<InvalidDataException>(() => new PokemonCodec().Decode(pokemon.StoredData, "unknown"));
        Assert.Equal(original, data);
    }
}
