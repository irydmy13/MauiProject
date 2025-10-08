using System.Collections.ObjectModel;

namespace MauiProject;

public partial class RatingPage : ContentPage
{
    private readonly ObservableCollection<ScoreItem> _items = new();

    public RatingPage()
    {
        InitializeComponent();
        ScoresView.ItemsSource = _items;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _items.Clear();
        foreach (var s in ScoreService.GetScores())
            _items.Add(s);
    }

    private async void Clear_Clicked(object sender, EventArgs e)
    {
        if (await DisplayAlert("��������?", "������� ��� ����������?", "��", "���"))
        {
            ScoreService.Clear();
            _items.Clear();
        }
    }

    private async void GoMenu_Clicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//menu");
    }

}
