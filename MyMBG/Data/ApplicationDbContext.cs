using Microsoft.EntityFrameworkCore;

namespace MyMBG.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Add your DbSets here
    // public DbSet<User> Users { get; set; }
    // public DbSet<Product> Products { get; set; }
    // public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Add your entity configurations here
    }
}
