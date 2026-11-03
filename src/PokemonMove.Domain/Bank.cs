namespace PokemonMove.Domain;

public sealed record BankEntry(Guid Id, string SourceGame, string SourceTrainer,
    DateTimeOffset StoredAt, byte[] Data)
{
    public string FormatKey { get; init; } = "pk4";
    public bool TransferEligible { get; init; }
}
public sealed record BankItem(BankEntry Entry, PokemonSummary Pokemon);
public sealed record BankAddResult(BankItem Item, bool Added);
public interface IPokemonCodec
{
    PokemonSummary Decode(byte[] data, string formatKey = "pk4");
}
public interface IBankStore
{
    Task<IReadOnlyList<BankItem>> ListAsync();
    Task<BankAddResult> AddAsync(PokemonSummary pokemon, string game, string trainer);
    Task<byte[]> ExportAsync();
    Task<int> ImportAsync(byte[] backup);
    Task RestorePreviousAsync();
    Task MoveIntoBankAsync(ISaveDocument source, PokemonLocation location, byte[] expectedHash);
    Task MoveToSaveAsync(Guid entryId, ISaveDocument target, PokemonLocation location, byte[] expectedHash, DateOnly? transferDate = null);
    Task RecoverMoveAsync(ISaveDocument document, bool restoreOriginal = false);
    Task<bool> HasPendingMoveAsync();
    Task<SaveBackup> LastSaveBackupAsync();
}
