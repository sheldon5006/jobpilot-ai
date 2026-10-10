using JobPilot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Api.Data;

/// <summary>
/// Copies the profile, saved jobs, CV attachments and generated CVs from a local SQLite database
/// into PostgreSQL. Safe to re-run: jobs that already exist in the target are skipped.
/// Usage: dotnet run -- copy-database --to "postgresql://user:password@host/db" [--from path/to/jobpilot.db]
/// </summary>
public static class DatabaseCopier
{
    public static async Task<int> RunAsync(string[] args)
    {
        string? Option(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        var target = Option("--to") ?? Environment.GetEnvironmentVariable("DATABASE_URL");
        var source = Option("--from") ?? Path.Combine(AppContext.BaseDirectory, "jobpilot.db");
        if (!File.Exists(source))
        {
            source = Path.Combine(Directory.GetCurrentDirectory(), "jobpilot.db");
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("Pass the PostgreSQL target with --to \"postgresql://...\" (or set DATABASE_URL).");
            return 1;
        }

        if (!File.Exists(source))
        {
            Console.Error.WriteLine($"SQLite source not found: {source}. Pass it with --from.");
            return 1;
        }

        await using var from = new JobPilotDbContext(new DbContextOptionsBuilder<JobPilotDbContext>()
            .UseSqlite($"Data Source={source};Mode=ReadOnly").Options);
        await using var to = new JobPilotDbContext(new DbContextOptionsBuilder<JobPilotDbContext>()
            .UseNpgsql(PostgresConnection.FromUrl(target)).Options);

        await to.Database.EnsureCreatedAsync();
        Console.WriteLine($"Copying from {source} …");

        var profile = await from.CandidateProfiles.AsNoTracking().FirstOrDefaultAsync(item => item.Id == "primary");
        if (profile is not null)
        {
            var existing = await to.CandidateProfiles.FirstOrDefaultAsync(item => item.Id == "primary");
            if (existing is null)
            {
                to.CandidateProfiles.Add(new CandidateProfileDocument { Id = "primary", ProfileJson = profile.ProfileJson, UpdatedAtUtc = Utc(profile.UpdatedAtUtc) });
            }
            else
            {
                existing.ProfileJson = profile.ProfileJson;
                existing.UpdatedAtUtc = Utc(profile.UpdatedAtUtc);
            }
        }

        var existingJobIds = (await to.SavedJobs.Select(job => job.Id).ToListAsync()).ToHashSet();
        var jobs = await from.SavedJobs.AsNoTracking().ToListAsync();
        var copiedJobIds = new HashSet<Guid>();
        foreach (var job in jobs.Where(job => !existingJobIds.Contains(job.Id)))
        {
            job.CreatedAtUtc = Utc(job.CreatedAtUtc);
            job.UpdatedAtUtc = Utc(job.UpdatedAtUtc);
            to.SavedJobs.Add(job);
            copiedJobIds.Add(job.Id);
        }

        var attachments = await from.JobCvAttachments.AsNoTracking().Where(item => copiedJobIds.Contains(item.JobId)).ToListAsync();
        foreach (var attachment in attachments)
        {
            attachment.UploadedAtUtc = Utc(attachment.UploadedAtUtc);
            to.JobCvAttachments.Add(attachment);
        }

        var generatedCvs = await from.GeneratedCvs.AsNoTracking().Where(item => copiedJobIds.Contains(item.JobId)).ToListAsync();
        foreach (var generated in generatedCvs)
        {
            generated.CreatedAtUtc = Utc(generated.CreatedAtUtc);
            generated.UpdatedAtUtc = Utc(generated.UpdatedAtUtc);
            to.GeneratedCvs.Add(generated);
        }

        await to.SaveChangesAsync();
        Console.WriteLine(
            $"Done. Profile: {(profile is null ? "none" : "copied")} · jobs copied: {copiedJobIds.Count} " +
            $"(skipped {jobs.Count - copiedJobIds.Count} already present) · CV files: {attachments.Count} · generated CVs: {generatedCvs.Count}.");
        return 0;
    }

    // SQLite does not keep DateTime.Kind; PostgreSQL timestamptz columns require UTC values.
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
