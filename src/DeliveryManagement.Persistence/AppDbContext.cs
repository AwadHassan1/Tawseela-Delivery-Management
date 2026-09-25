using DeliveryManagement.Application;
using DeliveryManagement.Domain;
using Microsoft.EntityFrameworkCore;

namespace DeliveryManagement.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<DeliveryMan> DeliveryMen => Set<DeliveryMan>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<SettlementDetail> SettlementDetails => Set<SettlementDetail>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(x => x.UserName).IsUnique();
        b.Entity<Customer>().HasIndex(x => x.Phone);
        b.Entity<DeliveryMan>().HasIndex(x => x.Phone);
        b.Entity<Order>().HasIndex(x => x.OrderNumber).IsUnique();
        b.Entity<Order>().HasIndex(x => new { x.CreatedAt, x.Status, x.DeliveryManId });
        b.Entity<Invoice>().HasIndex(x => x.InvoiceNumber).IsUnique();
        b.Entity<Invoice>().HasIndex(x => x.OrderId).IsUnique();
        b.Entity<Order>().Property(x => x.OrderValue).HasPrecision(18,2);
        b.Entity<Order>().Property(x => x.DeliveryFee).HasPrecision(18,2);
        b.Entity<Order>().Property(x => x.CollectedAmount).HasPrecision(18,2);
        b.Entity<DeliveryMan>().Property(x => x.CommissionValue).HasPrecision(18,2);
        b.Entity<Expense>().Property(x => x.Amount).HasPrecision(18,2);
        foreach (var p in new[] { "OrdersValue", "DeliveryFees", "AmountCollected", "Commission", "ApprovedExpenses", "AmountDueToOffice", "AmountPaid", "Remaining" }) b.Entity<Settlement>().Property(p).HasPrecision(18,2);
        b.Entity<Settlement>().HasIndex(x => new { x.DeliveryManId, x.Date }).IsUnique();
        b.Entity<SettlementDetail>().HasIndex(x => new { x.SettlementId, x.OrderId }).IsUnique();
        b.Entity<AppSetting>().HasIndex(x => x.Key).IsUnique();
        b.Entity<Order>().HasOne(x => x.Customer).WithMany(x => x.Orders).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Order>().HasOne(x => x.DeliveryMan).WithMany(x => x.Orders).HasForeignKey(x => x.DeliveryManId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<OrderStatusHistory>().HasOne(x => x.Order).WithMany(x => x.StatusHistory).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Invoice>().HasOne(x => x.Order).WithOne().HasForeignKey<Invoice>(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Invoice>().Property(x => x.InvoiceNumber).HasMaxLength(100);
        b.Entity<Invoice>().Property(x => x.CustomerName).HasMaxLength(200);
        b.Entity<Invoice>().Property(x => x.CustomerPhone).HasMaxLength(30);
        b.Entity<Invoice>().Property(x => x.CustomerAddress).HasMaxLength(500);
        b.Entity<Invoice>().Property(x => x.DeliveryManName).HasMaxLength(200);
        b.Entity<Invoice>().Property(x => x.Notes).HasMaxLength(1000);
        b.Entity<Invoice>().Property(x => x.OrderValue).HasPrecision(18,2);
        b.Entity<Invoice>().Property(x => x.DeliveryFee).HasPrecision(18,2);
        b.Entity<Invoice>().Property(x => x.Total).HasPrecision(18,2);
        b.Entity<Invoice>().Property(x => x.CollectedAmount).HasPrecision(18,2);
    }
}

public sealed class EfRepository<T>(AppDbContext db) : IRepository<T> where T : class
{
    public IQueryable<T> Query() => db.Set<T>();
    public Task<T?> GetAsync(int id, CancellationToken ct = default) => db.Set<T>().FindAsync([id], ct).AsTask();
    public Task AddAsync(T entity, CancellationToken ct = default) => db.Set<T>().AddAsync(entity, ct).AsTask();
    public void Remove(T entity) => db.Set<T>().Remove(entity);
}

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(AppDbContext db, IPasswordService passwordService)
    {
        await db.Database.EnsureCreatedAsync();
        // Safe schema patch for existing databases created before Invoice was introduced.
        // It never drops or resets existing data.
        await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'Invoices', N'U') IS NULL
BEGIN
    CREATE TABLE Invoices (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Invoices PRIMARY KEY,
        InvoiceNumber NVARCHAR(100) NOT NULL,
        OrderId INT NOT NULL,
        CustomerName NVARCHAR(200) NOT NULL,
        CustomerPhone NVARCHAR(30) NOT NULL,
        CustomerAddress NVARCHAR(500) NOT NULL,
        Date DATETIME2 NOT NULL,
        OrderType INT NOT NULL,
        OrderValue DECIMAL(18,2) NOT NULL,
        DeliveryFee DECIMAL(18,2) NOT NULL,
        Total DECIMAL(18,2) NOT NULL,
        OrderStatus INT NOT NULL,
        DeliveryManName NVARCHAR(200) NOT NULL,
        CollectedAmount DECIMAL(18,2) NOT NULL,
        IsCollected BIT NOT NULL,
        Notes NVARCHAR(1000) NOT NULL,
        CONSTRAINT FK_Invoices_Orders FOREIGN KEY(OrderId) REFERENCES Orders(Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX IX_Invoices_InvoiceNumber ON Invoices(InvoiceNumber);
    CREATE UNIQUE INDEX IX_Invoices_OrderId ON Invoices(OrderId);
END");
        if (!await db.Users.AnyAsync())
        {
            var initialAdminPassword =
                Environment.GetEnvironmentVariable("TAWSEELA_INITIAL_ADMIN_PASSWORD");

            if (string.IsNullOrWhiteSpace(initialAdminPassword))
            {
                throw new InvalidOperationException(
                    "TAWSEELA_INITIAL_ADMIN_PASSWORD environment variable is required to create the initial admin account.");
            }

            db.Users.Add(new User
            {
                UserName = "admin",
                DisplayName = "مدير النظام",
                PasswordHash = passwordService.Hash(initialAdminPassword),
                Role = UserRole.Admin,
                MustChangePassword = true
            });

            db.Settings.AddRange(
                new AppSetting { Key = "CompanyName", Value = "توصيله" },
                new AppSetting { Key = "Currency", Value = "جنيه" },
                new AppSetting { Key = "DateFormat", Value = "yyyy-MM-dd" });

            await db.SaveChangesAsync();
        }
    }
}
