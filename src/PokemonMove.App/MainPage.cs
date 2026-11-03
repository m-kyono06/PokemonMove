using PokemonMove.Domain;
using PokemonMove.Engine;
using System.Security.Cryptography;

namespace PokemonMove.App;

public sealed class MainPage : ContentPage
{
    private readonly ISaveReader reader;
    private SaveSnapshot? snapshot;
    private readonly IBankStore bank;
    private bool storing;
    private bool loading;
    private readonly ISaveDocuments documents;
    private readonly ICollectionDex dex;
    private readonly IRegisteredSaves registry;
    private readonly ISaveLibraryReader library;
    private ISaveDocument? sourceDocument;
    private byte[] sourceHash = [];
    private readonly Label status = new() { Text = "エミュレータを終了し、実際に使っているHGSSのセーブを選んでください。選択したファイルを書き換えます。", TextColor = Color.FromArgb("AFC2CD") };
    private readonly Picker location = new() { Title = "手持ち / ボックス", TextColor = Colors.White, BackgroundColor = Color.FromArgb("233E4B") };
    private readonly Grid slots = new() { ColumnSpacing = 5, RowSpacing = 5 };
    private readonly Button open = new() { Text = "セーブファイルを開く", BackgroundColor = Color.FromArgb("61DFC0"), TextColor = Color.FromArgb("152B36"), CornerRadius = 14 };

