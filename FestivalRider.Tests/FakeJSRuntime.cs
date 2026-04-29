using Microsoft.JSInterop;

namespace FestivalRider.Tests;

public sealed class FakeJSRuntime : IJSRuntime
{
    public readonly Dictionary<string, List<object?[]>> Invocations = new();
    public readonly Dictionary<string, object?> ReturnValues = new();

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        if (!Invocations.TryGetValue(identifier, out var list))
            Invocations[identifier] = list = new List<object?[]>();
        list.Add(args ?? Array.Empty<object?>());

        if (ReturnValues.TryGetValue(identifier, out var val))
            return new ValueTask<TValue>((TValue)val!);

        return new ValueTask<TValue>(Task.FromResult<TValue>(default!));
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        => InvokeAsync<TValue>(identifier, args);
}
