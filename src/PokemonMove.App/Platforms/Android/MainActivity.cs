using Android.App;
using Android.Content;
using Android.Content.PM;
using PokemonMove.Domain;

namespace PokemonMove.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int SaveRequest = 9017;
    private TaskCompletionSource<ISaveDocument?>? saveRequest;

    // OpenDocument selects the actual writable document, not MAUI FilePicker's possible cached copy.
    public Task<ISaveDocument?> PickSaveAsync()
    {
        if (saveRequest is not null) throw new IOException("ファイル選択がすでに開いています。");
        var completion = new TaskCompletionSource<ISaveDocument?>(TaskCreationOptions.RunContinuationsAsynchronously);
        saveRequest = completion;
        try
        {
            var intent = new Intent(Intent.ActionOpenDocument);
            intent.SetType("*/*");
            intent.PutExtra(Intent.ExtraLocalOnly, true);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantPersistableUriPermission);
            StartActivityForResult(intent, SaveRequest);
        }
        catch (Exception ex) { saveRequest = null; completion.SetException(ex); }
        return completion.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != SaveRequest || saveRequest is not { } completion) return;
        saveRequest = null;
        if (resultCode != Result.Ok || data?.Data is not { } uri) { completion.TrySetResult(null); return; }
        try
        {
            var permissions = data.Flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
            if ((permissions & ActivityFlags.GrantWriteUriPermission) == 0) throw new UnauthorizedAccessException("書き込みできるセーブを選択してください。");
            ContentResolver!.TakePersistableUriPermission(uri, permissions);
            completion.TrySetResult(new AndroidSaveDocument(uri));
        }
        catch (Exception ex) { completion.TrySetException(new IOException("セーブの読み書き権限を取得できませんでした。別の保存場所から選択してください。", ex)); }
    }
}
