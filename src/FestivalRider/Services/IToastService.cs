namespace FestivalRider.Services;

public enum ToastLevel { Info, Success, Warning, Error }

public sealed record ToastMessage(Guid Id, ToastLevel Level, string Text);

public interface IToastService
{
    IReadOnlyList<ToastMessage> Active { get; }
    event Action? OnChange;
    void Show(string text, ToastLevel level = ToastLevel.Info);
    void Dismiss(Guid id);
}
