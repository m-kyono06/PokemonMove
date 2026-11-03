namespace PokemonMove.Domain;

public sealed record BankEntry(Guid Id, string SourceGame, string SourceTrainer,
    DateTimeOffset StoredAt, byte[] Data);
public sealed record BankItem(BankEntry Entry, PokemonSummary Pokemon);
public sealed record BankAddResult(BankItem Item, bool Added);
public interface IPokemonCodec
{
    PokemonSummary Decode(byte[] data);
}
public interface IBankStore
{
    Task<IReadOnlyList<BankItem>> ListAsync();
    Task<BankAddResult> AddAsync(PokemonSummary pokemon, string game, string trainer);
    Task<byte[]> ExportAsync();
    Task<int> ImportAsync(byte[] backup);
    Task RestorePreviousAsync();
}
