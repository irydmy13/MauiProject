namespace MauiProject;

public class Question
{
    public string Text { get; }
    public string OptionA { get; }
    public string OptionB { get; }
    public char Correct { get; } // 'A' или 'B'

    public Question(string text, string a, string b, char correct)
    {
        Text = text; OptionA = a; OptionB = b;
        Correct = char.ToUpperInvariant(correct) == 'B' ? 'B' : 'A';
    }
}
