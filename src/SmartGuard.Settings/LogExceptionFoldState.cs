namespace SmartGuard.Settings;

public sealed class LogExceptionFoldState
{
    private readonly HashSet<string> _expandedEventIds = new(StringComparer.Ordinal);

    public bool IsExpanded(string rootEventId)
        => _expandedEventIds.Contains(rootEventId);

    public void SetExpanded(string rootEventId, bool expanded)
    {
        if (expanded)
            _expandedEventIds.Add(rootEventId);
        else
            _expandedEventIds.Remove(rootEventId);
    }

    public bool Toggle(string rootEventId)
    {
        if (_expandedEventIds.Contains(rootEventId))
        {
            _expandedEventIds.Remove(rootEventId);
            return false;
        }

        _expandedEventIds.Add(rootEventId);
        return true;
    }
}
