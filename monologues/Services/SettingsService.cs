using mystickymonologues.Models;
using Newtonsoft.Json;
using System.IO;
using System.Windows;

namespace mystickymonologues.Services;

public class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "mystickymonologues", "appsettings.json");

    private AppConfiguration _config = new();

    public AppSettings Settings => _config.AppSettings;

    public SettingsService()
    {
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                _config = JsonConvert.DeserializeObject<AppConfiguration>(json) ?? new AppConfiguration();
            }
            else
            {
                // Load defaults from embedded appsettings.json
                var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (File.Exists(localPath))
                {
                    var json = File.ReadAllText(localPath);
                    _config = JsonConvert.DeserializeObject<AppConfiguration>(json) ?? new AppConfiguration();
                }

                if (string.IsNullOrEmpty(_config.AppSettings.NotesFolder))
                {
                    _config.AppSettings.NotesFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "MyStickyMonologues");
                }
            }
        }
        catch
        {
            _config = new AppConfiguration();
            _config.AppSettings.NotesFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "MyStickyMonologues");
        }
    }

    public void Save()
    {
        
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
        File.WriteAllText(SettingsPath, json);
    }

    public string GetTodayNotesFolder()
    {
        var folder = Path.Combine(Settings.NotesFolder, DateTime.Now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public string GetNoteFilePath(string windowId)
    {
        return Path.Combine(GetTodayNotesFolder(), $"note_{windowId}.txt");
    }

    public void AppendToNote(string windowId, string text)
    {
        var path = GetNoteFilePath(windowId);
        File.AppendAllText(path, text + Environment.NewLine);
    }

    public void SaveNote(string windowId, string text)
    {
        var path = GetNoteFilePath(windowId);
        File.WriteAllText(path, text ?? string.Empty);
    }

    public string LoadNote(string windowId)
    {
        var path = GetNoteFilePath(windowId);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }
}
