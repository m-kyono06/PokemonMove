namespace PokemonMove.Domain;

public enum DexVariant { All, Normal, Shiny }
public enum DexOwnership { All, Owned, Missing }
public sealed record DexEntry(ushort Species, string Name, int PartyCount, int BoxCount, int BankCount,
    int ShinyPartyCount, int ShinyBoxCount, int ShinyBankCount)
{
    public int Count(DexVariant variant) => CountAt(PartyCount + BoxCount + BankCount,
        ShinyPartyCount + ShinyBoxCount + ShinyBankCount, variant);
    public int Party(DexVariant variant) => CountAt(PartyCount, ShinyPartyCount, variant);
    public int Boxes(DexVariant variant) => CountAt(BoxCount, ShinyBoxCount, variant);
    public int Bank(DexVariant variant) => CountAt(BankCount, ShinyBankCount, variant);
    private static int CountAt(int total, int shiny, DexVariant variant) => variant switch
    {
        DexVariant.Shiny => shiny, DexVariant.Normal => total - shiny, _ => total
    };
}
public sealed record CollectionDexSnapshot(IReadOnlyList<DexEntry> Entries, bool IncludesSave)
{
    public int Owned(DexVariant variant) => Entries.Count(e => e.Count(variant) > 0);
}
public interface ICollectionDex
{
    CollectionDexSnapshot Build(SaveSnapshot? save, IReadOnlyList<BankItem> bank);
    IReadOnlyList<DexEntry> Filter(CollectionDexSnapshot dex, DexVariant variant, DexOwnership ownership, string query);
}
