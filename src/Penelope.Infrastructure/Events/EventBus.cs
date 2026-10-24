using Penelope.Abstractions;

namespace Penelope.Infrastructure.Events;

public sealed class EventBus : IEventBus
{
    private readonly object _gate = new();
    private readonly List<PenelopeEvent> _recent = [];
    private readonly Dictionary<string, List<Action<PenelopeEvent>>> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public void Publish(PenelopeEvent evt)
    {
        lock (_gate)
        {
            _recent.Add(evt);
            if (_recent.Count > 500)
            {
                _recent.RemoveAt(0);
            }
        }

        List<Action<PenelopeEvent>> copy;
        lock (_gate)
        {
            copy = _handlers.TryGetValue(evt.Name, out var list) ? list.ToList() : [];
        }

        foreach (var handler in copy)
        {
            try
            {
                handler(evt);
            }
            catch
            {
                // Handlers must not break publishers.
            }
        }
    }

    public IDisposable Subscribe(string eventName, Action<PenelopeEvent> handler)
    {
        lock (_gate)
        {
            if (!_handlers.TryGetValue(eventName, out var list))
            {
                list = [];
                _handlers[eventName] = list;
            }

            list.Add(handler);
        }

        return new Subscription(this, eventName, handler);
    }

    public IReadOnlyList<PenelopeEvent> Recent(int limit = 100)
    {
        lock (_gate)
        {
            return _recent.TakeLast(limit).ToList();
        }
    }

    private void Unsubscribe(string eventName, Action<PenelopeEvent> handler)
    {
        lock (_gate)
        {
            if (_handlers.TryGetValue(eventName, out var list))
            {
                list.Remove(handler);
            }
        }
    }

    private sealed class Subscription(EventBus bus, string name, Action<PenelopeEvent> handler) : IDisposable
    {
        public void Dispose() => bus.Unsubscribe(name, handler);
    }
}