    public MainPage(ISaveReader reader, IBankStore bank, ISaveDocuments documents, ICollectionDex dex, IRegisteredSaves registry, ISaveLibraryReader library)
    {
        this.reader = reader;
        this.bank = bank;
        this.documents = documents;
        this.dex = dex;
        this.registry = registry;
        this.library = library;
        Title = "PokemonMove";
        BackgroundColor = Color.FromArgb("152B36");
        for (var i = 0; i < 6; i++) slots.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < 5; i++) slots.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        open.Clicked += OpenSave;
        location.SelectedIndexChanged += (_, _) => RenderSlots();
        location.IsVisible = false;
        var bankButton = new Button { Text = "ローカルバンクを開く", BackgroundColor = Color.FromArgb("294D59"), TextColor = Colors.White, CornerRadius = 14 };
        bankButton.Clicked += async (_, _) => await Navigation.PushAsync(new BankPage(bank, documents, reader, registry));
        var dexButton = new Button { Text = "収集図鑑を開く", BackgroundColor = Color.FromArgb("294D59"), TextColor = Colors.White, CornerRadius = 14 };
        dexButton.Clicked += async (_, _) =>
        {
            if (loading || storing) return;
            await Navigation.PushAsync(new CollectionPage(bank, dex, library));
        };
        var libraryButton = new Button { Text = "登録セーブを管理", BackgroundColor = Color.FromArgb("294D59"), TextColor = Colors.White, CornerRadius = 14 };
        libraryButton.Clicked += async (_, _) =>
        {
            if (loading || storing) return;
            await Navigation.PushAsync(new SaveLibraryPage(registry, documents, reader));
        };
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(18, 30), Spacing = 20,
                Children =
                {
                    new Label { Text = "POKEMON MOVE", FontSize = 13, CharacterSpacing = 3, TextColor = Color.FromArgb("61DFC0") },
                    new Label { Text = "冒険の仲間を、ひとつに。", FontSize = 26, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                    new Label { Text = "ハートゴールド / ソウルシルバー", TextColor = Color.FromArgb("AFC2CD") },
                    open, bankButton, dexButton, libraryButton, status, location, slots,
                    new Label { Text = "移動前にエミュレータを終了してください。選択したセーブを書き換えます。別のコピーを選ぶと、エミュレータ側の元データは変わりません。\n\nmelonDS：通常の .sav\nDraStic：バッテリーセーブの .dsv\nステートセーブには対応していません。\n\n保管時は元の枠から取り除き、引き出し時はバンクから取り除きます。バックアップはバンク画面から書き出せます。", FontSize = 13, TextColor = Color.FromArgb("AFC2CD") }
                }
            }
        };
    }

    private async void OpenSave(object? sender, EventArgs e)
    {
        if (loading || storing) return;
        loading = true;
        open.IsEnabled = false;
        slots.IsEnabled = false;
        try
        {
            var file = await documents.PickAsync();
            if (file is null) return;
            status.Text = "セーブを読み込み中…";
            var data = await file.ReadAsync();
            var loaded = await Task.Run(() => reader.Read(data));
            await registry.RegisterAsync(file.Id, loaded);
            sourceDocument = file;
            sourceHash = SHA256.HashData(data);
            snapshot = loaded;
            location.Items.Clear();
            location.Items.Add($"手持ち（{loaded.Party.Count}匹）");
            foreach (var box in loaded.Boxes) location.Items.Add(box.Name);
            location.IsVisible = true;
            location.SelectedIndex = 0;
            status.Text = $"{loaded.Game}\nトレーナー：{loaded.TrainerName}" +
                (loaded.Warnings.Count == 0 ? "" : "\n\n注意：" + string.Join("\n", loaded.Warnings));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            status.Text = snapshot is null ? "セーブを開けませんでした。別のファイルをお試しください。" : $"{snapshot.Game}\nトレーナー：{snapshot.TrainerName}（前のセーブを表示中）";
            await DisplayAlertAsync("読み込みできませんでした", ex is InvalidDataException ? ex.Message : "ファイルの内容またはアクセス権限を確認してください。", "閉じる");
        }
        finally { loading = false; open.IsEnabled = true; slots.IsEnabled = true; }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (sourceDocument is null || storing || loading) return;
        loading = true;
        open.IsEnabled = false;
        slots.IsEnabled = false;
        try
        {
            var data = await sourceDocument.ReadAsync();
            snapshot = await Task.Run(() => reader.Read(data));
            sourceHash = SHA256.HashData(data);
            status.Text = $"{snapshot.Game}\nトレーナー：{snapshot.TrainerName}" + (snapshot.Warnings.Count == 0 ? "" : "\n" + string.Join("\n", snapshot.Warnings));
            if (location.Items.Count > 0) location.Items[0] = $"手持ち（{snapshot.Party.Count}匹）";
            RenderSlots();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        { status.Text = "セーブを再読み込みできません。開き直すか、中断した移動をバンク画面で復旧してください。"; }
        finally { loading = false; open.IsEnabled = true; slots.IsEnabled = true; }
    }

    private void RenderSlots()
    {
        slots.Children.Clear();
        if (snapshot is null || location.SelectedIndex < 0) return;
        var selectedLocation = location.SelectedIndex;
        var pokemon = selectedLocation == 0 ? snapshot.Party : snapshot.Boxes[selectedLocation - 1].Slots;
        foreach (var p in pokemon)
        {
            var button = new Button
            {
                Text = p.Label, FontSize = 10, Padding = new Thickness(1), HeightRequest = 68,
                BackgroundColor = Color.FromArgb(p.Species == 0 ? "1C333E" : "294D59"), TextColor = Colors.White,
                CornerRadius = 8, IsEnabled = p.Species != 0,
            };
            button.Clicked += async (_, _) => await ShowPokemon(p, new PokemonLocation(selectedLocation == 0, selectedLocation - 1, p.Slot));
            slots.Add(button, p.Slot % 6, p.Slot / 6);
        }
    }

    private async Task ShowPokemon(PokemonSummary pokemon, PokemonLocation position)
    {
        if (storing || loading || snapshot is null || sourceDocument is null) return;
        storing = true;
        open.IsEnabled = false;
        slots.IsEnabled = false;
        try
        {
            var detail = BankPage.Detail(pokemon);
            if (!pokemon.CanStore)
            {
                await DisplayAlertAsync(pokemon.Name, detail + "\n\nデータ検証に失敗したため保管できません。", "閉じる");
                return;
            }
            if (!await DisplayAlertAsync(pokemon.Name, detail + "\n\nエミュレータを終了してください。バックアップ後、選択したセーブから取り除いてバンクへ移動します。", "バンクへ移動", "閉じる")) return;
            await bank.MoveIntoBankAsync(sourceDocument, position, sourceHash);
            var updated = await sourceDocument.ReadAsync();
            snapshot = await Task.Run(() => reader.Read(updated));
            sourceHash = SHA256.HashData(updated);
            status.Text = $"{snapshot.Game}\nトレーナー：{snapshot.TrainerName}\n移動後のセーブを表示中";
            if (location.Items.Count > 0) location.Items[0] = $"手持ち（{snapshot.Party.Count}匹）";
            RenderSlots();
            await DisplayAlertAsync("移動完了", "セーブから取り除き、バンクに保管しました。移動前のセーブはバックアップされています。", "閉じる");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            await DisplayAlertAsync("保管できませんでした", ex is InvalidDataException ? ex.Message : "保存先へのアクセスを確認してください。中断した可能性があります。バンク画面で復旧を確認してください。", "閉じる");
        }
        finally { storing = false; open.IsEnabled = true; slots.IsEnabled = true; }
    }

}
