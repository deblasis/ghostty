using System.Text.Json.Serialization;

namespace Ghostty.Core.Sponsor.Update;

/// <summary>
/// Row in the Velopack manifest JSON (what <c>vpk pack</c> emits and
/// the B.2 Worker serves from R2). Property names match the JSON
/// verbatim for System.Text.Json. Kept in Core so it is
/// Velopack-package-free and reachable from Ghostty.Tests.
/// </summary>
public sealed record VelopackReleaseEntry(
    [property: JsonPropertyName("Version")] string Version,
    [property: JsonPropertyName("Type")] string Type,
    [property: JsonPropertyName("FileName")] string FileName,
    [property: JsonPropertyName("Size")] long Size,
    [property: JsonPropertyName("SHA1")] string SHA1);
