namespace FestivalRider.Services;

public class ToastService : IToastService
{
    private readonly List<ToastMessage> _active = new();

    public IReadOnlyList<ToastMessage> Active => _active;
    public event Action? OnChange;

    public void Show(string text, ToastLevel level = ToastLevel.Info)
    {
        _active.Add(new ToastMessage(Guid.NewGuid(), level, text));
        OnChange?.Invoke();
    }

    public void Dismiss(Guid id)
    {
        if (_active.RemoveAll(t => t.Id == id) > 0) OnChange?.Invoke();
    }
}
