namespace Xb31.Core.Transport;

internal readonly record struct Xb31DeviceCandidate(
    string Id,
    string Name);

internal static class Xb31DeviceSelector
{
    private const string TargetName = "SRS-XB31";

    internal static string? SelectId(IEnumerable<Xb31DeviceCandidate> candidates) =>
        candidates
            .Where(candidate =>
                string.Equals(candidate.Name, TargetName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.Id, StringComparer.Ordinal)
            .Select(candidate => candidate.Id)
            .FirstOrDefault();
}
