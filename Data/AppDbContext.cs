using Microsoft.EntityFrameworkCore;
using Xrmbox.VoC.Portal.Models.Local;

namespace Xrmbox.VoC.Portal.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }
        public DbSet<SurveyInvitation> SurveyInvitations { get; set; }

        public DbSet<LocalResponse> LocalResponses { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<LocalResponse>();

            entity.ToTable("LocalResponses");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.SurveyId).IsRequired();
            entity.Property(e => e.ParticipantId).IsRequired(false);
            entity.Property(e => e.CampagneId).IsRequired(false);

            entity.Property(e => e.ResponseJson).IsRequired();
            entity.Property(e => e.SubmittedAt).IsRequired();

            entity.Property(e => e.IsSynced)
                .IsRequired()
                .HasDefaultValue(false);

            entity.Property(e => e.DataverseId)
                .HasMaxLength(50)
                .IsRequired(false);

            entity.Property(e => e.SyncError)
                .HasMaxLength(2000)
                .IsRequired(false);
        }
    }
}