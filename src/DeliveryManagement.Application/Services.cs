using DeliveryManagement.Domain;

namespace DeliveryManagement.Application;

public interface IRepository<T> where T : class
{
    IQueryable<T> Query();
    Task<T?> GetAsync(int id, CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Remove(T entity);
}

public interface IUnitOfWork { Task<int> SaveChangesAsync(CancellationToken ct = default); }
public interface IPasswordService { string Hash(string password); bool Verify(string password, string hash); }
public interface IAuditService { Task WriteAsync(string action, string entity, string entityId, string description, int? userId = null); }
public interface IOrderService
{
    Task<List<Order>> GetOrdersAsync(string? search = null, OrderStatus? status = null, OrderType? type = null, int? driverId = null, DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 50);
    Task<Order> CreateAsync(Order order, int userId);
    Task ChangeStatusAsync(int orderId, OrderStatus status, int userId, bool isAdmin);
}
public interface ISettlementService
{
    Task<Settlement?> GetByDriverAndDateAsync(int driverId, DateTime date, CancellationToken ct = default);
    Task<SettlementCalculation> PreviewAsync(int driverId, DateTime date, decimal paid, CancellationToken ct = default);
    Task<Settlement> CreateAsync(int driverId, DateTime date, decimal paid, string notes, int userId, CancellationToken ct = default);
}
public interface IInvoiceService
{
    Task<Invoice> GetOrCreateForOrderAsync(int orderId, CancellationToken ct = default);
}
public sealed record DriverStatistics(
    int DriverId, string Name, string Phone, string Address, DateTime StartDate,
    CommissionType CommissionType, decimal CommissionValue, bool IsActive,
    int TotalOrders, int DeliveredOrders, int OutForDeliveryOrders, int AssignedOrders,
    int FailedOrders, int ReturnedOrders, int CancelledOrders,
    decimal OrdersValue, decimal DeliveryFees, decimal CollectedAmount,
    decimal Commission, decimal Expenses, decimal AmountDueToOffice);
public interface IDriverService
{
    Task<List<DriverStatistics>> GetStatisticsAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    Task<DriverStatistics?> GetStatisticsAsync(int driverId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    Task<List<Order>> GetOrdersAsync(int driverId, OrderStatus? status = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
}
