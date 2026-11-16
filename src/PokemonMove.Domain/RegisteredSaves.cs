namespace PokemonMove.Domain;

public sealed record RegisteredSave(string DocumentId, string Game, string Trainer,
    DateTimeOffset RegisteredAt, DateTimeOffset LastOpenedAt)
{
    public string FormatKey { get; init; } = "hgss";
}
public interface IRegisteredSaves
{
    Task<IReadOnlyList<RegisteredSave>> ListAsync();
    Task RegisterAsync(string documentId, SaveSnapshot save);
    Task RebindAsync(string oldDocumentId, string newDocumentId, SaveSnapshot save);
    Task RemoveAsync(string documentId);
    Task<byte[]> ExportAsync();
    Task<int> ImportAsync(byte[] data);
}
public interface ISaveLibraryReader
{
    Task<IReadOnlyList<SaveSnapshot>> ReadAllAsync();
}

public static class RegisteredSaveSelection
{
    // A removed or rebound last selection must not silently switch to a different game.
    public static RegisteredSave? ForAutomaticOpen(IReadOnlyList<RegisteredSave> saves, string? lastDocumentId) =>
        !string.IsNullOrWhiteSpace(lastDocumentId)
            ? saves.FirstOrDefault(s => s.DocumentId == lastDocumentId)
            : saves.OrderByDescending(s => s.LastOpenedAt).ThenBy(s => s.DocumentId, StringComparer.Ordinal).FirstOrDefault();
}
