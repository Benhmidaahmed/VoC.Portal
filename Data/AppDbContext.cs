using Microsoft.EntityFrameworkCore;
using Xrmbox.VoC.Portal.Models.Local;

namespace Xrmbox.VoC.Portal.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<SurveyInvitation> SurveyInvitations { get; set; } = null!;
        public DbSet<LocalResponse> LocalResponses { get; set; } = null!;
        public DbSet<IntegrationLog> IntegrationLogs { get; set; } = null!;
        public DbSet<Survey> Surveys { get; set; }
        public DbSet<Campaign> Campaigns { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // LocalResponse configuration (existant)
            var responseEntity = modelBuilder.Entity<LocalResponse>();

            responseEntity.ToTable("LocalResponses");
            responseEntity.HasKey(e => e.Id);
            responseEntity.Property(e => e.Id).ValueGeneratedOnAdd();

            responseEntity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(200);

            responseEntity.Property(e => e.SurveyId).IsRequired();
            responseEntity.Property(e => e.ParticipantId).IsRequired(false);
            responseEntity.Property(e => e.CampagneId).IsRequired(false);

            responseEntity.Property(e => e.ResponseJson).IsRequired();
            responseEntity.Property(e => e.SubmittedAt).IsRequired();

            responseEntity.Property(e => e.IsSynced)
                .IsRequired()
                .HasDefaultValue(false);

            responseEntity.Property(e => e.DataverseId)
                .HasMaxLength(50)
                .IsRequired(false);

            responseEntity.Property(e => e.SyncError)
                .HasMaxLength(2000)
                .IsRequired(false);

            // SurveyInvitation configuration
            var inviteEntity = modelBuilder.Entity<SurveyInvitation>();

            inviteEntity.ToTable("SurveyInvitations");
            inviteEntity.HasKey(e => e.Id);
            inviteEntity.Property(e => e.Id).ValueGeneratedOnAdd();

            inviteEntity.Property(e => e.Token)
                .IsRequired();

            inviteEntity.Property(e => e.ParticipantDataverseId)
                .IsRequired();

            inviteEntity.Property(e => e.ExpirationDate)
                .IsRequired();

            inviteEntity.Property(e => e.IsUsed)
                .IsRequired()
                .HasDefaultValue(false);

            inviteEntity.Property(e => e.LastPartialSave)
                .IsRequired(false);

            inviteEntity.Property(e => e.LastReminderSent)
                .IsRequired(false);

            inviteEntity.Property(e => e.ReminderCount)
                .IsRequired()
                .HasDefaultValue(0);

            // Nouveaux champs
            inviteEntity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            inviteEntity.Property(e => e.SyncStatus)
                .HasMaxLength(100)
                .IsRequired(false);

            // IntegrationLog configuration
            var logEntity = modelBuilder.Entity<IntegrationLog>();

            logEntity.ToTable("IntegrationLogs");
            logEntity.HasKey(e => e.Id);
            logEntity.Property(e => e.Id).ValueGeneratedOnAdd();

            logEntity.Property(e => e.EventDate)
                .IsRequired();

            logEntity.Property(e => e.EntityName)
                .IsRequired()
                .HasMaxLength(200);

            logEntity.Property(e => e.Action)
                .IsRequired()
                .HasMaxLength(100);

            logEntity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(50);

            logEntity.Property(e => e.Message)
                .IsRequired(false)
                .HasMaxLength(2000);
            modelBuilder.Entity<Survey>()
    .HasAlternateKey(s => s.DataverseId);

            // 2. On configure la relation dans la table Campaign
            modelBuilder.Entity<Campaign>(entity =>
            {
                entity.HasKey(e => e.DataverseId); // Clé primaire sur le Guid Dataverse

                entity.HasOne(c => c.Survey)
                    .WithMany()
                    .HasPrincipalKey(s => s.DataverseId) // La cible est le Guid de Survey
                    .HasForeignKey(c => c.SurveyDataverseId) // La source est le Guid dans Campaign
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}