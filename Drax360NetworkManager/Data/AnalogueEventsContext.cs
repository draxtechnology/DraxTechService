using Microsoft.EntityFrameworkCore;

namespace DraxTechnology.Data
{
    // Per-panel DbContext following the EspaEventsContext template.
    public class AnalogueEventsContext : DbContext
    {
        private readonly string _dbPath;

        public AnalogueEventsContext(string dbPath)
        {
            _dbPath = dbPath;
        }

        public DbSet<AnalogueEvent> AnalogueEvents { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseSqlite("Data Source=" + _dbPath);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AnalogueEvent>(b =>
            {
                b.ToTable("AnalogueEvents");
                b.HasKey(e => e.Id);
                b.Property(e => e.Id).ValueGeneratedOnAdd();
                b.Property(e => e.Node).IsRequired();
                b.Property(e => e.Address).IsRequired();
                b.Property(e => e.Value).IsRequired();
                b.Property(e => e.DateCreated)
                    .IsRequired()
                    .HasDefaultValueSql("datetime('now', 'localtime')");
            });
        }
    }
}
