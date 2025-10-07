namespace MauiProject;

public class Player
{
    public string Name { get; }
    public Player(string name) => Name = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim();
}
