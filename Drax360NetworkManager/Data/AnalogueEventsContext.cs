using System;
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

        // EnsureCreated is a no-op on an existing database, so columns added
        // after a db is already in the field have to be bolted on by hand —
        // cheap raw SQL rather than the EF Migrations infrastructure, same
        // trade the Espa legacy migrator makes.
        public void EnsureReady()
        {
            Database.EnsureCreated();

            var conn = Database.GetDbConnection();
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('AnalogueEvents') WHERE name = 'Loop'";
                bool hasLoop = Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                if (!hasLoop)
                {
                    cmd.CommandText = "ALTER TABLE \"AnalogueEvents\" ADD COLUMN \"Loop\" INTEGER NOT NULL DEFAULT 0";
                    cmd.ExecuteNonQuery();
                }
            }
            conn.Close();
        }

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
                b.Property(e => e.Loop).IsRequired();
                b.Property(e => e.Address).IsRequired();
                b.Property(e => e.Value).IsRequired();
                b.Property(e => e.DateCreated)
                    .IsRequired()
                    .HasDefaultValueSql("datetime('now', 'localtime')");
            });
        }
    }
}
