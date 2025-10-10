using MauiProject.Services;

namespace MauiProject;

public partial class MenuPage : ContentPage
{
    private readonly IMusicService _music;

    public MenuPage(IMusicService music)
    {
        InitializeComponent();
        _music = music;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _music.InitAsync();
        _music.Play();
        UpdateMusicIcon();
    }

    private void UpdateMusicIcon()
        => BtnMusic.Source = _music.IsOn ? "music_on.png" : "music_off.png";

    private void BtnMusic_Clicked(object sender, EventArgs e)
    {
        _music.Toggle();
        UpdateMusicIcon();
    }

    private async void NewGame_Clicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("//game");

    private async void Rating_Clicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("//rating");

    private void Exit_Clicked(object sender, EventArgs e)
    {
#if ANDROID
        Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
#else
        Application.Current?.Quit();
#endif
    }
}
