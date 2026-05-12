namespace FestivalRider.Services;

public class ToastService : IToastService
{
    private readonly List<ToastMessage> _active = new();
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<Guid, ITimer> _timers = new();

    public ToastService(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyList<ToastMessage> Active => _active;
    public event Action? OnChange;

    public void Show(string text, ToastLevel level = ToastLevel.Info)
    {
        var toast = new ToastMessage(Guid.NewGuid(), level, text);

        if (_active.Count >= 5)
        {
            var oldest = _active.First(t => !t.IsExiting);
            DismissWithAnimation(oldest.Id);
        }

        _active.Add(toast);
        OnChange?.Invoke();

        ITimer? autoTimer = null;
        autoTimer = _timeProvider.CreateTimer(
            _ =>
            {
                DismissWithAnimation(toast.Id);
                autoTimer?.Dispose();
            },
            null,
            TimeSpan.FromSeconds(5),
            Timeout.InfiniteTimeSpan);
        _timers[toast.Id] = autoTimer;
    }

    public void Dismiss(Guid id)
    {
        if (_timers.TryGetValue(id, out var timer))
        {
            timer?.Dispose();
            _timers.Remove(id);
        }
        DismissWithAnimation(id);
    }

    private void DismissWithAnimation(Guid id)
    {
        var toast = _active.FirstOrDefault(t => t.Id == id && !t.IsExiting);
        if (toast is null) return;

        toast.IsExiting = true;
        OnChange?.Invoke();

        var delay = TimeSpan.FromMilliseconds(400);
        ITimer? exitTimer = null;
        exitTimer = _timeProvider.CreateTimer(
            _ =>
            {
                _active.RemoveAll(t => t.Id == id);
                OnChange?.Invoke();
                exitTimer?.Dispose();
            },
            null,
            delay,
            Timeout.InfiniteTimeSpan);
    }
}
