namespace FestivalRider.Services;

public enum ToastLevel { Info, Success, Warning, Error }

public sealed class ToastMessage(Guid id, ToastLevel level, string text)
{
    public Guid Id { get; } = id;
    public ToastLevel Level { get; } = level;
    public string Text { get; } = text;
    public bool IsExiting { get; set; }
}

public interface IToastService
{
    IReadOnlyList<ToastMessage> Active { get; }
    event Action? OnChange;
    void Show(string text, ToastLevel level = ToastLevel.Info);
    void Dismiss(Guid id);
}
