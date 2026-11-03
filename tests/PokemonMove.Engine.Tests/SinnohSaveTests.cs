using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.TestSupport;
using Xunit;

namespace PokemonMove.Engine.Tests;

public sealed class SinnohSaveTests
{
    [Theory]
    [InlineData(GameVersion.D, false, "ダイヤモンド・パール", "dp")]
    [InlineData(GameVersion.P, true, "ダイヤモンド・パール", "dp")]
    [InlineData(GameVersion.Pt, false, "プラチナ", "pt")]
    [InlineData(GameVersion.Pt, true, "プラチナ", "pt")]
    public void ReadsTrainerPartyBoxesAndDetailsWithoutChangingInput(GameVersion version, bool dsv, string game, string format)
    {
        var data = Gen4Fixtures.Create(version, dsv);
        var before = data.ToArray();
        var view = new Gen4SaveReader().Read(data);
        Assert.Equal(before, data); Assert.Equal(game, view.Game); Assert.Equal(format, view.FormatKey);
        Assert.Equal("テスト", view.TrainerName); Assert.Empty(view.Warnings);
        Assert.Equal(2, view.Party.Count); Assert.Equal(18, view.Boxes.Count);
        Assert.All(view.Boxes, box => Assert.Equal(30, box.Slots.Count));
        Assert.Equal("さいご", view.Boxes[17].Name);
        var pk = view.Boxes[17].Slots[29];
        Assert.Equal("ナエトル", pk.Name); Assert.Equal("ナエ", pk.Nickname); Assert.True(pk.CanStore);
        var details = Assert.IsType<PokemonDetails>(pk.Details);
        Assert.Equal(1, details.PerfectIVCount); Assert.Equal(12, details.EffortValues.Speed);
        Assert.Equal((ushort)42, details.OriginalTrainerId);
    }

    [Theory]
    [InlineData(GameVersion.D, 0, 1)]
    [InlineData(GameVersion.D, 1, 0)]
    [InlineData(GameVersion.D, 1, 1)]
    [InlineData(GameVersion.Pt, 0, 1)]
    [InlineData(GameVersion.Pt, 1, 0)]
    [InlineData(GameVersion.Pt, 1, 1)]
    public void RoundTripEditsPreserveInactiveBlocksOptionalDataAndDsvFooter(GameVersion version, int general, int storage)
    {
        var data = Gen4Fixtures.Create(version, dsv: true, optionalDamage: true, generalPartition: general, storagePartition: storage);
        var original = data.ToArray(); var reader = new Gen4SaveReader(); var editor = new Gen4SaveEditor();
        var pk = reader.Read(data).Boxes[17].Slots[29];
        var removed = editor.Remove(data, new(false, 17, 29));
        Assert.Equal(original, data); Assert.Equal(pk.StoredData, removed.Pokemon.StoredData);
        var view = reader.Read(removed.After);
        Assert.Equal((ushort)0, view.Boxes[17].Slots[29].Species); Assert.Single(view.Warnings);
        var layout = Gen4Fixtures.Layout(version);
        var gs = general * 0x40000; var ss = storage * 0x40000 + layout.StorageStart;
        for (var i = 0; i < original.Length; i++)
            if (!(i >= gs && i < gs + layout.General) && !(i >= ss && i < ss + layout.StorageSize))
                Assert.Equal(original[i], removed.After[i]);
        var inserted = editor.Insert(removed.After, new(false, 17, 29), removed.Pokemon.StoredData);
        Assert.Equal(pk.StoredData, reader.Read(inserted.After).Boxes[17].Slots[29].StoredData);
        Assert.Equal(original, inserted.After);
    }

    [Theory]
    [InlineData(GameVersion.D, false, 0)]
    [InlineData(GameVersion.D, false, 1)]
    [InlineData(GameVersion.D, true, 0)]
    [InlineData(GameVersion.D, true, 1)]
    [InlineData(GameVersion.Pt, false, 0)]
    [InlineData(GameVersion.Pt, false, 1)]
    [InlineData(GameVersion.Pt, true, 0)]
    [InlineData(GameVersion.Pt, true, 1)]
    public void RejectsDamagedActiveGeneralOrStorageInEitherPartition(GameVersion version, bool storage, int partition)
    {
        var data = Gen4Fixtures.Create(version, generalPartition: partition, storagePartition: partition);
        var start = storage ? Gen4Fixtures.Layout(version).StorageStart : 0;
        data[partition * 0x40000 + start + 0x100] ^= 1;
        var before = data.ToArray();
        Assert.Throws<InvalidDataException>(() => new Gen4SaveReader().Read(data));
        Assert.Throws<InvalidDataException>(() => new Gen4SaveEditor().Remove(data, new(false, 17, 29)));
        Assert.Equal(before, data);
    }

    [Theory]
    [InlineData(GameVersion.D)]
    [InlineData(GameVersion.P)]
    [InlineData(GameVersion.Pt)]
    public void PartyRemovalCompactsAndProtectsLastUsablePokemon(GameVersion version)
    {
        var data = Gen4Fixtures.Create(version); var editor = new Gen4SaveEditor();
        var removed = editor.Remove(data, new(true, -1, 0));
        Assert.Equal((ushort)394, Assert.Single(new Gen4SaveReader().Read(removed.After).Party).Species);
        Assert.Throws<InvalidDataException>(() => editor.Remove(removed.After, new(true, -1, 0)));
    }

    [Theory]
    [InlineData(GameVersion.D, 479, 1, 0)] // Appliance Rotom was added in Pt.
    [InlineData(GameVersion.D, 487, 1, 0)] // Origin Giratina.
    [InlineData(GameVersion.D, 492, 1, 0)] // Sky Shaymin.
    [InlineData(GameVersion.D, 25, 0, 112)] // Griseous Orb.
    [InlineData(GameVersion.D, 172, 1, 0)] // Spiky-eared Pichu is HGSS-only.
    [InlineData(GameVersion.Pt, 172, 1, 0)]
    public void IncompatibleFormsAndItemsAreRejectedWithoutChangingTarget(GameVersion version, int species, byte form, int item)
    {
        var data = Gen4Fixtures.Create(version); var before = data.ToArray();
        var pk = new PK4 { Species = (ushort)species, Form = form, HeldItem = item, CurrentLevel = 20 };
        var bytes = new byte[pk.SIZE_STORED]; pk.WriteEncryptedDataStored(bytes);
        Assert.Throws<InvalidDataException>(() => new Gen4SaveEditor().Insert(data, new(false, 0, 0), bytes));
        Assert.Equal(before, data);
    }

    [Theory]
    [InlineData(GameVersion.D, 201, 27)] // Unown forms exist in DP.
    [InlineData(GameVersion.Pt, 479, 1)]
    [InlineData(GameVersion.Pt, 487, 1)]
    [InlineData(GameVersion.SS, 479, 1)]
    public void CompatibleFormsArePreserved(GameVersion version, int species, byte form)
    {
        var pk = new PK4 { Species = (ushort)species, Form = form, CurrentLevel = 20, PID = 123, HeldItem = species == 487 ? 112 : 0 };
        var bytes = new byte[pk.SIZE_STORED]; pk.WriteEncryptedDataStored(bytes);
        var result = new Gen4SaveEditor().Insert(Gen4Fixtures.Create(version), new(false, 0, 0), bytes);
        Assert.Equal(bytes, new Gen4SaveReader().Read(result.After).Boxes[0].Slots[0].StoredData);
    }
}
