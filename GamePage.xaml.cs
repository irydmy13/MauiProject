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
    private const int LevelsCount = 5;     // игра на 5 уровней
    private const double PieceSize = 96;   // размер кусочка

    // ====== состояние ======
    public string PlayerName { get; set; } = "Player";
    private Player _player = new("Player");
    private readonly Stopwatch _sw = new();
    private bool _timerRunning;
    private bool _timerLoopActive = false;
    private bool _questionShowing = false;

    private int _rows, _cols;
    private int _levelIndex = 0;   // 0..LevelsCount-1

    private readonly (int rows, int cols)[] _difficulty =
    {
        (2,2), (2,3), (3,3), (3,4), (4,4)   // 5 уровней
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
    private readonly Dictionary<(int r, int c), string> _correct = new();    // верный id для ячейки
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

        // возврат в источник DnD
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

        // тап по источнику — положить выбранный кусок
        SourcePanel.GestureRecognizers.Add(new TapGestureRecognizer
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
        });
    }

    // ====== Показ вопроса только когда поле собрано и верхняя панель пуста ======
    private bool IsBoardSolved()
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

    private bool IsSourceEmpty() => SourcePanel.Children.Count == 0;

    private bool IsReadyForQuestion() => IsBoardSolved() && IsSourceEmpty();

    private void TryShowQuestionIfSolved()
    {
        if (_questionShowing || QuestionOverlay.IsVisible) return;

        Device.BeginInvokeOnMainThread(async () =>
        {
            // даём UI один тик применить изменения после Drop/Tap/ScaleTo
            await Task.Delay(30);

            if (_questionShowing || QuestionOverlay.IsVisible) return;
            if (!IsReadyForQuestion()) return;   // ← оба условия: поле верно + источник пуст

            // гарантируем, что слой сверху и растянут
            QuestionOverlay.ZIndex = 100;
            QuestionOverlay.HorizontalOptions = LayoutOptions.Fill;
            QuestionOverlay.VerticalOptions = LayoutOptions.Fill;

            ShowQuestion();
        });
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
        _levelImages = _imagePool.OrderBy(_ => _rnd.Next()).Take(LevelsCount).ToArray(); // 5 картинок
        _levelIndex = 0;

        _sw.Reset();
        _sw.Start();
        _timerRunning = true;
        StartTimerLoop();

        LoadLevel(_levelIndex);
    }

    private void StartTimerLoop()
    {
        if (_timerLoopActive) return;
        _timerLoopActive = true;

        Device.StartTimer(TimeSpan.FromSeconds(1), () =>
        {
            if (!_timerRunning) { _timerLoopActive = false; return false; }
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

    // ====== загрузка уровня ======
    private async void LoadLevel(int index)
    {
        (_rows, _cols) = _difficulty[index];
        var imgName = _levelImages[index];

        LblStatus.Text = $"Игрок: {_player.Name}, Уровень {index + 1}/{LevelsCount}  ({_rows}×{_cols})";
        ImgPreview.Source = imgName;

        // верхняя зона не схлопывается
        SourceHost.HeightRequest = Math.Max(220, _rows * (PieceSize + 12));

        // активируем нужные ячейки и чистим их
        for (int r = 0; r < MaxRows; r++)
            for (int c = 0; c < MaxCols; c++)
            {
                var active = r < _rows && c < _cols;
                var cell = _cells[r, c];
                cell.IsVisible = active;
                cell.Content = null;
            }

        _pieces.Clear();
        _correct.Clear();
        _undo.Clear();
        _selectedPiece = null;
        QuestionOverlay.IsVisible = false;
        BtnNext.IsVisible = false;

        // === нарезка в фоне ===
        List<byte[]> pieceBytes;
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(imgName);
            pieceBytes = await Task.Run(() => SplitAndResizeToBytes(stream, _rows, _cols, (int)PieceSize));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Ошибка", $"Не удалось загрузить картинку: {ex.Message}", "ОК");
            return;
        }

        // === создаём кусочки на UI ===
        var created = new List<Image>();
        int k = 0;
        for (int r = 0; r < _rows; r++)
            for (int c = 0; c < _cols; c++)
            {
                var id = $"piece_{r + 1}_{c + 1}";
                var src = ImageSource.FromStream(() => new MemoryStream(pieceBytes[k++]));
                var img = CreatePieceImage(src, id);
                _pieces[id] = img;
                _correct[(r, c)] = id;
                created.Add(img);
            }

        SourcePanel.Children.Clear();
        foreach (var img in created.OrderBy(_ => _rnd.Next()))
            SourcePanel.Children.Add(img);
    }

    // ====== обработчики цели ======
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

                        await piece.ScaleTo(1.02, 60);
                        await piece.ScaleTo(1.00, 60);
                        Device.BeginInvokeOnMainThread(TryShowQuestionIfSolved);
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

                    await target.ScaleTo(1.02, 60);
                    await target.ScaleTo(1.00, 60);
                    Device.BeginInvokeOnMainThread(TryShowQuestionIfSolved);
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

    // ====== создание кусочка ======
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

    // ====== вопрос ======
    private void ShowQuestion()
    {
        if (_questionShowing) return;
        _questionShowing = true;

        QuestionOverlay.IsVisible = true;  // включаем сразу (в зоне SourcePanel)
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
        _questionShowing = false;
        _timerRunning = true;
        StartTimerLoop();

        // финал при LevelsCount
        if (_levelIndex == LevelsCount - 1)
        {
            _timerRunning = false;
            _sw.Stop();

            var total = _sw.Elapsed;
            await DisplayAlert("Финиш 🎉",
                $"{_player.Name}, время: {(int)total.TotalMinutes:00}:{total.Seconds:00}",
                "OK");

            ScoreService.Add(new ScoreItem { Name = _player.Name, Level = LevelsCount, Time = total, When = DateTime.Now });
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
        var reached = _levelIndex + 1; // 1..LevelsCount

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

    // ====== «Назад» (безопасный откат) ======
    private void BtnUndo_Clicked(object? sender, EventArgs e)
    {
        if (QuestionOverlay.IsVisible) return;
        if (_undo.Count == 0) return;

        var a = _undo.Pop();

        // 1) убрать наш кусок из новой цели (или где бы он ни был)
        if (a.ToCell.Content == a.Piece) a.ToCell.Content = null;
        else DetachFromParent(a.Piece);

        // 2) вернуть вытеснённого обратно в эту цель
        if (a.Displaced != null)
        {
            DetachFromParent(a.Displaced);
            a.ToCell.Content = a.Displaced;
        }

        // 3) вернуть наш кусок туда, откуда он пришёл
        if (a.From == FromKind.Target && a.FromCell != null)
        {
            if (a.FromCell.Content is View v && v != a.Piece) DetachFromParent(v);
            a.FromCell.Content = a.Piece;
        }
        else
        {
            PlaceInSourcePanel(a.Piece);
        }

        if (_selectedPiece == a.Piece) { _selectedPiece.Opacity = 1; _selectedPiece = null; }
    }

    // ====== вспомогалки (безопасные) ======
    private void PlaceInSourcePanel(Image img)
    {
        if (img.Parent == SourcePanel) return; // уже там
        DetachFromParent(img);
        SourcePanel.Children.Add(img);
    }

    // универсальный безопасный detach (Grid/Flex/Border)
    private static void DetachFromParent(View v)
    {
        if (v.Parent is ContentView cv)
        {
            if (cv.Content == v) cv.Content = null;
            return;
        }
        if (v.Parent is Layout layout)
        {
            if (layout.Children.Contains(v))
                layout.Children.Remove(v);
            return;
        }
        // если родителей нет — ок
    }

    private MoveAction CaptureActionBeforePlace(Image piece, Border to)
    {
        FromKind from = FromKind.Source;
        Border? fromCell = null;

        for (int r = 0; r < MaxRows; r++)
            for (int c = 0; c < MaxCols; c++)
                if (_cells[r, c].Content == piece)
                {
                    from = FromKind.Target;
                    fromCell = _cells[r, c];
                    r = MaxRows; break;
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

    // ====== нарезка изображения (оптимизировано) ======
    private static List<byte[]> SplitAndResizeToBytes(Stream input, int rows, int cols, int pieceSizePx)
    {
        using var original = SKBitmap.Decode(input);
        if (original == null) throw new Exception("SKBitmap.Decode вернул null");

        int targetW = cols * pieceSizePx;
        int targetH = rows * pieceSizePx;

        using var scaled = original.Resize(new SKImageInfo(targetW, targetH), SKFilterQuality.Medium)
                         ?? original;

        var result = new List<byte[]>(rows * cols);
        int pieceW = targetW / cols;
        int pieceH = targetH / rows;

        using var scaledImage = SKImage.FromBitmap(scaled);

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var srcRect = new SKRectI(c * pieceW, r * pieceH, (c + 1) * pieceW, (r + 1) * pieceH);
                using var subset = scaledImage.Subset(srcRect);
                using var data = subset.Encode(SKEncodedImageFormat.Png, 90);
                result.Add(data.ToArray());
            }

        return result;
    }
}
