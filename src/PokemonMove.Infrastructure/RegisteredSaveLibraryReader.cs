using PokemonMove.Domain;

namespace PokemonMove.Infrastructure;

public sealed class RegisteredSaveLibraryReader(IRegisteredSaves registry, ISaveDocuments documents, ISaveReader reader) : ISaveLibraryReader
{
    public async Task<IReadOnlyList<SaveSnapshot>> ReadAllAsync()
    {
        var registered = await registry.ListAsync();
        var saves = new List<SaveSnapshot>();
        foreach (var item in registered)
        {
            try
            {
                var data = await documents.Open(item.DocumentId).ReadAsync();
                saves.Add(await Task.Run(() => reader.Read(data)));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                throw new InvalidDataException($"登録済みの {item.Game} / {item.Trainer} を読み込めません。登録セーブの管理から接続し直すか、集計対象から外してください。ほかの登録は保持されています。", ex);
            }
        }
        return saves;
    }
}
