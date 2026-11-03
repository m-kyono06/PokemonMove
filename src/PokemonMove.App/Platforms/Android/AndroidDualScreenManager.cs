using Android.App;
using Android.Content;
using Android.Hardware.Display;
using Android.Util;
using Android.Views;
using PokemonMove.Domain;

namespace PokemonMove.App;

public sealed class AndroidDualScreenManager(IBankStore bank, IPokemonInspection selection, UserOperations operations) : Java.Lang.Object, IDualScreenManager, DisplayManager.IDisplayListener
{
    private const string EnabledKey = "dual.enabled";
    private const string ControlsKey = "dual.controls";
    private const string InspectorKey = "dual.inspector";
    private const string RoutedKey = "pokemonmove.routed-display";
    private WeakReference<MainActivity>? main;
    private DisplayManager? displays;
    private bool subscribed;
    private bool selectionSubscribed;
    private bool routing;
    private bool suspended = true;
    private bool blocked;
    private string status = "2画面表示は停止しています。";
    public ScreenRoute? ConfiguredRoute
    {
        get
        {
            var route = new ScreenRoute(Preferences.Default.Get(ControlsKey, -1), Preferences.Default.Get(InspectorKey, -1));
            return DualScreenRouting.IsValid(route, Screens) ? route : null;
        }
    }
    public bool Enabled => Preferences.Default.Get(EnabledKey, false);
    public bool IsActive => Enabled && !blocked && !suspended && PokemonDisplayActivity.Current is { IsFinishing: false, IsVisible: true };
    public string Status => status;
    public IReadOnlyList<ScreenPanel> Screens
    {
        get
        {
            var manager = Android.App.Application.Context.GetSystemService(Context.DisplayService) as DisplayManager;
            return manager?.GetDisplays()?.Where(d => d.IsValid && (d.Flags & DisplayFlags.Private) == 0).Select(d =>
            {
                using var mode = d.GetMode();
                return new ScreenPanel(d.DisplayId, d.Name ?? $"画面 {d.DisplayId}", mode?.PhysicalWidth ?? 0, mode?.PhysicalHeight ?? 0);
            }).OrderBy(d => d.Id).ToArray() ?? [];
        }
    }

