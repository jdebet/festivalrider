using FestivalRider.Services;

namespace FestivalRider.Tests;

public sealed class FakeToastService : IToastService
{
    public readonly List<ToastMessage> Messages = new();

    public IReadOnlyList<ToastMessage> Active => Messages;
    public event Action? OnChange;

    public void Show(string text, ToastLevel level = ToastLevel.Info)
    {
        Messages.Add(new ToastMessage(Guid.NewGuid(), level, text));
        OnChange?.Invoke();
    }

    public void Dismiss(Guid id)
    {
        if (Messages.RemoveAll(t => t.Id == id) > 0)
            OnChange?.Invoke();
    }
}
