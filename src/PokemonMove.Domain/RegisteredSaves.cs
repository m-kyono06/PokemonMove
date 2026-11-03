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
