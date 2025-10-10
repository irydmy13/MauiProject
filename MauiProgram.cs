using MauiProject.Services;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Plugin.Maui.Audio;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace MauiProject;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // аудио и наш сервис музыки
        builder.Services.AddSingleton(AudioManager.Current);
        builder.Services.AddSingleton<IMusicService, MusicService>();

        // страницы через DI (меню берём из DI)
        builder.Services.AddTransient<MenuPage>();

        return builder.Build();
    }
}