    public async Task EnableAsync(int controlsDisplay, int inspectorDisplay)
    {
        if (!DualScreenRouting.IsValid(new(controlsDisplay, inspectorDisplay), Screens))
            throw new IOException("異なる2つの画面を選択してください。画面が見つからない場合は端末の2画面機能を有効にしてください。");
        if (operations.IsBusy) throw new IOException("操作が完了してから画面を切り替えてください。");
        if (await bank.HasPendingMoveAsync()) throw new IOException("先に中断した移動を復旧してください。");
        if (operations.IsBusy) throw new IOException("操作が完了してから画面を切り替えてください。");
        Preferences.Default.Set(ControlsKey, controlsDisplay); Preferences.Default.Set(InspectorKey, inspectorDisplay);
        Preferences.Default.Set(EnabledKey, true); blocked = false;
        if (main is null || !main.TryGetTarget(out var activity) || activity is null) throw new IOException("ホーム画面から設定を開き直してください。");
        suspended = false; Reconcile(activity);
        if (blocked) throw new IOException(status);
    }
    public void Stop()
    {
        Preferences.Default.Set(EnabledKey, false); blocked = false;
        PokemonDisplayActivity.Current?.Finish(); status = "2画面表示を停止しました。操作画面は現在の画面に残ります。";
    }
    internal void Resume(MainActivity activity)
    {
        main = new(activity); suspended = false;
        if (!selectionSubscribed) { selection.Changed += (_, _) => Changed(); operations.BecameIdle += (_, _) => Changed(); selectionSubscribed = true; }
        if (!subscribed)
        {
            displays = Android.App.Application.Context.GetSystemService(Context.DisplayService) as DisplayManager;
            displays?.RegisterDisplayListener(this, null); subscribed = displays is not null;
        }
        Reconcile(activity);
    }
    internal void Suspend(MainActivity activity)
    {
        if (main?.TryGetTarget(out var current) != true || !ReferenceEquals(current, activity) || routing) return;
        suspended = true; PokemonDisplayActivity.Current?.Finish();
    }
    private void Reconcile(MainActivity activity)
    {
        if (!Enabled || blocked || suspended || operations.IsBusy || activity.IsFinishing) return;
        var route = new ScreenRoute(Preferences.Default.Get(ControlsKey, -1), Preferences.Default.Get(InspectorKey, -1));
        if (!DualScreenRouting.IsValid(route, Screens))
        { PokemonDisplayActivity.Current?.Finish(); status = "設定した2つの画面が見つかりません。2画面設定で選び直してください。"; return; }
        var own = DisplayId(activity);
        if (own != route.Controls)
        {
            if (activity.Intent?.GetIntExtra(RoutedKey, -1) == route.Controls)
            { blocked = true; routing = false; Report("端末が操作画面の切り替えを許可しませんでした。通常画面で続行します。端末側の画面切り替えを確認してください。"); return; }
            try
            {
                using var options = ActivityOptions.MakeBasic() ?? throw new IOException("画面を切り替えられません。");
                options.SetLaunchDisplayId(route.Controls);
                var intent = new Intent(activity, typeof(MainActivity));
                intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.MultipleTask);
                intent.PutExtra(RoutedKey, route.Controls);
                routing = true; PokemonDisplayActivity.Current?.Finish();
                activity.StartActivity(intent, options.ToBundle());
                activity.FinishAndRemoveTask();
                status = "操作画面を切り替えています。";
            }
            catch (Exception ex) when (ex is Java.Lang.Exception or IOException)
            { routing = false; blocked = true; status = "操作画面を切り替えられませんでした。端末側の2画面設定を確認してください。"; }
            return;
        }
        routing = false;
        if (PokemonDisplayActivity.Current is { IsFinishing: false } existing)
        {
            if (DisplayId(existing) == route.Inspector && existing.IsVisible) { status = "下画面で操作し、上画面に選択したポケモンを表示しています。"; return; }
            existing.Finish();
        }
        try
        {
            using var options = ActivityOptions.MakeBasic() ?? throw new IOException("表示画面を開けません。");
            options.SetLaunchDisplayId(route.Inspector);
            var intent = new Intent(activity, typeof(PokemonDisplayActivity)); intent.AddFlags(ActivityFlags.NewTask);
            intent.PutExtra("pokemonmove.inspector-display", route.Inspector);
            activity.StartActivity(intent, options.ToBundle());
            status = "ポケモン表示画面を開きました。上下が逆なら設定で入れ替えてください。";
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or IOException)
        { blocked = true; status = "ポケモン表示画面を開けませんでした。端末側の2画面設定を確認してください。"; }
    }
    internal void InspectorRejected()
    { blocked = true; Report("端末が表示画面の切り替えを許可しませんでした。2画面設定を確認し、通常画面で続行してください。"); }
    private void Report(string message)
    {
        status = message;
        Android.Widget.Toast.MakeText(Android.App.Application.Context, message, Android.Widget.ToastLength.Long)?.Show();
    }
    internal static int DisplayId(Activity activity) => OperatingSystem.IsAndroidVersionAtLeast(30)
        ? activity.Display?.DisplayId ?? 0 : activity.WindowManager?.DefaultDisplay?.DisplayId ?? 0;
    public void OnDisplayAdded(int displayId) => Changed();
    public void OnDisplayChanged(int displayId) => Changed();
    public void OnDisplayRemoved(int displayId) => Changed();
    private void Changed()
    {
        if (main?.TryGetTarget(out var activity) == true) activity.RunOnUiThread(() => Reconcile(activity));
    }
}
