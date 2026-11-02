using Microsoft.Extensions.DependencyInjection;
using PokemonMove.Domain;
using PokemonMove.Engine;

namespace PokemonMove.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder()
        .UseMauiApp<App>().ConfigureServices().Build();

    private static MauiAppBuilder ConfigureServices(this MauiAppBuilder builder)
    {
        builder.Services.AddSingleton<ISaveReader, HgssSaveReader>();
        builder.Services.AddTransient<MainPage>();
        return builder;
    }
}

public sealed class App(MainPage page) : Application
{
    protected override Window CreateWindow(IActivationState? activationState) => new(page);
}
