using PokemonMove.Domain;

namespace PokemonMove.Infrastructure;

public sealed record OpenedRegisteredSave(ISaveDocument Document, byte[] Data, SaveSnapshot Save);

/// <summary>Reads the live document on every open. Never caches or writes emulator save data.</summary>
public sealed class RegisteredSaveAccess(IRegisteredSaves registry, ISaveDocuments documents, ISaveReader reader)
{
    public Task<OpenedRegisteredSave> OpenAsync(string documentId) => OpenAsync(documents.Open(documentId));
    public async Task<OpenedRegisteredSave> OpenAsync(ISaveDocument document)
    {
        var data = await document.ReadAsync();
        var save = await Task.Run(() => reader.Read(data.ToArray()));
        await registry.RegisterAsync(document.Id, save);
        return new(document, data, save);
    }
}
