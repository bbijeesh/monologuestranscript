using mystickymonologues.Models;

namespace mystickymonologues.Services;

public class WindowManagerService
{
    private readonly SettingsService _settings;
    private readonly List<string> _openWindowIds = new();

    public int OpenWindowCount => _openWindowIds.Count;

    public WindowManagerService(SettingsService settings)
    {
        _settings = settings;
    }

    public bool CanOpenNewWindow() => _openWindowIds.Count < _settings.Settings.MaxWindows;

    // Returns (id, index)
    public (string id, int index) RegisterWindow()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _openWindowIds.Add(id);
        var index = _openWindowIds.IndexOf(id);
        return (id, index);
    }

    public void UnregisterWindow(string id)
    {
        _openWindowIds.Remove(id);
    }

    // Window position methods by index
    public WindowPosition? GetPositionByIndex(int index)
    {
        if (index < 0 || index >= _settings.Settings.WindowPositions.Count) return null;
        return _settings.Settings.WindowPositions[index];
    }

    public void SetPositionByIndex(int index, WindowPosition pos)
    {
        var list = _settings.Settings.WindowPositions;
        while (list.Count <= index) list.Add(new WindowPosition());
        list[index] = pos;
        _settings.Save();
    }
}
