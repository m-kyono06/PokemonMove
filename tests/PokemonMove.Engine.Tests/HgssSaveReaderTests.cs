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
        Assert.Empty(result.Warnings);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsDisplayDataWithWarningWhenOnlyExtraBlockIsDamaged(bool dsv)
    {
        var save = CreateSave(GameVersion.SS);
        save.OT = "コトネ";
        save.SetPartySlotAtIndex(new PK4 { Species = 158, CurrentLevel = 10 }, 0);
        var raw = save.Write().ToArray();
        raw[0x27010] ^= 0xFF; // Battle video, outside general/storage blocks.
        Assert.False(new SAV4HGSS(raw.ToArray()).ChecksumsValid);
        var data = raw;
        if (dsv)
        {
            data = new byte[raw.Length + 0x7A];
            raw.CopyTo(data, 0);
            "|-DESMUME SAVE-|"u8.CopyTo(data.AsSpan(data.Length - "|-DESMUME SAVE-|"u8.Length));
        }
        var before = data.ToArray();
        var result = new HgssSaveReader().Read(data);
        Assert.Equal("コトネ", result.TrainerName);
        Assert.Equal((ushort)158, Assert.Single(result.Party).Species);
        Assert.Single(result.Warnings);
        Assert.Equal(before, data);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0xF700, 0)]
    [InlineData(0xF700, 1)]
    public void RejectsCorruptActiveDisplayBlock(int start, int partition)
    {
        var bytes = CreateSave(GameVersion.SS).Write().ToArray();
        var size = start == 0 ? SAV4HGSS.GeneralSize : 0x12310;
        // Select this partition independently for each block, matching PKHeX.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(partition * 0x40000 + start + size - 0x14, 4), 10);
        bytes[partition * 0x40000 + start + 0x100] ^= 0xFF;
        var error = Assert.Throws<InvalidDataException>(() => new HgssSaveReader().Read(bytes));
        Assert.Contains(start == 0 ? "主人公・手持ち" : "ボックス", error.Message);
    }


    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    public void ValidatesIndependentlySelectedGeneralAndStoragePartitions(int general, int storage)
    {
        var bytes = CreateSave(GameVersion.SS).Write().ToArray();
        bytes.AsSpan(0, 0x40000).CopyTo(bytes.AsSpan(0x40000));
        foreach (var (partition, start, size) in new[]
        {
            (general, 0, SAV4HGSS.GeneralSize), (storage, 0xF700, 0x12310)
        })
        {
            var block = bytes.AsSpan(partition * 0x40000 + start, size);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(block[^0x14..], 10);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(block[^2..],
                Checksums.CRC16_CCITT(block[..^0x10]));
        }
        var result = new HgssSaveReader().Read(bytes);
        Assert.Equal("ソウルシルバー", result.Game);
        Assert.Empty(result.Warnings);
    }

}
