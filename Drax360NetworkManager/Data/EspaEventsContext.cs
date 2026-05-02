using Microsoft.EntityFrameworkCore;

namespace DraxTechnology.Data
{
    // Template for per-panel DbContexts. Each panel that wants its own
    // SQLite-backed store should create a sibling context here (e.g.
    // RsmEventsContext) following the same pattern: constructor takes the
    // path, OnConfiguring wires UseSqlite, OnModelCreating maps the entity.
    // EF Core + SQLite are project-level packages so every panel has access.
    public class EspaEventsContext : DbContext
    {
        private readonly string _dbPath;

        public EspaEventsContext(string dbPath)
        {
            _dbPath = dbPath;
        }

        public DbSet<EspaEvent> Events { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseSqlite("Data Source=" + _dbPath);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EspaEvent>(b =>
            {
                b.ToTable("Events");
                b.HasKey(e => e.Id);
                b.Property(e => e.Id).ValueGeneratedOnAdd();
                b.Property(e => e.Node).IsRequired();
                b.Property(e => e.Loop).IsRequired();
                b.Property(e => e.Device).IsRequired();
                b.Property(e => e.Name).IsRequired();
                b.HasIndex(e => e.Name).IsUnique();
            });
        }
    }
}
