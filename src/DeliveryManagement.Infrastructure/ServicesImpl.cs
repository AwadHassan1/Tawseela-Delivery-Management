using DeliveryManagement.Application;
using DeliveryManagement.Domain;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace DeliveryManagement.Infrastructure;

public sealed class PasswordService : IPasswordService
{
    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(12));
    public bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);
}

public sealed class AuditService(IRepository<AuditLog> repository, IUnitOfWork uow) : IAuditService
{
    public async Task WriteAsync(string action, string entity, string entityId, string description, int? userId = null)
    {
        await repository.AddAsync(new AuditLog { Action = action, Entity = entity, EntityId = entityId, Description = description, UserId = userId });
        await uow.SaveChangesAsync();
    }
}

public sealed class OrderService(IRepository<Order> orders, IRepository<OrderStatusHistory> history, IUnitOfWork uow, IAuditService audit) : IOrderService
{
    public async Task<List<Order>> GetOrdersAsync(string? search = null, OrderStatus? status = null, OrderType? type = null, int? driverId = null, DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 50)
    {
        var q = orders.Query().AsNoTracking().Include(o => o.Customer).Include(o => o.DeliveryMan).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(o => o.OrderNumber.Contains(search) || o.Customer.Name.Contains(search) || o.PhoneSnapshot.Contains(search) || (o.DeliveryMan != null && o.DeliveryMan.Name.Contains(search)));
        if (status.HasValue) q = q.Where(o => o.Status == status);
        if (type.HasValue) q = q.Where(o => o.Type == type);
        if (driverId.HasValue) q = q.Where(o => o.DeliveryManId == driverId);
        if (from.HasValue) q = q.Where(o => o.CreatedAt >= from.Value.Date);
        if (to.HasValue) q = q.Where(o => o.CreatedAt < to.Value.Date.AddDays(1));
        return await q.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
    }

    public async Task<Order> CreateAsync(Order order, int userId)
    {
        if (order.OrderValue < 0 || order.DeliveryFee < 0) throw new ArgumentException("القيم المالية لا يمكن أن تكون سالبة.");
        if (order.CustomerId <= 0) throw new ArgumentException("العميل مطلوب.");
        // The UI supplies foreign-key IDs. Do not attach detached Customer/DeliveryMan navigation objects
        // to this DbContext; doing so can cause EF Core tracking conflicts or unintended INSERTs.
        order.Customer = null!;
        order.DeliveryMan = null;
        order.OrderNumber = $"TW-{DateTime.Now:yyyyMMddHHmmss}-{Random.Shared.Next(100,999)}";
        order.CreatedAt = DateTime.Now; order.UpdatedAt = order.CreatedAt;
        order.CollectedAmount = order.Status == OrderStatus.Delivered ? order.TotalAmount : 0m;
        await orders.AddAsync(order);
        await uow.SaveChangesAsync();
        await history.AddAsync(new OrderStatusHistory { OrderId = order.Id, Status = order.Status, ChangedByUserId = userId, ChangedAt = DateTime.Now, Note = "إنشاء الأوردر" });
        await uow.SaveChangesAsync();
        await audit.WriteAsync("Add Order", nameof(Order), order.Id.ToString(), $"تم إنشاء الأوردر {order.OrderNumber}", userId);
        return order;
    }

    public async Task ChangeStatusAsync(int orderId, OrderStatus status, int userId, bool isAdmin)
    {
        var order = await orders.GetAsync(orderId) ?? throw new KeyNotFoundException("الأوردر غير موجود.");
        if (!FinancialRules.CanTransition(order.Status, status, isAdmin)) throw new InvalidOperationException("انتقال الحالة غير مسموح.");
        order.Status = status; order.UpdatedAt = DateTime.Now;
        if (status == OrderStatus.Delivered) order.CollectedAmount = order.TotalAmount;
        if (status is OrderStatus.Cancelled or OrderStatus.Returned or OrderStatus.Failed) order.CollectedAmount = 0m;
        await history.AddAsync(new OrderStatusHistory { OrderId = order.Id, Status = status, ChangedByUserId = userId, ChangedAt = DateTime.Now });
        await uow.SaveChangesAsync();
        await audit.WriteAsync("Change Order Status", nameof(Order), order.Id.ToString(), $"الحالة: {status}", userId);
    }
}

