using SmartGuard.Configuration;

namespace SmartGuard.Settings;

internal sealed class SavedLogFilterCoordinator
{
  private readonly GuardConfigRepository _repository;
  private readonly Func<GuardConfig> _getConfig;
  private readonly Action<GuardConfig> _commitConfig;

  internal SavedLogFilterCoordinator(
    GuardConfigRepository repository,
    Func<GuardConfig> getConfig,
    Action<GuardConfig> commitConfig)
  {
    _repository = repository;
    _getConfig = getConfig;
    _commitConfig = commitConfig;
  }

  internal IReadOnlyList<SavedLogFilter> GetFilters()
    => _getConfig().LogViewPreferences.SavedFilters;

  internal void Add(SavedLogFilter filter)
  {
    var config = _getConfig();
    config.LogViewPreferences.SavedFilters.Add(filter);
    Persist(config);
  }

  internal void Update(SavedLogFilter filter)
  {
    var config = _getConfig();
    var index = config.LogViewPreferences.SavedFilters.FindIndex(item => item.Id == filter.Id);
    if (index < 0)
      throw new InvalidOperationException($"Saved filter '{filter.Id}' was not found.");

    config.LogViewPreferences.SavedFilters[index] = filter;
    Persist(config);
  }

  internal void Rename(string id, string newName)
  {
    var config = _getConfig();
    var filter = config.LogViewPreferences.SavedFilters.SingleOrDefault(item => item.Id == id)
      ?? throw new InvalidOperationException($"Saved filter '{id}' was not found.");
    filter.Name = newName;
    Persist(config);
  }

  internal void Delete(string id)
  {
    var config = _getConfig();
    var removed = config.LogViewPreferences.SavedFilters.RemoveAll(item => item.Id == id);
    if (removed == 0)
      throw new InvalidOperationException($"Saved filter '{id}' was not found.");

    if (string.Equals(config.LogViewPreferences.DefaultFilterId, id, StringComparison.Ordinal))
      config.LogViewPreferences.DefaultFilterId = null;

    Persist(config);
  }

  internal void Reorder(IReadOnlyList<string> orderedIds)
  {
    var config = _getConfig();
    var byId = config.LogViewPreferences.SavedFilters.ToDictionary(filter => filter.Id);
    if (orderedIds.Count != byId.Count || orderedIds.Any(id => !byId.ContainsKey(id)))
      throw new InvalidOperationException("Reorder must include every saved filter exactly once.");

    config.LogViewPreferences.SavedFilters = orderedIds.Select(id => byId[id]).ToList();
    Persist(config);
  }

  internal void SetDefault(string? id)
  {
    var config = _getConfig();
    if (id is not null &&
        config.LogViewPreferences.SavedFilters.All(filter => filter.Id != id))
    {
      throw new InvalidOperationException($"Saved filter '{id}' was not found.");
    }

    config.LogViewPreferences.DefaultFilterId = id;
    Persist(config);
  }

  private void Persist(GuardConfig config)
  {
    _repository.Save(config);
    _commitConfig(config);
  }
}
