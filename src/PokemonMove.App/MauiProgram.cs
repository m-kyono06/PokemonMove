using Microsoft.Extensions.DependencyInjection;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.Infrastructure;

namespace PokemonMove.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder()
        .UseMauiApp<App>().ConfigureServices().Build();

    private static MauiAppBuilder ConfigureServices(this MauiAppBuilder builder)
    {
        builder.Services.AddSingleton<ISaveReader, HgssSaveReader>();
        builder.Services.AddSingleton<IPokemonCodec, HgssPokemonCodec>();
        builder.Services.AddSingleton<IBankStore>(services => new LocalBankStore(
            Path.Combine(FileSystem.AppDataDirectory, "bank"), services.GetRequiredService<IPokemonCodec>(), new HgssSaveEditor()));
        builder.Services.AddSingleton<ISaveDocuments, AndroidSaveDocuments>();
        builder.Services.AddTransient<MainPage>();
        return builder;
    }
}

public sealed class App(MainPage page) : Application
{
    protected override Window CreateWindow(IActivationState? activationState) => new(new NavigationPage(page) { BarBackgroundColor = Color.FromArgb("152B36"), BarTextColor = Colors.White });
}
