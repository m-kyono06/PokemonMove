using PokemonMove.Domain;

namespace PokemonMove.App;

public sealed class CollectionPage : ContentPage
{
    private readonly IBankStore bank;
    private readonly ISaveLibraryReader library;
    private readonly ICollectionDex dex;
    private readonly IPokemonInspection inspection;
    private readonly IDualScreenManager dual;
    private readonly UserOperations operations;
    private CollectionDexSnapshot? snapshot;
    private bool loading;
    private bool active;
    private readonly Label summary = new() { TextColor = Colors.White, FontSize = 16 };
    private readonly Label scope = new() { TextColor = Colors.LightGray, FontSize = 12 };
    private readonly ProgressBar progress = new() { ProgressColor = Color.FromArgb("F2CF72") };
    private readonly SearchBar search = new() { Placeholder = "名前・全国図鑑番号で検索", TextColor = Colors.White, PlaceholderColor = Colors.LightGray };
    private readonly Picker variant = new() { Title = "色", TextColor = Colors.White, ItemsSource = new[] { "通常・色違い", "通常色", "色違い" }, SelectedIndex = 0 };
    private readonly Picker ownership = new() { Title = "所持状況", TextColor = Colors.White, ItemsSource = new[] { "すべて", "所持", "未所持" }, SelectedIndex = 0 };
    private readonly Button refresh = new() { Text = "更新", WidthRequest = 72, BackgroundColor = Color.FromArgb("45637B"), TextColor = Colors.White };
    private readonly Label empty = new() { TextColor = Colors.LightGray, Margin = 12 };
    private readonly CollectionView entries = new() { SelectionMode = SelectionMode.Single };

