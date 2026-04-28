using Microsoft.Extensions.Logging;

namespace FestivalRider.Services;

public class StorageService : IStorageService
{
    private readonly ILogger<StorageService> _logger;

    public StorageService(ILogger<StorageService> logger)
    {
        _logger = logger;
    }

    public bool AnotherTabActive { get; private set; }

    public event Action? OnAnotherTabChanged;

    public Task EnsureLoadedAsync() => Task.CompletedTask;
    public Task FlushAsync() => Task.CompletedTask;
    public Task ClearAsync() => Task.CompletedTask;
}
