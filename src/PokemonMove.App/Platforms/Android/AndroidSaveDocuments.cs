using Android.Content;
using PokemonMove.Domain;
using PokemonMove.Engine;

namespace PokemonMove.App;

public sealed class AndroidSaveDocuments : ISaveDocuments
{
    public ISaveDocument Open(string documentId)
    {
        var uri = Android.Net.Uri.Parse(documentId);
        if (uri is null || !string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(uri.Authority) || string.IsNullOrWhiteSpace(uri.Path))
            throw new InvalidDataException("セーブの登録情報が不正です。ファイルを選び直してください。");
        return new AndroidSaveDocument(uri);
    }

    public Task<ISaveDocument?> PickAsync() => MainThread.InvokeOnMainThreadAsync(() =>
        (MainActivity.ControlActivity ?? throw new IOException("画面を開き直してください。")).PickSaveAsync());
}

internal sealed class AndroidSaveDocument(Android.Net.Uri uri) : ISaveDocument
{
    public string Id => uri.ToString()!;
    private static ContentResolver Resolver => Android.App.Application.Context.ContentResolver ?? throw new IOException("ファイルを開けません。");
    public async Task<byte[]> ReadAsync()
    {
        try
        {
            await using var stream = Resolver.OpenInputStream(uri) ?? throw new IOException("セーブを読み取れません。");
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer)) != 0)
            {
                if (memory.Length + count > DsSaveReader.MaximumFileSize) throw new InvalidDataException("D・P・Pt・HGSS・BW・BW2の通常セーブを選択してください。");
                memory.Write(buffer, 0, count);
            }
            return memory.ToArray();
        }
        catch (Java.Lang.Exception ex) { throw new IOException("Androidからセーブを読み取れません。保存場所と権限を確認してください。", ex); }
    }
    public async Task WriteAsync(byte[] data)
    {
        try
        {
            await using var stream = Resolver.OpenOutputStream(uri, "rwt") ?? throw new IOException("セーブへの書き込みが許可されていません。");
            await stream.WriteAsync(data);
            await stream.FlushAsync();
        }
        catch (Java.Lang.Exception ex) { throw new IOException("Androidでセーブを書き込めません。中断した移動を復旧してください。", ex); }
    }
}
