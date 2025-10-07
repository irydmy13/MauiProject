using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Maui.Storage;
using SkiaSharp;

namespace MauiProject;

[QueryProperty(nameof(PlayerName), "playerName")]
public partial class GamePage : ContentPage
{
    // ====== общие настройки ======
    private const int MaxRows = 7;
    private const int MaxCols = 7;
    private const double PieceSize = 96; // размер кусочка в px

    // ====== состояние ======
    public string PlayerName { get; set; } = "Player";
    private Player _player = new("Player");
    private readonly Stopwatch _sw = new();
    private bool _timerRunning;

    private int _rows, _cols;      // активный размер уровня
    private int _levelIndex = 0;   // 0..9

    private readonly (int rows, int cols)[] _difficulty =
    {
        (2,2),(2,3),(3,3),(3,4),(4,4),
        (4,5),(5,5),(5,6),(6,6),(6,7)
    };

    private readonly string[] _imagePool =
    {
        "p1.png","p2.png","p3.png","p4.png","p5.png",
        "p6.png","p7.png","p8.png","p9.png","p10.png"
    };
    private string[] _levelImages = Array.Empty<string>();
    private readonly Random _rnd = new();

    // пазл
    private readonly Dictionary<string, Image> _pieces = new();              // id -> Image
    private readonly Dictionary<(int r, int c), string> _correct = new();     // верный id для ячейки
    private Image? _selectedPiece;

    // поле (цель) — создаём один раз
    private readonly Border[,] _cells = new Border[MaxRows, MaxCols];

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
    private Question _currentQuestion = new("", "", "", 'A');

    // ====== UNDO ======
    private enum FromKind { Source, Target }
    private sealed class MoveAction
    {
        public Image Piece = null!;
        public Border ToCell = null!;
        public Image? Displaced;
        public FromKind From;
        public Border? FromCell; // если шли из целевой ячейки
    }
    private readonly Stack<MoveAction> _undo = new();

