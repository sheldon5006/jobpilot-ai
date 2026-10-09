using JobPilot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Api.Data;

public sealed class JobPilotDbContext(DbContextOptions<JobPilotDbContext> options)
    : DbContext(options)
{
    public DbSet<SavedJob> SavedJobs => Set<SavedJob>();
    public DbSet<JobCvAttachment> JobCvAttachments => Set<JobCvAttachment>();
    public DbSet<CandidateProfileDocument> CandidateProfiles => Set<CandidateProfileDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var job = modelBuilder.Entity<SavedJob>();
        job.ToTable("SavedJobs");
        job.HasKey(item => item.Id);
        job.Property(item => item.JobTitle).HasMaxLength(160).IsRequired();
        job.Property(item => item.Company).HasMaxLength(160);
        job.Property(item => item.ApplicationStatus).HasMaxLength(24).IsRequired();
        job.Property(item => item.Notes).HasMaxLength(4000);
        job.Property(item => item.Recommendation).HasMaxLength(16).IsRequired();
        job.Property(item => item.DetectedLanguage).HasMaxLength(80);
        job.Property(item => item.AnalysisJson).IsRequired();
        job.Property(item => item.JobDescription).IsRequired();
        job.HasIndex(item => item.CreatedAtUtc);
        job.HasIndex(item => item.ApplicationStatus);
        job.HasOne(item => item.CvAttachment)
            .WithOne(attachment => attachment.Job)
            .HasForeignKey<JobCvAttachment>(attachment => attachment.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        var cv = modelBuilder.Entity<JobCvAttachment>();
        cv.ToTable("JobCvAttachments");
        cv.HasKey(item => item.JobId);
        cv.Property(item => item.FileName).HasMaxLength(255).IsRequired();
        cv.Property(item => item.ContentType).HasMaxLength(160).IsRequired();
        cv.Property(item => item.Bytes).IsRequired();

        var profile = modelBuilder.Entity<CandidateProfileDocument>();
        profile.ToTable("CandidateProfiles");
        profile.HasKey(item => item.Id);
        profile.Property(item => item.Id).HasMaxLength(32).IsRequired();
        profile.Property(item => item.ProfileJson).IsRequired();
        profile.Property(item => item.UpdatedAtUtc).IsRequired();
    }
}
