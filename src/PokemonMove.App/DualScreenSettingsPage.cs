using PokemonMove.Domain;

namespace PokemonMove.App;

public sealed class DualScreenSettingsPage : ContentPage
{
    private readonly IDualScreenManager manager;
    private IReadOnlyList<ScreenPanel> screens = [];
    private readonly Picker controls = new() { Title = "操作する画面（下）", TextColor = Colors.White };
    private readonly Picker inspector = new() { Title = "画像・情報を表示する画面（上）", TextColor = Colors.White };
    private readonly Label status = new() { TextColor = Colors.LightGray };
    private readonly Button start = Button("2画面モードを開始");
    public DualScreenSettingsPage(IDualScreenManager manager)
    {
        this.manager = manager; Title = "2画面設定"; BackgroundColor = Color.FromArgb("152B36");
        var refresh = Button("画面を再確認"); refresh.Clicked += (_, _) => Refresh();
        var swap = Button("上下を入れ替える"); swap.Clicked += (_, _) =>
        { var previous = controls.SelectedIndex; controls.SelectedIndex = inspector.SelectedIndex; inspector.SelectedIndex = previous; };
        var stop = Button("2画面表示を停止"); stop.Clicked += (_, _) => { manager.Stop(); status.Text = manager.Status; };
        start.Clicked += async (_, _) =>
        {
            if (controls.SelectedIndex < 0 || inspector.SelectedIndex < 0) return;
            start.IsEnabled = false;
            try
            {
                // A successful screen switch creates a fresh control window; no post-switch dialogs.
                await manager.EnableAsync(screens[controls.SelectedIndex].Id, screens[inspector.SelectedIndex].Id);
                status.Text = manager.Status;
            }
            catch (IOException ex) { await DisplayAlertAsync("2画面を開始できません", ex.Message, "閉じる"); }
            finally { start.IsEnabled = screens.Count >= 2; }
        };
        Content = new ScrollView { Content = new VerticalStackLayout { Padding = 16, Spacing = 12, Children =
        {
            new Label { Text = "下画面で操作、上画面でポケモンを見る", TextColor = Colors.White, FontSize = 22 },
            new Label { Text = "RGDS・AYN Thorのような独立した2画面向けです。開始すると操作画面を移し、ホームへ戻ります。上下が逆なら入れ替えて開始してください。セーブやバンクの内容は変更しません。", TextColor = Colors.LightGray },
            status, controls, inspector, swap, start, stop, refresh,
            new Label { Text = "アプリを別のアプリやホームへ切り替えると上画面の表示を閉じ、復帰時に再表示します。1画面しか検出されない場合は、通常画面のまま操作できます。", FontSize = 12, TextColor = Colors.LightGray }
        }}};
    }
    protected override void OnAppearing() { base.OnAppearing(); Refresh(); }
    private void Refresh()
    {
        screens = manager.Screens;
        var labels = screens.Select(s => $"画面{s.Id}: {s.Name} ({s.Width}×{s.Height})").ToArray();
        controls.ItemsSource = labels; inspector.ItemsSource = labels;
        var route = manager.ConfiguredRoute ?? DualScreenRouting.Default(screens);
        if (route is not null)
        {
            controls.SelectedIndex = screens.ToList().FindIndex(s => s.Id == route.Controls);
            inspector.SelectedIndex = screens.ToList().FindIndex(s => s.Id == route.Inspector);
        }
        start.IsEnabled = route is not null;
        status.Text = $"検出した画面：{screens.Count}\n{manager.Status}";
    }
    private static Button Button(string text) => new() { Text = text, TextColor = Colors.White, BackgroundColor = Color.FromArgb("294D59") };
}
