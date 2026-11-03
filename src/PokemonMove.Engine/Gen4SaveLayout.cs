using PKHeX.Core;

namespace PokemonMove.Engine;

// Layout and CRC coverage follow PKHeX 26.10.18 SAV4DP / SAV4Pt / SAV4HGSS.
internal sealed record Gen4SaveLayout(int GeneralSize, int StorageStart, int StorageSize, int FooterSize)
{
    public static Gen4SaveLayout For(SAV4 save) => save switch
    {
        SAV4DP => new(SAV4DP.GeneralSize, SAV4DP.GeneralSize, 0x121E0, 0x14),
        SAV4Pt => new(SAV4Pt.GeneralSize, SAV4Pt.GeneralSize, 0x121E4, 0x14),
        SAV4HGSS => new(SAV4HGSS.GeneralSize, 0xF700, 0x12310, 0x10),
        _ => throw new InvalidDataException("D・P・Pt・HGSSの通常セーブを選択してください。"),
    };
    public IEnumerable<(int Start, int Length)> Blocks => [(0, GeneralSize), (StorageStart, StorageSize)];
    public static int ActiveOffset(SAV4 save, int start, int length)
    {
        var footer = start + length - 0x14;
        return start + 0x40000 * SAV4BlockDetection.CompareFooters(save.Data, footer, footer + 0x40000);
    }
}
