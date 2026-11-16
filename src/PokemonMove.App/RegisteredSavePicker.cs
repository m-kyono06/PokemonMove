using PokemonMove.Domain;

namespace PokemonMove.App;

internal static class RegisteredSavePicker
{
    internal static async Task<ISaveDocument?> PickAsync(Page owner, IRegisteredSaves registry, ISaveDocuments documents, string title)
    {
        var saves = (await registry.ListAsync()).OrderByDescending(s => s.LastOpenedAt).ToArray();
        if (saves.Length == 0) return await documents.PickAsync();
        const string add = "新しいセーブを追加…";
        var choices = saves.Select((s, i) => $"{i + 1}: {s.Game} / {s.Trainer}（{s.LastOpenedAt.ToLocalTime():MM/dd HH:mm}）").Append(add).ToArray();
        var choice = await owner.DisplayActionSheetAsync(title, "キャンセル", null, choices);
        if (choice == add) return await documents.PickAsync();
        var index = Array.IndexOf(choices, choice);
        return index >= 0 && index < saves.Length ? documents.Open(saves[index].DocumentId) : null;
    }
}
