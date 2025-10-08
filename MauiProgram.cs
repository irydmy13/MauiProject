using SkiaSharp.Views.Maui.Controls.Hosting;
using Plugin.Maui.Audio;

namespace MauiProject;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseSkiaSharp() // обязательная строка
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Audio DI
        builder.Services.AddSingleton(AudioManager.Current);
        builder.Services.AddSingleton<IMusicService, MusicService>();

        // (опц) страницы через DI, чтобы прокинуть сервис в конструктор
        builder.Services.AddTransient<MainPage>();


        return builder.Build();
    }
}