    public GamePage()
    {
        InitializeComponent();

        // инициализируем один раз матрицу цели и обработчики
        InitBoardsOnce();

        // жесты для возврата в источник
        SourceHost.GestureRecognizers.Add(new DropGestureRecognizer
        {
            AllowDrop = true,
            DropCommand = new Command<DropEventArgs>(args =>
            {
                if (args.Data.Properties.TryGetValue("id", out var val) && val is string id
                    && _pieces.TryGetValue(id, out var img))
                {
                    DetachFromParent(img);
                    PlaceInSourcePanel(img);
                    if (_selectedPiece == img) { _selectedPiece.Opacity = 1; _selectedPiece = null; }
                }
            })
        });

        var tapSource = new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                if (_selectedPiece != null)
                {
                    PlaceInSourcePanel(_selectedPiece);
                    _selectedPiece.Opacity = 1;
                    _selectedPiece = null;
                }
            })
        };
        SourcePanel.GestureRecognizers.Add(tapSource);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _player = new Player(PlayerName);
        StartNewRun();
    }

    protected override void OnDisappearing()
    {
        _timerRunning = false;
        base.OnDisappearing();
    }

    // ====== старт новой игры ======
    private void StartNewRun()
    {
        _levelImages = _imagePool.OrderBy(_ => _rnd.Next()).Take(10).ToArray();
        _levelIndex = 0;

        _sw.Reset();
        _sw.Start();
        _timerRunning = true;
        StartTimerLoop();

        LoadLevel(_levelIndex);
    }

    private void StartTimerLoop()
    {
        Device.StartTimer(TimeSpan.FromSeconds(1), () =>
        {
            if (!_timerRunning) return false;
            LblTime.Text = $"{(int)_sw.Elapsed.TotalMinutes:00}:{_sw.Elapsed.Seconds:00}";
            return true;
        });
    }

    // ====== инициализация поля 7×7 (один раз) ======
    private void InitBoardsOnce()
    {
        var grid = new Grid
        {
            Padding = new Thickness(6),
            RowSpacing = 4,
            ColumnSpacing = 4,
            BackgroundColor = Color.FromArgb("#E3E6EB")
        };

        for (int i = 0; i < MaxRows; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int j = 0; j < MaxCols; j++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (int r = 0; r < MaxRows; r++)
            for (int c = 0; c < MaxCols; c++)
            {
                var cell = new Border
                {
                    BackgroundColor = Color.FromArgb("#ECEFF1"),
                    Stroke = Colors.Black,
                    StrokeThickness = 1,
                    WidthRequest = PieceSize,
                    HeightRequest = PieceSize,
                    IsVisible = false
                };
                AttachTargetHandlers(cell);
                _cells[r, c] = cell;
                grid.Add(cell, c, r);
            }

        TargetHost.Children.Clear();
        TargetHost.Children.Add(grid);
    }

    // ====== загрузка уровня (без перестройки сеток) ======
    private async void LoadLevel(int index)
    {
        (_rows, _cols) = _difficulty[index];
        var imgName = _levelImages[index];

        LblStatus.Text = $"Игрок: {_player.Name}, Уровень {index + 1}/10  ({_rows}×{_cols})";
        ImgPreview.Source = imgName;

        // верхняя зона не схлопывается
        SourceHost.HeightRequest = Math.Max(220, _rows * (PieceSize + 12));

        // активируем нужное число ячеек и чистим их
        for (int r = 0; r < MaxRows; r++)
            for (int c = 0; c < MaxCols; c++)
            {
                var active = r < _rows && c < _cols;
                var cell = _cells[r, c];
                cell.IsVisible = active;
                if (!active) cell.Content = null;
                else cell.Content = null;
            }

        _pieces.Clear();
        _correct.Clear();
        _undo.Clear();
        _selectedPiece = null;

        QuestionOverlay.IsVisible = false;
        BtnNext.IsVisible = false;

        // режем картинку и создаём кусочки
        using var stream = await FileSystem.OpenAppPackageFileAsync(imgName);
        var parts = SplitImageFromStream(stream, _rows, _cols);

        // создаём кусочки с id согласно верной позиции (row-major)
        var created = new List<Image>();
        int k = 0;
        for (int r = 0; r < _rows; r++)
            for (int c = 0; c < _cols; c++)
            {
                var id = $"piece_{r + 1}_{c + 1}";
                var img = CreatePieceImage(parts[k++], id);
                _pieces[id] = img;
                _correct[(r, c)] = id;
                created.Add(img);
            }

        // перемешиваем и добавляем в FlexLayout
        SourcePanel.Children.Clear();
        foreach (var img in created.OrderBy(_ => _rnd.Next()))
            SourcePanel.Children.Add(img);
    }

    // ====== обработчики цели (устанавливаются один раз на каждую ячейку) ======
    private void AttachTargetHandlers(Border target)
    {
        // drop с обменом
        target.GestureRecognizers.Add(new DropGestureRecognizer
        {
            AllowDrop = true,
            DropCommand = new Command<DropEventArgs>(async args =>
            {
                if (args.Data.Properties.TryGetValue("id", out var val) && val is string droppedId)
                {
                    if (_pieces.TryGetValue(droppedId, out var piece))
                    {
                        var action = CaptureActionBeforePlace(piece, target);
                        PlacePieceIntoTarget(piece, target, displacedGoesToSource: true);
                        _undo.Push(action);

                        await piece.ScaleTo(1.06, 90);
                        await piece.ScaleTo(1.00, 90);
                        if (CheckWin()) ShowQuestion();
                    }
                }
            })
        });

        // tap: поставить выбранный / забрать кусок
        target.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                if (_selectedPiece != null)
                {
                    var action = CaptureActionBeforePlace(_selectedPiece, target);
                    PlacePieceIntoTarget(_selectedPiece, target, displacedGoesToSource: true);
                    _undo.Push(action);

                    _selectedPiece.Opacity = 1.0;
                    _selectedPiece = null;

                    await target.ScaleTo(1.03, 80);
                    await target.ScaleTo(1.00, 80);
                    if (CheckWin()) ShowQuestion();
                }
                else if (target.Content is Image imgInside)
                {
                    target.Content = null;
                    _selectedPiece = imgInside;
                    _selectedPiece.Opacity = 0.6;
                }
            })
        });
    }

    // ====== создание кусочка с общими жестами ======
    private Image CreatePieceImage(ImageSource src, string id)
    {
        var img = new Image
        {
            Source = src,
            WidthRequest = PieceSize,
            HeightRequest = PieceSize,
            AutomationId = id
        };

        img.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                if (_selectedPiece != null) _selectedPiece.Opacity = 1.0;
                _selectedPiece = img;
                img.Opacity = 0.6;
            })
        });

        img.GestureRecognizers.Add(new DragGestureRecognizer
        {
            CanDrag = true,
            DragStartingCommand = new Command<DragStartingEventArgs>(e => e.Data.Properties["id"] = id)
        });

        return img;
    }

    // ====== проверка победы ======
    private bool CheckWin()
    {
        for (int r = 0; r < _rows; r++)
            for (int c = 0; c < _cols; c++)
            {
                var cell = _cells[r, c];
                if (cell.Content is not Image img) return false;
                if (img.AutomationId != _correct[(r, c)]) return false;
            }
        return true;
    }

    // ====== вопрос (оверлей в верхней зоне) ======
    private void ShowQuestion()
    {
        _timerRunning = false;
        _currentQuestion = _questions[_rnd.Next(_questions.Count)];
        LblQuestion.Text = _currentQuestion.Text;
        BtnA.Text = _currentQuestion.OptionA;
        BtnB.Text = _currentQuestion.OptionB;

        BtnA.BackgroundColor = Colors.LightGray;
        BtnB.BackgroundColor = Colors.LightGray;
        BtnA.IsEnabled = BtnB.IsEnabled = true;
        BtnNext.IsVisible = false;

        SourceHost.IsEnabled = false;
        TargetHost.IsEnabled = false;
        QuestionOverlay.IsVisible = true;
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
        QuestionOverlay.IsVisible = false;
        SourceHost.IsEnabled = true;
        TargetHost.IsEnabled = true;
        _timerRunning = true;
        StartTimerLoop();

        if (_levelIndex == 9)
        {
            _timerRunning = false;
            _sw.Stop();

            var total = _sw.Elapsed;
            await DisplayAlert("Финиш 🎉",
                $"{_player.Name}, время: {(int)total.TotalMinutes:00}:{total.Seconds:00}",
                "OK");

            ScoreService.Add(new ScoreItem { Name = _player.Name, Level = 10, Time = total, When = DateTime.Now });
            await Shell.Current.GoToAsync("//rating");
            return;
        }

        _levelIndex++;
        LoadLevel(_levelIndex);
    }

    // ====== завершить игру ======
    private async void BtnEnd_Clicked(object sender, EventArgs e)
    {
        var ok = await DisplayAlert("Завершить игру?",
            "Прогресс будет сохранён в рейтинге (имя, уровень, время).",
            "Да", "Нет");
        if (!ok) return;

        _timerRunning = false;
        _sw.Stop();

        var total = _sw.Elapsed;
        var reached = _levelIndex + 1;

        ScoreService.Add(new ScoreItem
        {
            Name = _player.Name,
            Level = reached,
            Time = total,
            When = DateTime.Now
        });

        await DisplayAlert("Игра завершена",
            $"{_player.Name}, уровень: {reached}, время: {(int)total.TotalMinutes:00}:{total.Seconds:00}",
            "Ок");

        await Shell.Current.GoToAsync("//rating");
    }

    // ====== «Назад» (полный откат последнего шага) ======
    private void BtnUndo_Clicked(object sender, EventArgs e)
    {
        if (QuestionOverlay.IsVisible) return;
        if (_undo.Count == 0) return;

        var a = _undo.Pop();

        // убрать наш кусок из новой цели
        if (a.ToCell.Content is Image placed && placed == a.Piece)
            a.ToCell.Content = null;
        else
            DetachFromParent(a.Piece);

        // вернуть вытесненного обратно в новую цель
        if (a.Displaced != null)
        {
            DetachFromParent(a.Displaced);
            a.ToCell.Content = a.Displaced;
        }

        // вернуть наш кусок туда, откуда он пришёл
        if (a.From == FromKind.Target && a.FromCell != null)
        {
            a.FromCell.Content = a.Piece;
        }
        else
        {
            PlaceInSourcePanel(a.Piece);
        }

        if (_selectedPiece == a.Piece) { _selectedPiece.Opacity = 1; _selectedPiece = null; }
    }

    // ====== вспомогалки ======
    private void PlaceInSourcePanel(Image img)
    {
        DetachFromParent(img);
        SourcePanel.Children.Add(img);
    }

    private static void DetachFromParent(Image img)
    {
        if (img.Parent is Layout layout)
            layout.Children.Remove(img);
        else if (img.Parent is ContentView cv)
            cv.Content = null;
    }

    private MoveAction CaptureActionBeforePlace(Image piece, Border to)
    {
        FromKind from = FromKind.Source;
        Border? fromCell = null;

        // проверяем, не стоит ли уже в какой-то цели
        for (int r = 0; r < MaxRows; r++)
            for (int c = 0; c < MaxCols; c++)
                if (_cells[r, c].Content == piece)
                {
                    from = FromKind.Target;
                    fromCell = _cells[r, c];
                    break;
                }

        return new MoveAction
        {
            Piece = piece,
            ToCell = to,
            Displaced = to.Content as Image,
            From = from,
            FromCell = fromCell
        };
    }

    private void PlacePieceIntoTarget(Image piece, Border to, bool displacedGoesToSource)
    {
        var displaced = to.Content as Image;

        DetachFromParent(piece);
        to.Content = piece;

        if (displacedGoesToSource && displaced != null && displaced != piece)
            PlaceInSourcePanel(displaced);
    }

    // ====== нарезка изображения ======
    public static List<ImageSource> SplitImageFromStream(Stream input, int rows, int cols)
    {
        var result = new List<ImageSource>();
        using var bitmap = SKBitmap.Decode(input);

        int pieceWidth = bitmap.Width / cols;
        int pieceHeight = bitmap.Height / rows;

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var rect = new SkiaSharp.SKRectI(
                    c * pieceWidth, r * pieceHeight,
                    (c + 1) * pieceWidth, (r + 1) * pieceHeight
                );

                using var piece = new SkiaSharp.SKBitmap(rect.Width, rect.Height);
                using (var canvas = new SkiaSharp.SKCanvas(piece))
                    canvas.DrawBitmap(bitmap, rect, new SkiaSharp.SKRect(0, 0, rect.Width, rect.Height));

                using var image = SkiaSharp.SKImage.FromBitmap(piece);
                using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                var bytes = data.ToArray();

                result.Add(ImageSource.FromStream(() => new MemoryStream(bytes)));
            }
        return result;
    }
}
