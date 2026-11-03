namespace PokemonMove.Domain;

public sealed record PokemonLocation(bool IsParty, int Box, int Slot);
public sealed record SaveEdit(byte[] After, PokemonSummary Pokemon, string Game, string Trainer);
public sealed record SaveBackup(string Name, byte[] Data);
public interface ISaveDocument
{
    string Id { get; }
    Task<byte[]> ReadAsync();
    Task WriteAsync(byte[] data);
}
public interface ISaveDocuments
{
    Task<ISaveDocument?> PickAsync();
}
public interface ISaveEditor
{
    SaveEdit Remove(byte[] source, PokemonLocation location);
    SaveEdit Insert(byte[] source, PokemonLocation location, byte[] pokemon);
}
public sealed class SaveRecoveryRequiredException(string message) : IOException(message);
