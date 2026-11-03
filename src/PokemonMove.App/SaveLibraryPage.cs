using PokemonMove.Domain;
using PokemonMove.Infrastructure;

namespace PokemonMove.App;

public sealed class SaveLibraryPage : ContentPage
{
    private readonly IRegisteredSaves registry;
    private readonly ISaveDocuments documents;
    private readonly ISaveReader reader;
    private readonly UserOperations operations;
    private bool busy;
    private readonly Label status = new() { TextColor = Colors.White };
    private readonly Button add = Button("セーブを追加");
    private readonly Button export = Button("一覧を書き出す");
    private readonly Button import = Button("一覧を取り込む");
    private readonly CollectionView saves = new() { SelectionMode = SelectionMode.Single };

    public SaveLibraryPage(IRegisteredSaves registry, ISaveDocuments documents, ISaveReader reader, UserOperations operations)
    {
        this.registry = registry; this.documents = documents; this.reader = reader; this.operations = operations;
        Title = "登録セーブの管理"; BackgroundColor = Color.FromArgb("152B36");
        saves.ItemTemplate = new DataTemplate(() =>
        {
            var title = new Label { FontSize = 17, TextColor = Colors.White };
            title.SetBinding(Label.TextProperty, nameof(Row.Title));
            var date = new Label { FontSize = 12, TextColor = Colors.LightGray };
            date.SetBinding(Label.TextProperty, nameof(Row.Detail));
            return new VerticalStackLayout { Padding = 12, Children = { title, date } };
        });
        saves.EmptyView = new Label { Text = "セーブを開くか追加すると、図鑑の集計対象として登録されます。", TextColor = Colors.LightGray, Margin = 16 };
        saves.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not Row row) return;
            saves.SelectedItem = null;
            await Run(async () =>
            {
                var action = await DisplayActionSheetAsync(row.Title, "キャンセル", null, "接続し直す", "集計対象から外す");
                if (action == "接続し直す")
                {
                    var file = await documents.PickAsync(); if (file is null) return;
                    var view = await Read(file);
                    if (!await DisplayAlertAsync("接続を更新", $"{row.Title} の登録を、選択した {view.Game} / {view.TrainerName} に接続し直します。", "更新する", "キャンセル")) return;
                    await registry.RebindAsync(row.Save.DocumentId, file.Id, view);
                }
                else if (action == "集計対象から外す")
                {
                    if (!await DisplayAlertAsync("登録解除", $"{row.Title} を図鑑の集計対象から外します。セーブファイルやバンクは削除しません。", "解除する", "キャンセル")) return;
                    await registry.RemoveAsync(row.Save.DocumentId);
                }
                await Refresh();
            });
        };
        add.Clicked += async (_, _) => await Run(async () =>
        {
            var file = await documents.PickAsync(); if (file is null) return;
            await registry.RegisterAsync(file.Id, await Read(file)); await Refresh();
        });
        export.Clicked += async (_, _) => await Run(async () =>
        {
            var file = Path.Combine(FileSystem.CacheDirectory, $"PokemonMove-saves-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            await File.WriteAllBytesAsync(file, await registry.ExportAsync());
            await Share.Default.RequestAsync(new ShareFileRequest { Title = "登録セーブ一覧を保存", File = new ShareFile(file, "application/json") });
        });
        import.Clicked += async (_, _) => await Run(async () =>
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "登録セーブ一覧を選択" }); if (file is null) return;
            await using var stream = await file.OpenReadAsync(); using var memory = new MemoryStream();
            var buffer = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(buffer)) != 0)
            {
                if (memory.Length + count > LocalRegisteredSaves.MaximumBytes) throw new InvalidDataException("登録一覧が大きすぎます。");
                memory.Write(buffer, 0, count);
            }
            var added = await registry.ImportAsync(memory.ToArray()); await Refresh();
            await DisplayAlertAsync("取り込み完了", $"{added}件を追加しました。読み取り権限がない場合は、登録をタップして接続し直してください。", "閉じる");
        });
        var backups = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 8 };
        backups.Add(export, 0); backups.Add(import, 1);
        var header = new VerticalStackLayout { Padding = 12, Spacing = 8, Children =
        {
            status, add,
            new Label { Text = "登録したセーブすべて＋バンクを図鑑で集計します。登録をタップすると再接続・解除できます。現在の読込対応はD・P・Pt・HGSSです。", FontSize = 12, TextColor = Colors.LightGray }
        }};
        var footer = new VerticalStackLayout { Padding = 12, Spacing = 8, Children =
        {
            backups,
            new Label { Text = "アプリ削除前にバンクと登録一覧をそれぞれ書き出してください。一覧にセーブ本体は含みません。再インストール・端末変更後は接続し直してください。", FontSize = 11, TextColor = Colors.LightGray }
        }};
        var layout = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        layout.Add(header, 0, 0); layout.Add(saves, 0, 1); layout.Add(footer, 0, 2); Content = layout;
    }

    protected override async void OnAppearing() { base.OnAppearing(); await Run(Refresh); }
    private async Task<SaveSnapshot> Read(ISaveDocument file)
    { var data = await file.ReadAsync(); return await Task.Run(() => reader.Read(data)); }
    private async Task Refresh()
    {
        var registered = await registry.ListAsync();
        saves.ItemsSource = registered.Select(s => new Row(s)).ToArray();
        status.Text = $"図鑑の集計対象：{registered.Count}セーブ";
    }
    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        using var operation = operations.Begin();
        busy = true; add.IsEnabled = export.IsEnabled = import.IsEnabled = saves.IsEnabled = false;
        try { await action(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        { status.Text = "登録一覧を処理できませんでした。"; await DisplayAlertAsync("登録できませんでした", ex is InvalidDataException ? ex.Message : "ファイルの保存場所と権限を確認してください。", "閉じる"); }
        finally { busy = false; add.IsEnabled = export.IsEnabled = import.IsEnabled = saves.IsEnabled = true; }
    }
    private static Button Button(string text) => new() { Text = text, BackgroundColor = Color.FromArgb("294D59"), TextColor = Colors.White };
    public sealed record Row(RegisteredSave Save)
    {
        public string Title => $"{Save.Game} / {Save.Trainer}";
        public string Detail => $"最終登録：{Save.LastOpenedAt.ToLocalTime():yyyy/MM/dd HH:mm}（タップで管理）";
    }
}
