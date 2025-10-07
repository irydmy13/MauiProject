using SkiaSharp;
using System.Diagnostics;

namespace MauiProject;

[QueryProperty(nameof(PlayerName), "playerName")]
public partial class GamePage : ContentPage
{
    // модели
    public string PlayerName { get; set; } = "Player";
    private Player _player = new("Player");

    // таймер
    private readonly Stopwatch _sw = new();
    private bool _timerRunning;

    // уровень/сложность
    private readonly (int rows, int cols)[] _difficulty =
    {
        (2,2),(2,3),(3,3),(3,4),(4,4),
        (4,5),(5,5),(5,6),(6,6),(6,7)
    };

    // пул доступных картинок (обнови под свои имена)
    private readonly string[] _imagePool = new[]
    {
        "p1.png","p2.png","p3.png","p4.png","p5.png",
        "p6.png","p7.png","p8.png","p9.png","p10.png"
    };
    private string[] _levelImages = Array.Empty<string>();

    // вопросы
    private readonly List<Question> _questions = new()
    {
        new("Сколько будет 2 + 2 * 2?", "6", "8", 'A'),
        new("Ключевое слово для наследования в C#?", "extends", ":", 'B'),
        new("Тип для целых чисел?", "int", "string", 'A'),
        new("Какой модификатор делает поле доступным только внутри класса?", "private", "public", 'A'),
        new("Коллекция с уникальными ключами?", "List<T>", "Dictionary<TKey,TValue>", 'B'),
        new("Как вывести в консоль?", "Console.WriteLine()", "System.Print()", 'A'),
        new("Индекс первого элемента массива?", "0", "1", 'A'),
        new("Оператор равенства в C#?", "==", "=", 'A'),
        new("Исключение ловим блоком…", "catch", "error", 'A'),
        new("Интерфейс объявляется словом…", "class", "interface", 'B'),
        new("Лямбда начинается с…", "=>", "->", 'A'),
        new("Асинх. метод помечают…", "sync", "async", 'B')
    };
    private readonly Random _rnd = new();

    // состояние пазла
    Grid _sourceGrid, _targetGrid;
    readonly Dictionary<string, Image> _pieces = new();
    readonly Dictionary<(int r, int c), string> _correct = new();
    Image? _selectedPiece;
    int _levelIndex = 0; // 0..9

    public GamePage()
    {
        InitializeComponent();

        _sourceGrid = SourceGrid;
        _targetGrid = TargetGrid;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Инициализация новой игры при входе
        _player = new Player(PlayerName);
        StartNewRun();
    }

    // ====== НОВАЯ ИГРА ======
    private void StartNewRun()
    {
        // перемешаем изображения на 10 уровней
        _levelImages = _imagePool.OrderBy(_ => _rnd.Next()).Take(10).ToArray();
        _levelIndex = 0;

        _sw.Reset();
        _sw.Start();
        _timerRunning = true;
        Device.StartTimer(TimeSpan.FromSeconds(1), () =>
        {
            if (!_timerRunning) return false;
            LblTime.Text = $"{(int)_sw.Elapsed.TotalMinutes:00}:{_sw.Elapsed.Seconds:00}";
            return true;
        });

        LoadLevel(_levelIndex);
    }

    // ====== ЗАГРУЗКА УРОВНЯ ======
    private async void LoadLevel(int index)
    {
        var (rows, cols) = _difficulty[index];
        var imgName = _levelImages[index];
        LblStatus.Text = $"Игрок: {_player.Name}, Уровень {index + 1}/10  ({rows}×{cols})";

        BuildGrids(rows, cols);

        _pieces.Clear();
        _correct.Clear();
        _selectedPiece = null;
        QuestionPanel.IsVisible = false;
        BtnNext.IsVisible = false;

        // превью (из Images)
        ImgPreview.Source = imgName;

        // поток для Skia (из raw)
        using var stream = await FileSystem.OpenAppPackageFileAsync(imgName); // файл должен лежать и в Resources/raw
        var parts = SplitImageFromStream(stream, rows, cols);

        int k = 0;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var id = $"piece_{r + 1}_{c + 1}";
                var img = new Image
                {
                    Source = parts[k++],
                    WidthRequest = 96,
                    HeightRequest = 96,
                    AutomationId = id
                };
                AddPieceGestures(img, id);
                _pieces[id] = img;
                _correct[(r, c)] = id;
            }

        // разложим источники случайно
        var shuffled = _pieces.Values.OrderBy(_ => _rnd.Next()).ToList();
        int t = 0;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                _sourceGrid.Add(shuffled[t++], c, r);

        // подготовим цели
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var target = new Border
                {
                    BackgroundColor = Color.FromArgb("#ECEFF1"),
                    Stroke = Colors.Black,
                    StrokeThickness = 1,
                    WidthRequest = 96,
                    HeightRequest = 96
                };

                target.GestureRecognizers.Add(new DropGestureRecognizer
                {
                    AllowDrop = true,
                    DropCommand = new Command<DropEventArgs>(async args =>
                    {
                        if (args.Data.Properties.TryGetValue("id", out var val) && val is string droppedId)
                        {
                            if (_pieces.TryGetValue(droppedId, out var piece))
                            {
                                if (piece.Parent is Layout old) old.Children.Remove(piece);
                                target.Content = piece;

                                // лёгкая анимация
                                await piece.ScaleTo(1.06, 90);
                                await piece.ScaleTo(1.00, 90);

                                if (CheckWin()) ShowQuestion();
                            }
                        }
                    })
                });

