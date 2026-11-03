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
        builder.Services.AddSingleton<UserOperations>();
        builder.Services.AddSingleton<IPokemonInspection, PokemonInspection>();
        builder.Services.AddSingleton<IDualScreenManager, AndroidDualScreenManager>();
        builder.Services.AddSingleton<ISaveReader, DsSaveReader>();
        builder.Services.AddSingleton<ICollectionDex, CollectionDex>();
        builder.Services.AddSingleton<IPokemonCodec, PokemonCodec>();
        builder.Services.AddSingleton<IBankStore>(services => new LocalBankStore(
            Path.Combine(FileSystem.AppDataDirectory, "bank"), services.GetRequiredService<IPokemonCodec>(), new DsSaveEditor()));
        builder.Services.AddSingleton<ISaveDocuments, AndroidSaveDocuments>();
        builder.Services.AddSingleton<IRegisteredSaves>(_ => new LocalRegisteredSaves(Path.Combine(FileSystem.AppDataDirectory, "saves")));
        builder.Services.AddSingleton<ISaveLibraryReader, RegisteredSaveLibraryReader>();
        builder.Services.AddTransient<MainPage>();
        return builder;
    }
}

public sealed class App(IServiceProvider services) : Application
{
    protected override Window CreateWindow(IActivationState? activationState) => new(new NavigationPage(services.GetRequiredService<MainPage>()) { BarBackgroundColor = Color.FromArgb("152B36"), BarTextColor = Colors.White });
}