public sealed class SettlementService(IRepository<DeliveryMan> drivers, IRepository<Order> orders, IRepository<Expense> expenses, IRepository<Settlement> settlements, IRepository<SettlementDetail> details, IUnitOfWork uow, IAuditService audit) : ISettlementService
{
    public async Task<Settlement?> GetByDriverAndDateAsync(int driverId, DateTime date, CancellationToken ct = default)
        => await settlements.Query().AsNoTracking()
            .FirstOrDefaultAsync(s => s.DeliveryManId == driverId && s.Date.Date == date.Date, ct);

    public async Task<SettlementCalculation> PreviewAsync(int driverId, DateTime date, decimal paid, CancellationToken ct = default)
    {
        var driver = await drivers.GetAsync(driverId, ct) ?? throw new KeyNotFoundException("المندوب غير موجود.");
        var existing = await GetByDriverAndDateAsync(driverId, date, ct);

        // Previewing an already-closed settlement is intentionally read-only:
        // return the values that were actually saved instead of recalculating them.
        if (existing is not null)
            return new(existing.OrdersValue, existing.DeliveryFees, existing.AmountCollected, existing.Commission, existing.ApprovedExpenses, existing.AmountDueToOffice, existing.Remaining);

        var dayOrders = await orders.Query().AsNoTracking().Where(o => o.DeliveryManId == driverId && o.CreatedAt.Date == date.Date).ToListAsync(ct);
        var exp = await expenses.Query().AsNoTracking().Where(e => e.DeliveryManId == driverId && e.Date.Date == date.Date && e.Approved).SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
        return FinancialRules.CalculateSettlement(driver, dayOrders, exp, paid);
    }

    public async Task<Settlement> CreateAsync(int driverId, DateTime date, decimal paid, string notes, int userId, CancellationToken ct = default)
    {
        var calc = await PreviewAsync(driverId, date, paid, ct);
        if (paid > calc.Due)
        {
            throw new InvalidOperationException(
                $"المبلغ المدفوع لا يمكن أن يكون أكبر من المستحق للمكتب ({calc.Due:N2} جنيه).");
        }
        if (paid < 0) throw new ArgumentException("المبلغ المدفوع لا يمكن أن يكون سالبًا.");
        var existing = await settlements.Query().AnyAsync(s => s.DeliveryManId == driverId && s.Date.Date == date.Date, ct);
        if (existing) throw new InvalidOperationException("توجد تسوية لهذا المندوب والتاريخ بالفعل.");
        var settlement = new Settlement { DeliveryManId = driverId, Date = date.Date, OrdersValue = calc.OrdersValue, DeliveryFees = calc.DeliveryFees, AmountCollected = calc.Collected, Commission = calc.Commission, ApprovedExpenses = calc.Expenses, AmountDueToOffice = calc.Due, AmountPaid = paid, Remaining = calc.Remaining, IsClosed = true, Notes = notes, CreatedAt = DateTime.Now };
        await settlements.AddAsync(settlement); await uow.SaveChangesAsync();
        var dayOrders = await orders.Query().AsNoTracking().Where(o => o.DeliveryManId == driverId && o.CreatedAt.Date == date.Date && o.Status == OrderStatus.Delivered).ToListAsync(ct);
        foreach (var order in dayOrders) await details.AddAsync(new SettlementDetail { SettlementId = settlement.Id, OrderId = order.Id, CollectedAmount = order.CollectedAmount });
        await uow.SaveChangesAsync();
        await audit.WriteAsync("Settlement", nameof(Settlement), settlement.Id.ToString(), $"تمت تسوية المندوب {driverId} عن {date:yyyy-MM-dd}", userId);
        return settlement;
    }
}

