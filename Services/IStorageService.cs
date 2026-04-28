namespace FestivalRider.Services;

public interface IStorageService
{
    Task EnsureLoadedAsync();
    Task FlushAsync();
    Task ClearAsync();

    bool AnotherTabActive { get; }

    event Action? OnAnotherTabChanged;
}
