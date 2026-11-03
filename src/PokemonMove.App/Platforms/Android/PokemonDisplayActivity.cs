using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Microsoft.Extensions.DependencyInjection;
using PokemonMove.Domain;

namespace PokemonMove.App;

// A separate native, non-interactive activity also works when the inspector is display 0.
// Android Presentation windows cannot be used on every built-in main display.
[Activity(Theme = "@android:style/Theme.Material.NoActionBar", Exported = false,
    LaunchMode = LaunchMode.SingleTask, TaskAffinity = "io.github.mkyono06.pokemonmove.inspector",
    ExcludeFromRecents = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.Density | ConfigChanges.UiMode)]
public sealed class PokemonDisplayActivity : Activity
{
    internal static PokemonDisplayActivity? Current { get; private set; }
    internal bool IsVisible { get; private set; }
    private IPokemonInspection? selection;
    private PokemonDisplayView? view;
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState); Current = this;
        var expected = Intent?.GetIntExtra("pokemonmove.inspector-display", -1) ?? -1;
        if (expected >= 0 && AndroidDualScreenManager.DisplayId(this) != expected)
        {
            (IPlatformApplication.Current?.Services.GetService<IDualScreenManager>() as AndroidDualScreenManager)?.InspectorRejected();
            Finish(); return;
        }
        Window?.AddFlags(WindowManagerFlags.Fullscreen | WindowManagerFlags.KeepScreenOn | WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchable);
        selection = IPlatformApplication.Current?.Services.GetRequiredService<IPokemonInspection>();
        if (selection is null) { Finish(); return; }
        view = new PokemonDisplayView(this); SetContentView(view);
        selection.Changed += Selected;
        view.Update(selection.Current);
    }
    protected override void OnStart() { base.OnStart(); IsVisible = true; }
    protected override void OnStop() { IsVisible = false; base.OnStop(); }
    protected override void OnNewIntent(Intent? intent) { base.OnNewIntent(intent); if (selection is not null) view?.Update(selection.Current); }
    private void Selected(object? sender, EventArgs e) => RunOnUiThread(() =>
    { if (selection is not null && !IsFinishing) view?.Update(selection.Current); });
    protected override void OnDestroy()
    {
        if (selection is not null) selection.Changed -= Selected;
        view?.Dispose(); view = null; selection = null;
        if (ReferenceEquals(Current, this)) Current = null;
        base.OnDestroy();
    }
}