    public CollectionPage(IBankStore bank, ICollectionDex dex, ISaveLibraryReader library, IPokemonInspection inspection, IDualScreenManager dual, UserOperations operations)
    {
        this.bank = bank; this.dex = dex; this.library = library; this.inspection = inspection; this.dual = dual; this.operations = operations;
        Title = "収集図鑑";
        BackgroundColor = Color.FromArgb("253B50");
        entries.ItemTemplate = new DataTemplate(() =>
        {
            var title = new Label { FontSize = 17, TextColor = Colors.White, FontAttributes = FontAttributes.Bold };
            title.SetBinding(Label.TextProperty, nameof(Row.Title));
            var state = new Label { FontSize = 13 };
            state.SetBinding(Label.TextProperty, nameof(Row.State));
            state.SetBinding(Label.TextColorProperty, nameof(Row.Color));
            var sources = new Label { FontSize = 12, TextColor = Colors.LightGray };
            sources.SetBinding(Label.TextProperty, nameof(Row.Sources));
            return RetroTheme.Panel(new VerticalStackLayout { Spacing = 3, Children = { title, state, sources } });
        });
        entries.EmptyView = empty;
        entries.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not Row row) return;
            using var operation = operations.Begin();
            inspection.SelectSpecies(row.Entry, row.Variant);
            if (dual.IsActive) return;
            entries.SelectedItem = null;
            var p = row.Entry;
            await DisplayAlertAsync(row.Title,
                $"現在の所持先\n手持ち：{p.PartyCount}匹（色違い{p.ShinyPartyCount}匹）\nボックス：{p.BoxCount}匹（色違い{p.ShinyBoxCount}匹）\nバンク：{p.BankCount}件（色違い{p.ShinyBankCount}件）\n\nセーブ別\n{string.Join("\n", p.Saves.Select(s => $"#{s.SourceNumber} {s.Game} / {s.Trainer}：手持ち{s.PartyCount}(★{s.ShinyPartyCount})・ボックス{s.BoxCount}(★{s.ShinyBoxCount})"))}\n\nバンクは旧版のコピー保管も含みます。図鑑では同じ種類を1種類として数えます。", "閉じる");
        };
        variant.SelectedIndexChanged += (_, _) => Filter();
        ownership.SelectedIndexChanged += (_, _) => Filter();
        search.TextChanged += (_, _) => Filter();
        refresh.Clicked += async (_, _) => await Refresh();
        var filters = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 12 };
        filters.Add(variant, 0); filters.Add(ownership, 1);
        var heading = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        heading.Add(RetroTheme.Heading("収集図鑑", "NATIONAL No.001–807"), 0);
        heading.Add(refresh, 1);
        var header = new VerticalStackLayout { Padding = 12, Spacing = 6, Children =
        {
            heading, summary, progress, scope, search, filters,
            new Label { Text = "現在の所持を集計。タマゴ・検証できない個体は除外、フォルムは共通です。捕獲履歴とは異なります。", FontSize = 11, TextColor = Colors.LightGray }
        }};
        var layout = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        layout.Add(header, 0, 0); layout.Add(entries, 0, 1);
        Content = layout;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        active = true;
        await Refresh();
    }

    protected override void OnDisappearing() { active = false; base.OnDisappearing(); }

    private async Task Refresh()
    {
        if (loading) return;
        using var operation = operations.Begin();
        loading = true; refresh.IsEnabled = false;
        search.IsEnabled = variant.IsEnabled = ownership.IsEnabled = false;
        empty.Text = "所持状況を集計中…";
        summary.Text = "所持状況を集計中…";
        try
        {
            if (await bank.HasPendingMoveAsync()) throw new InvalidDataException("中断した移動があります。バンク画面で復旧してから図鑑を更新してください。");
            var bankBefore = await Task.Run(bank.ExportAsync);
            var stored = await Task.Run(bank.ListAsync);
            var saves = await library.ReadAllAsync();
            var bankAfter = await Task.Run(bank.ExportAsync);
            if (!bankBefore.AsSpan().SequenceEqual(bankAfter))
                throw new InvalidDataException("集計中にバンクの内容が更新されました。もう一度図鑑を更新してください。");
            var loaded = await Task.Run(() => dex.BuildAll(saves, stored));
            if (!active) return;
            snapshot = loaded;
            scope.Text = saves.Count == 0 ? "集計対象：バンクのみ・旧コピー含む（ホームからセーブを登録できます）" : $"集計対象：登録済み{saves.Count}セーブの手持ち・ボックス ＋ バンク（旧コピー含む）";
            if (saves.Any(s => s.Warnings.Count > 0)) scope.Text += "\n追加領域に警告のあるセーブを含みます。";
            Filter();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            if (!active) return;
            empty.Text = "集計結果を表示できません。";
            snapshot = null; entries.ItemsSource = Array.Empty<Row>();
            summary.Text = "所持状況を集計できませんでした。";
            scope.Text = "セーブ・バンクを確認してから更新してください。未所持としては扱っていません。";
            progress.IsVisible = false;
            await DisplayAlertAsync("集計できませんでした", ex is InvalidDataException ? ex.Message : "セーブのアクセス権限とバンクを確認してください。", "閉じる");
        }
        finally { loading = false; refresh.IsEnabled = true; search.IsEnabled = variant.IsEnabled = ownership.IsEnabled = true; }
    }

    private void Filter()
    {
        if (snapshot is null || variant.SelectedIndex < 0 || ownership.SelectedIndex < 0) return;
        inspection.Clear();
        empty.Text = "該当するポケモンはいません。";
        var kind = (DexVariant)variant.SelectedIndex;
        var state = (DexOwnership)ownership.SelectedIndex;
        var owned = snapshot.Owned(kind);
        summary.Text = $"所持 {owned} / {snapshot.Entries.Count}種類  ({owned / (double)snapshot.Entries.Count:P1})\n全体 {snapshot.Owned(DexVariant.All)}・通常色 {snapshot.Owned(DexVariant.Normal)}・色違い {snapshot.Owned(DexVariant.Shiny)}";
        progress.IsVisible = true; progress.Progress = owned / (double)snapshot.Entries.Count;
        entries.ItemsSource = dex.Filter(snapshot, kind, state, search.Text ?? "").Select(p => new Row(p, kind)).ToArray();
    }

    public sealed record Row(DexEntry Entry, DexVariant Variant)
    {
        public string Title => $"No.{Entry.Species:000}  {Entry.Name}";
        public string State => Entry.Count(Variant) > 0 ? "● 所持" + (Variant == DexVariant.All && Entry.Count(DexVariant.Shiny) > 0 ? " / ★ 色違いあり" : "") : "○ 未所持";
        public Color Color => Microsoft.Maui.Graphics.Color.FromArgb(Entry.Count(Variant) > 0 ? "F2CF72" : "CCD7DC");
        public string Sources => $"手持ち {Entry.Party(Variant)}匹 / ボックス {Entry.Boxes(Variant)}匹 / バンク {Entry.Bank(Variant)}件";
    }
}
