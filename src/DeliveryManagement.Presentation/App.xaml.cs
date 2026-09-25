using System.IO;
using System.Windows;
using DeliveryManagement.Application;
using DeliveryManagement.Infrastructure;
using DeliveryManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace DeliveryManagement.Presentation;

public partial class App : System.Windows.Application
{
    public static IHost Host { get; private set; } = null!;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tawseela");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(Path.Combine(dataDir, "logs"));
        Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.File(Path.Combine(dataDir, "logs", "tawseela-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30).CreateLogger();
        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                var cs = Environment.GetEnvironmentVariable("TAWSEELA_CONNECTION") ?? "Server=DESKTOP-SMDFSSF;Database=TawseelaDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
                services.AddDbContext<AppDbContext>(o => o.UseSqlServer(cs));
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
                services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
                services.AddSingleton<IPasswordService, PasswordService>();
                services.AddScoped<IAuditService, AuditService>();
                services.AddScoped<IOrderService, OrderService>();
                services.AddScoped<ISettlementService, SettlementService>();
                services.AddScoped<IInvoiceService, InvoiceService>();
                services.AddScoped<IDriverService, DriverService>();
                services.AddTransient<LoginWindow>();
                services.AddTransient<MainWindow>();
                services.AddTransient<MainViewModel>();
            }).Build();
        await Host.StartAsync();
        using var scope = Host.Services.CreateScope();
        await DatabaseInitializer.InitializeAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider.GetRequiredService<IPasswordService>());
        var login = Host.Services.GetRequiredService<LoginWindow>();
        login.Show();
    }
    protected override async void OnExit(ExitEventArgs e)
    {
        await Host.StopAsync(); Host.Dispose(); Log.CloseAndFlush(); base.OnExit(e);
    }
}
