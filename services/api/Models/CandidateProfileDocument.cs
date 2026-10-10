namespace JobPilot.Api.Models;

/// <summary>
/// Stores the editable candidate profile as JSON so it persists in both local SQLite
/// and hosted PostgreSQL instead of depending on an ephemeral deployment file.
/// </summary>
public sealed class CandidateProfileDocument
{
    public string Id { get; set; } = "primary";
    public string ProfileJson { get; set; } = "{}";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record CandidateProfileResponse(CandidateProfile Profile, DateTime UpdatedAtUtc);
