namespace SmartGuard.Adversary;

public static class AdversaryCommand
{
  public static Task<AdversaryObservation> RunAsync(AdversaryRequest request, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    ValidateRequest(request);
    return Task.FromResult(new AdversaryObservation(request.RequestedLayers.Select(tier =>
      new LayerObservation(tier, 0, request.MaxCases, "Trusted broker and independent verification are not implemented.")).ToArray()));
  }

  internal static void ValidateRequest(AdversaryRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    if (request.MaxCases is < 1 or > 2040 || request.RequestedLayers is null ||
        request.RequestedLayers.Length is < 1 or > 4 ||
        request.RequestedLayers.Any(tier => !Enum.IsDefined(tier)) ||
        request.RequestedLayers.Distinct().Count() != request.RequestedLayers.Length)
      throw new ArgumentException("Request must contain unique known layers and a bounded positive case budget.", nameof(request));
  }
}

public static class ParentResultModel
{
  public static ParentEvaluation Evaluate(AdversaryRequest request, ParentLayerEvidence[] evidence,
    bool calibrated, bool budgetAborted = false)
  {
    try { AdversaryCommand.ValidateRequest(request); }
    catch (ArgumentException) { return new(ResultState.InfrastructureFailed, 20); }
    if (evidence is null || evidence.Length > 4 || evidence.Any(layer => layer is null ||
          !Enum.IsDefined(layer.Tier) || !Enum.IsDefined(layer.State) || layer.Executed < 0 || layer.Remaining < 0 ||
          (long)layer.Executed + layer.Remaining > request.MaxCases) ||
        evidence.Select(layer => layer.Tier).Distinct().Count() != evidence.Length ||
        evidence.Any(layer => !request.RequestedLayers.Contains(layer.Tier) &&
          (layer.State != ResultState.NotRun || layer.Executed != 0 || layer.Remaining != 0)))
      return new(ResultState.InfrastructureFailed, 20);

    var requested = evidence.Where(layer => request.RequestedLayers.Contains(layer.Tier)).ToArray();
    if (requested.Any(layer => layer.State == ResultState.RestoreFailed)) return new(ResultState.RestoreFailed, 30);
    if (!calibrated || requested.Any(layer => layer.State == ResultState.SelfCheckFailed)) return new(ResultState.SelfCheckFailed, 21);
    if (requested.Any(layer => layer.State == ResultState.InfrastructureFailed ||
          (layer.State is ResultState.Passed or ResultState.ConfirmedFinding or ResultState.FlakyFinding && layer.Executed == 0)))
      return new(ResultState.InfrastructureFailed, 20);
    if (budgetAborted) return new(ResultState.InfrastructureFailed, 41);
    if (requested.Length != request.RequestedLayers.Length || requested.Any(layer =>
          layer.State is ResultState.NotRun or ResultState.Blocked || layer.Remaining > 0)) return new(ResultState.Blocked, 40);
    if (requested.Any(layer => layer.State == ResultState.FlakyFinding)) return new(ResultState.FlakyFinding, 11);
    if (requested.Any(layer => layer.State == ResultState.ConfirmedFinding)) return new(ResultState.ConfirmedFinding, 10);
    return new(ResultState.Passed, 0);
  }
}