                target.GestureRecognizers.Add(new TapGestureRecognizer
                {
                    Command = new Command(async () =>
                    {
                        if (_selectedPiece != null)
                        {
                            if (_selectedPiece.Parent is Layout old) old.Children.Remove(_selectedPiece);
                            target.Content = _selectedPiece;
                            _selectedPiece.Opacity = 1.0;
                            _selectedPiece = null;

                            await target.ScaleTo(1.03, 80);
                            await target.ScaleTo(1.00, 80);

                            if (CheckWin()) ShowQuestion();
                        }
                    })
                });

                _targetGrid.Add(target, c, r);
            }
    }

    private void BuildGrids(int rows, int cols)
    {
        _sourceGrid.Children.Clear();
        _targetGrid.Children.Clear();
        _sourceGrid.RowDefinitions.Clear();
        _sourceGrid.ColumnDefinitions.Clear();
        _targetGrid.RowDefinitions.Clear();
        _targetGrid.ColumnDefinitions.Clear();

        for (int i = 0; i < rows; i++)
        {
            _sourceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _targetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (int j = 0; j < cols; j++)
        {
            _sourceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _targetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }
    }

    private void AddPieceGestures(Image img, string id)
    {
        img.GestureRecognizers.Add(new DragGestureRecognizer
        {
            CanDrag = true,
            DragStartingCommand = new Command<DragStartingEventArgs>(e => e.Data.Properties["id"] = id)
        });

        img.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                if (_selectedPiece != null) _selectedPiece.Opacity = 1.0;
                _selectedPiece = img;
                img.Opacity = 0.6;
            })
        });
    }

    private bool CheckWin()
    {
        foreach (var cell in _targetGrid.Children.OfType<Border>())
        {
            if (cell.Content is not Image img) return false;
            int r = Grid.GetRow(cell);
            int c = Grid.GetColumn(cell);
            if (img.AutomationId != _correct[(r, c)]) return false;
        }
        return true;
    }

    // ====== ВОПРОС С 2 ОТВЕТАМИ ======
    Question _currentQuestion = new("", "", "", 'A');
    private void ShowQuestion()
    {
        // случайный вопрос
        _currentQuestion = _questions[_rnd.Next(_questions.Count)];
        LblQuestion.Text = _currentQuestion.Text;
        BtnA.Text = _currentQuestion.OptionA;
        BtnB.Text = _currentQuestion.OptionB;

        BtnA.BackgroundColor = Colors.LightGray;
        BtnB.BackgroundColor = Colors.LightGray;
        BtnA.IsEnabled = BtnB.IsEnabled = true;
        BtnNext.IsVisible = false;

        QuestionPanel.IsVisible = true;
    }

    private void BtnA_Clicked(object sender, EventArgs e) => HandleAnswer('A', (Button)sender, BtnB);
    private void BtnB_Clicked(object sender, EventArgs e) => HandleAnswer('B', (Button)sender, BtnA);

    private void HandleAnswer(char pick, Button picked, Button other)
    {
        if (pick == _currentQuestion.Correct)
        {
            picked.BackgroundColor = Colors.LightGreen;
            other.BackgroundColor = Colors.LightGray;
            BtnA.IsEnabled = BtnB.IsEnabled = false;
            BtnNext.IsVisible = true;
        }
        else
        {
            picked.BackgroundColor = Colors.IndianRed;
        }
    }

    private async void BtnNext_Clicked(object sender, EventArgs e)
    {
        QuestionPanel.IsVisible = false;

        if (_levelIndex == 9)
        {
            // Конец игры
            _timerRunning = false;
            _sw.Stop();

            var total = _sw.Elapsed;
            await DisplayAlert("Финиш 🎉",
                $"{_player.Name}, время: {(int)total.TotalMinutes:00}:{total.Seconds:00}",
                "OK");

            ScoreService.Add(new ScoreItem { Name = _player.Name, Time = total, When = DateTime.Now });
            await Shell.Current.GoToAsync("//rating");
            return;
        }

        // Следующий уровень
        _levelIndex++;
        LoadLevel(_levelIndex);
    }

    // ====== Нарезка картинки ======
    public static List<ImageSource> SplitImageFromStream(Stream input, int rows, int cols)
    {
        var result = new List<ImageSource>();
        using var bitmap = SKBitmap.Decode(input);

        int pieceWidth = bitmap.Width / cols;
        int pieceHeight = bitmap.Height / rows;

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var rect = new SKRectI(
                    c * pieceWidth, r * pieceHeight,
                    (c + 1) * pieceWidth, (r + 1) * pieceHeight
                );

                using var piece = new SKBitmap(rect.Width, rect.Height);
                using (var canvas = new SKCanvas(piece))
                    canvas.DrawBitmap(bitmap, rect, new SKRect(0, 0, rect.Width, rect.Height));

                using var image = SKImage.FromBitmap(piece);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                var bytes = data.ToArray();

                result.Add(ImageSource.FromStream(() => new MemoryStream(bytes)));
            }
        return result;
    }
}