public sealed class InvoiceService(IRepository<Invoice> invoices, IRepository<Order> orders, IUnitOfWork uow) : IInvoiceService
{
    public async Task<Invoice> GetOrCreateForOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await orders.Query().Include(x => x.Customer).Include(x => x.DeliveryMan).FirstOrDefaultAsync(x => x.Id == orderId, ct)
            ?? throw new KeyNotFoundException("الأوردر غير موجود.");
        var invoice = await invoices.Query().FirstOrDefaultAsync(x => x.OrderId == orderId, ct);
        if (invoice is null)
        {
            invoice = new Invoice { OrderId = order.Id, InvoiceNumber = $"INV-{DateTime.Now:yyyyMMddHHmmss}-{Random.Shared.Next(100,999)}" };
            await invoices.AddAsync(invoice, ct);
        }
        invoice.CustomerName = order.Customer.Name;
        invoice.CustomerPhone = order.PhoneSnapshot;
        invoice.CustomerAddress = order.Address;
        invoice.Date = order.UpdatedAt;
        invoice.OrderType = order.Type;
        invoice.OrderValue = order.OrderValue;
        invoice.DeliveryFee = order.DeliveryFee;
        invoice.Total = order.TotalAmount;
        invoice.OrderStatus = order.Status;
        invoice.DeliveryManName = order.DeliveryMan?.Name ?? "غير محدد";
        invoice.CollectedAmount = order.CollectedAmount;
        invoice.IsCollected = order.Status == OrderStatus.Delivered && order.CollectedAmount >= order.TotalAmount;
        invoice.Notes = order.Notes;
        await uow.SaveChangesAsync(ct);
        return invoice;
    }
}

public sealed class DriverService(IRepository<DeliveryMan> drivers, IRepository<Order> orders, IRepository<Expense> expenses) : IDriverService
{
    public async Task<List<DriverStatistics>> GetStatisticsAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var result = new List<DriverStatistics>();
        var allDrivers = await drivers.Query().AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        foreach (var d in allDrivers) result.Add(await BuildAsync(d, from, to, ct));
        return result;
    }

    public async Task<DriverStatistics?> GetStatisticsAsync(int driverId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var d = await drivers.Query().AsNoTracking().FirstOrDefaultAsync(x => x.Id == driverId, ct);
        return d is null ? null : await BuildAsync(d, from, to, ct);
    }

    public async Task<List<Order>> GetOrdersAsync(int driverId, OrderStatus? status = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var q = orders.Query().AsNoTracking().Include(x => x.Customer).Include(x => x.DeliveryMan).Where(x => x.DeliveryManId == driverId);
        if (status.HasValue) q = q.Where(x => x.Status == status.Value);
        if (from.HasValue) q = q.Where(x => x.CreatedAt >= from.Value.Date);
        if (to.HasValue) q = q.Where(x => x.CreatedAt < to.Value.Date.AddDays(1));
        return await q.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    }

    private async Task<DriverStatistics> BuildAsync(DeliveryMan d, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var q = orders.Query().AsNoTracking().Where(x => x.DeliveryManId == d.Id);
        if (from.HasValue) q = q.Where(x => x.CreatedAt >= from.Value.Date);
        if (to.HasValue) q = q.Where(x => x.CreatedAt < to.Value.Date.AddDays(1));
        var list = await q.ToListAsync(ct);
        var delivered = list.Where(x => x.Status == OrderStatus.Delivered).ToList();
        var expQ = expenses.Query().AsNoTracking().Where(x => x.DeliveryManId == d.Id && x.Approved);
        if (from.HasValue) expQ = expQ.Where(x => x.Date >= from.Value.Date);
        if (to.HasValue) expQ = expQ.Where(x => x.Date < to.Value.Date.AddDays(1));
        var exp = await expQ.SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var commission = FinancialRules.CalculateCommission(d, delivered.Count, delivered.Sum(x => x.OrderValue), delivered.Sum(x => x.DeliveryFee));
        var collected = delivered.Sum(x => x.CollectedAmount);
        return new DriverStatistics(d.Id,d.Name,d.Phone,d.Address,d.StartDate,d.CommissionType,d.CommissionValue,d.IsActive,
            list.Count, delivered.Count, list.Count(x=>x.Status==OrderStatus.OutForDelivery), list.Count(x=>x.Status==OrderStatus.Assigned),
            list.Count(x=>x.Status==OrderStatus.Failed), list.Count(x=>x.Status==OrderStatus.Returned), list.Count(x=>x.Status==OrderStatus.Cancelled),
            list.Sum(x=>x.OrderValue), list.Sum(x=>x.DeliveryFee), collected, commission, exp, collected-commission-exp);
    }
}
