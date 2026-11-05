using Deckle.Core.Settings;

namespace Deckle.Core.Scanning;

/// <summary>Named scan profiles persisted to profiles.json. There is always at least one profile.</summary>
public sealed class ProfileStore
{
    private readonly string _path;
    private readonly List<ScanSettings> _profiles;

    public ProfileStore(string path)
    {
        _path = path;
        _profiles = JsonStore.Load(path, () => new List<ScanSettings>());
        _profiles.RemoveAll(p => string.IsNullOrWhiteSpace(p.Name));
        if (_profiles.Count == 0)
        {
            _profiles.Add(new ScanSettings());
        }
    }

    public event EventHandler? Changed;

    public IReadOnlyList<ScanSettings> Profiles => _profiles;

    public ScanSettings Get(string? name) =>
        _profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? _profiles[0];

    /// <summary>Stores a copy of <paramref name="settings"/>, replacing any profile with the same name.</summary>
    public void Save(ScanSettings settings)
    {
        var copy = settings.Clone();
        var index = _profiles.FindIndex(p => string.Equals(p.Name, settings.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _profiles[index] = copy;
        }
        else
        {
            _profiles.Add(copy);
        }
        Persist();
    }

    public bool Rename(string oldName, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase) && Exists(newName)))
        {
            return false;
        }
        var profile = _profiles.FirstOrDefault(p => string.Equals(p.Name, oldName, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return false;
        }
        profile.Name = newName;
        Persist();
        return true;
    }

    public bool Delete(string name)
    {
        if (_profiles.Count <= 1)
        {
            return false;
        }
        var removed = _profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            Persist();
        }
        return removed;
    }

    public bool Exists(string name) => _profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public string UniqueName(string baseName)
    {
        if (!Exists(baseName))
        {
            return baseName;
        }
        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private void Persist()
    {
        JsonStore.Save(_path, _profiles);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
