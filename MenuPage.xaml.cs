namespace MauiProject;

public partial class MenuPage : ContentPage
{
    public MenuPage() => InitializeComponent();

    private async void NewGame_Clicked(object sender, EventArgs e)
    {
        var name = await DisplayPromptAsync("����� ����", "������� ���:", "OK", "������", "Player");
        if (string.IsNullOrWhiteSpace(name)) return;

        // ������� ��� ����� query-��������
        await Shell.Current.GoToAsync("//game?playerName=" + Uri.EscapeDataString(name.Trim()));
    }

    private async void Rating_Clicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("//rating");

    private void Exit_Clicked(object sender, EventArgs e)
        => Application.Current?.Quit(); // iOS ���������� � ���������
}
