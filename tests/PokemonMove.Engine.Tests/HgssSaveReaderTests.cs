using PKHeX.Core;
using PokemonMove.Engine;
using Xunit;

namespace PokemonMove.Engine.Tests;

public class HgssSaveReaderTests
{
    // Use full-size memory: PKHeX's parameterless blank Gen4 saves do not
    // allocate the optional extra blocks needed by its checksum writer.
    private static SAV4HGSS CreateSave(GameVersion version)
    {
        var save = new SAV4HGSS(new byte[HgssSaveReader.RawSize]);
        save.Version = version;
        save.Language = (int)LanguageID.Japanese;
        save.Magic = SAV4.MAGIC_JAPAN_INTL;
        InitializeGeneralFooter(save, SAV4HGSS.GeneralSize);
        return save;
    }

    private static void InitializeGeneralFooter(SAV4 save, int size)
    {
        foreach (var partition in new[] { 0, 0x40000 })
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                save.Data.Slice(partition + size - 0xC, 4), size);
    }

    [Theory]
    [InlineData(GameVersion.HG, "ハートゴールド")]
    [InlineData(GameVersion.SS, "ソウルシルバー")]
    public void ReadsTrainerPartyAndAllBoxSlotsWithoutChangingInput(GameVersion version, string title)
    {
        var save = CreateSave(version);
        save.OT = "ヒビキ";
        var pokemon = new PK4 { Species = 152, Nickname = "チコ", CurrentLevel = 12, Move1 = 33, Language = 1 };
        save.SetPartySlotAtIndex(pokemon, 0);
        save.SetBoxSlotAtIndex(pokemon, 17, 29);
        save.SetBoxName(17, "さいご");
        var bytes = save.Write().ToArray();
        var original = bytes.ToArray();
        var result = new HgssSaveReader().Read(bytes);
        Assert.Equal(original, bytes);
        Assert.Equal(title, result.Game);
        Assert.Equal("ヒビキ", result.TrainerName);
        Assert.Equal(18, result.Boxes.Count);
        Assert.All(result.Boxes, box => Assert.Equal(30, box.Slots.Count));
        var party = Assert.Single(result.Party);
        Assert.Equal("チコリータ", party.Name);
        Assert.Equal("チコ", party.Nickname);
        Assert.Equal(12, party.Level);
        Assert.Contains("くさ", party.Type);
        Assert.Contains("たいあたり", party.Moves);
        Assert.Equal((ushort)152, result.Boxes[17].Slots[29].Species);
        Assert.Equal("さいご", result.Boxes[17].Name);
        Assert.Equal((ushort)0, result.Boxes[0].Slots[0].Species);
    }

    [Fact]
    public void ReadsDsvFooterWithoutChangingInput()
    {
        var raw = CreateSave(GameVersion.HG).Write().ToArray();
        var dsv = new byte[raw.Length + 0x7A];
        raw.CopyTo(dsv, 0);
        "|-DESMUME SAVE-|"u8.CopyTo(dsv.AsSpan(dsv.Length - "|-DESMUME SAVE-|"u8.Length));
        var original = dsv.ToArray();
        var result = new HgssSaveReader().Read(dsv);
        Assert.Equal(18, result.Boxes.Count);
        Assert.Equal(original, dsv);
    }

    [Fact]
    public void RejectsAnotherDsGame()
    {
        var save = new SAV4Pt(new byte[HgssSaveReader.RawSize]);
        save.Magic = SAV4.MAGIC_JAPAN_INTL;
        InitializeGeneralFooter(save, SAV4Pt.GeneralSize);
        Assert.Throws<InvalidDataException>(() => new HgssSaveReader().Read(save.Write().ToArray()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(HgssSaveReader.RawSize)]
    [InlineData(HgssSaveReader.MaximumFileSize + 1)]
    public void RejectsInvalidData(int size) =>
        Assert.Throws<InvalidDataException>(() => new HgssSaveReader().Read(new byte[size]));

    [Fact]
    public void RejectsDamagedSave()
    {
        var save = CreateSave(GameVersion.HG);
        var bytes = save.Write().ToArray();
        bytes[0x100] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => new HgssSaveReader().Read(bytes));
    }
}
