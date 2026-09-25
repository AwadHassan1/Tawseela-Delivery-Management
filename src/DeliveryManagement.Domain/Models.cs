namespace DeliveryManagement.Domain;

public enum OrderStatus { New, Assigned, OutForDelivery, Delivered, Failed, Returned, Cancelled }
public enum OrderType { Parcel, Food, Documents, Requests, Shipping, Other }
public enum CommissionType { None, FixedPerOrder, PercentageOfDeliveryFee, PercentageOfOrderValue }
public enum UserRole { Admin, Employee, Accountant }

public sealed class User
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string DefaultAddress { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

public sealed class DeliveryMan
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public DateTime StartDate { get; set; } = DateTime.Today;
    public bool IsActive { get; set; } = true;
    public CommissionType CommissionType { get; set; }
    public decimal CommissionValue { get; set; }
    public string Notes { get; set; } = "";
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

public sealed class Order
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public string PhoneSnapshot { get; set; } = "";
    public string Address { get; set; } = "";
    public OrderType Type { get; set; }
    public decimal OrderValue { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal TotalAmount => OrderValue + DeliveryFee;
    public int? DeliveryManId { get; set; }
    public DeliveryMan? DeliveryMan { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.New;
    public decimal CollectedAmount { get; set; }
    public string Notes { get; set; } = "";
    public ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();
}

public sealed class Invoice
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public string CustomerName { get; set; } = "";
    public string CustomerPhone { get; set; } = "";
    public string CustomerAddress { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Now;
    public OrderType OrderType { get; set; }
    public decimal OrderValue { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal Total { get; set; }
    public OrderStatus OrderStatus { get; set; }
    public string DeliveryManName { get; set; } = "";
    public decimal CollectedAmount { get; set; }
    public bool IsCollected { get; set; }
    public string PaymentStatus => IsCollected ? "تم التحصيل" : "غير محصل";
    public string Notes { get; set; } = "";
}

public sealed class OrderStatusHistory
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public OrderStatus Status { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.Now;
    public int? ChangedByUserId { get; set; }
    public string Note { get; set; } = "";
}

public sealed class Expense
{
    public int Id { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public string Type { get; set; } = "";
    public decimal Amount { get; set; }
    public int? DeliveryManId { get; set; }
    public string Description { get; set; } = "";
    public int CreatedByUserId { get; set; }
    public bool Approved { get; set; } = true;
}

public sealed class Settlement
{
    public int Id { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public int DeliveryManId { get; set; }
    public decimal OrdersValue { get; set; }
    public decimal DeliveryFees { get; set; }
    public decimal AmountCollected { get; set; }
    public decimal Commission { get; set; }
    public decimal ApprovedExpenses { get; set; }
    public decimal AmountDueToOffice { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Remaining { get; set; }
    public bool IsClosed { get; set; }
    public string Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public ICollection<SettlementDetail> Details { get; set; } = new List<SettlementDetail>();
}

public sealed class SettlementDetail
{
    public int Id { get; set; }
    public int SettlementId { get; set; }
    public int OrderId { get; set; }
    public decimal CollectedAmount { get; set; }
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string Action { get; set; } = "";
    public DateTime DateTime { get; set; } = DateTime.Now;
    public string Entity { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class AppSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed record SettlementCalculation(decimal OrdersValue, decimal DeliveryFees, decimal Collected, decimal Commission, decimal Expenses, decimal Due, decimal Remaining);

public static class FinancialRules
{
    public static decimal CalculateCommission(DeliveryMan driver, int deliveredOrders, decimal orderValue, decimal deliveryFees) => driver.CommissionType switch
    {
        CommissionType.FixedPerOrder => driver.CommissionValue * deliveredOrders,
        CommissionType.PercentageOfDeliveryFee => deliveryFees * driver.CommissionValue / 100m,
        CommissionType.PercentageOfOrderValue => orderValue * driver.CommissionValue / 100m,
        _ => 0m
    };

    public static SettlementCalculation CalculateSettlement(DeliveryMan driver, IEnumerable<Order> orders, decimal approvedExpenses, decimal amountPaid)
    {
        var delivered = orders.Where(o => o.Status == OrderStatus.Delivered).ToList();
        var orderValue = delivered.Sum(o => o.OrderValue);
        var deliveryFees = delivered.Sum(o => o.DeliveryFee);
        var collected = delivered.Sum(o => o.CollectedAmount);
        var commission = CalculateCommission(driver, delivered.Count, orderValue, deliveryFees);
        var due = collected - commission - approvedExpenses;
        var remaining = due - amountPaid;
        return new(orderValue, deliveryFees, collected, commission, approvedExpenses, due, remaining);
    }

    public static bool CanTransition(OrderStatus from, OrderStatus to, bool isAdmin = false)
    {
        if (from == OrderStatus.Cancelled) return isAdmin && to == OrderStatus.New;
        return from switch
        {
            OrderStatus.New => to is OrderStatus.Assigned or OrderStatus.Cancelled,
            OrderStatus.Assigned => to is OrderStatus.OutForDelivery or OrderStatus.Cancelled,
            OrderStatus.OutForDelivery => to is OrderStatus.Delivered or OrderStatus.Failed or OrderStatus.Cancelled,
            OrderStatus.Failed => to == OrderStatus.Returned || to == OrderStatus.OutForDelivery,
            OrderStatus.Returned => isAdmin && to == OrderStatus.New,
            OrderStatus.Delivered => false,
            _ => false
        };
    }
}
