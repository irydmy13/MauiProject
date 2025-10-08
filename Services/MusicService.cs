using Plugin.Maui.Audio;

namespace MauiProject.Services;

public interface IMusicService
{
    bool IsOn { get; }
    Task InitAsync();
    void Play();
    void Pause();
    void Toggle();
}

public sealed class MusicService : IMusicService
{
    private readonly IAudioManager _audio;
    private IAudioPlayer? _player;

    public bool IsOn => _player?.IsPlaying == true;

    public MusicService(IAudioManager audio)
    {
        _audio = audio;
    }

    public async Task InitAsync()
    {
        if (_player != null) return;

        // menu_bgm.mp3 положи в Resources/Raw/
        using var stream = await FileSystem.OpenAppPackageFileAsync("menu_bgm.mp3");
        _player = _audio.CreatePlayer(stream);
        _player.Loop = true;
        _player.Volume = 0.45; // громкость по умолчанию
    }

    public void Play()
    {
        if (_player == null) return;
        if (!_player.IsPlaying) _player.Play();
    }

    public void Pause()
    {
        if (_player?.IsPlaying == true) _player.Pause();
    }

    public void Toggle()
    {
        if (IsOn) Pause(); else Play();
    }
}
