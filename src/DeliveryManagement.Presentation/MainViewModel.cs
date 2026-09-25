using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeliveryManagement.Application;
using DeliveryManagement.Domain;
using DeliveryManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace DeliveryManagement.Presentation;

public partial class MainViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly IOrderService _orders;
    private readonly ISettlementService _settlements;
    private readonly IInvoiceService _invoices;
    private readonly IDriverService _driverService;

    [ObservableProperty] private string pageTitle = "الرئيسية";
    [ObservableProperty] private object? currentPage;
    [ObservableProperty] private string currentUserName = "";
    [ObservableProperty] private string todayText = DateTime.Now.ToString("dddd، dd MMMM yyyy", new CultureInfo("ar-EG"));
    [ObservableProperty] private int todayOrders;
    [ObservableProperty] private int deliveredOrders;
    [ObservableProperty] private int returnedOrders;
    [ObservableProperty] private decimal todayRevenue;
    [ObservableProperty] private decimal todayFees;
    [ObservableProperty] private decimal todayCollected;
    [ObservableProperty] private ObservableCollection<Order> latestOrders = new();
    [ObservableProperty] private ObservableCollection<DeliveryMan> drivers = new();
    [ObservableProperty] private ObservableCollection<DriverStatistics> driverStatistics = new();
    [ObservableProperty] private ObservableCollection<Customer> customers = new();
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string customerName = "";
    [ObservableProperty] private string customerPhone = "";
    [ObservableProperty] private string customerAddress = "";
    [ObservableProperty] private OrderType selectedType = OrderType.Parcel;
    [ObservableProperty] private decimal orderValue;
    [ObservableProperty] private decimal deliveryFee;
    [ObservableProperty] private DeliveryMan? selectedDriver;
    [ObservableProperty] private OrderStatus selectedStatus = OrderStatus.New;
    [ObservableProperty] private string orderNotes = "";
    [ObservableProperty] private string statusMessage = "";

    public event Action<Invoice>? InvoiceRequested;
    public decimal TotalOrderAmount => OrderValue + DeliveryFee;

    public ObservableCollection<Choice<OrderType>> OrderTypeOptions { get; } = new()
    {
        new("طرد", OrderType.Parcel), new("مأكولات", OrderType.Food), new("مستندات", OrderType.Documents),
        new("طلبات", OrderType.Requests), new("شحن", OrderType.Shipping), new("أخرى", OrderType.Other)
    };
    public ObservableCollection<Choice<OrderStatus>> OrderStatusOptions { get; } = new()
    {
        new("جديد", OrderStatus.New), new("مع المندوب", OrderStatus.Assigned), new("في الطريق", OrderStatus.OutForDelivery),
        new("تم التسليم", OrderStatus.Delivered), new("لم يتم التسليم", OrderStatus.Failed), new("مرتجع", OrderStatus.Returned), new("ملغي", OrderStatus.Cancelled)
    };
    public User CurrentUser { get; private set; } = null!;

    public MainViewModel(AppDbContext db, IOrderService orders, ISettlementService settlements, IInvoiceService invoices, IDriverService driverService)
    { _db = db; _orders = orders; _settlements = settlements; _invoices = invoices; _driverService = driverService; }

    public void Initialize(User user)
    {
        CurrentUser = user; CurrentUserName = user.DisplayName; CurrentPage = new DashboardPageVm(this); _ = LoadDashboardAsync();
    }

    partial void OnOrderValueChanged(decimal value) => OnPropertyChanged(nameof(TotalOrderAmount));
    partial void OnDeliveryFeeChanged(decimal value) => OnPropertyChanged(nameof(TotalOrderAmount));

    [RelayCommand]
    private async Task Navigate(string page)
    {
        PageTitle = page; StatusMessage = "";
        switch (page)
        {
            case "الرئيسية": CurrentPage = new DashboardPageVm(this); await LoadDashboardAsync(); break;
            case "الأوردرات": CurrentPage = new OrdersPageVm(this); await LoadDashboardAsync(); break;
            case "إضافة أوردر": await LoadDriversAsync(); CurrentPage = new AddOrderPageVm(this); break;
            case "المناديب": await LoadDriverStatisticsAsync(); CurrentPage = new DriversPageVm(this); break;
            case "العملاء": await LoadCustomersAsync(); CurrentPage = new CustomersPageVm(this); break;
            case "التسويات المالية": await LoadDriversAsync(); CurrentPage = new SettlementPageVm(this, _settlements, _driverService); break;
            default: CurrentPage = new InfoPageVm(page); break;
        }
    }

    public async Task LoadDashboardAsync()
    {
        var start = DateTime.Today; var end = start.AddDays(1);
        await LoadTodayDriverStatisticsAsync();
        var q = _db.Orders.AsNoTracking().Where(o => o.CreatedAt >= start && o.CreatedAt < end);
        TodayOrders = await q.CountAsync(); DeliveredOrders = await q.CountAsync(o => o.Status == OrderStatus.Delivered); ReturnedOrders = await q.CountAsync(o => o.Status == OrderStatus.Returned);
        TodayRevenue = await q.SumAsync(o => (decimal?)o.OrderValue) ?? 0m; TodayFees = await q.SumAsync(o => (decimal?)o.DeliveryFee) ?? 0m;
        TodayCollected = await q.Where(o => o.Status == OrderStatus.Delivered).SumAsync(o => (decimal?)o.CollectedAmount) ?? 0m;
        var search = SearchText.Trim(); var oq = _db.Orders.AsNoTracking().Include(o => o.Customer).Include(o => o.DeliveryMan).OrderByDescending(o => o.CreatedAt).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) oq = oq.Where(o => o.OrderNumber.Contains(search) || o.PhoneSnapshot.Contains(search) || o.Customer.Name.Contains(search) || (o.DeliveryMan != null && o.DeliveryMan.Name.Contains(search))).OrderByDescending(o => o.CreatedAt);
        LatestOrders = new ObservableCollection<Order>(await oq.Take(100).ToListAsync());
    }
    public async Task LoadDriversAsync() => Drivers = new ObservableCollection<DeliveryMan>(await _db.DeliveryMen.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync());
    public async Task LoadDriverStatisticsAsync() => DriverStatistics = new ObservableCollection<DriverStatistics>(await _driverService.GetStatisticsAsync());
    public async Task LoadTodayDriverStatisticsAsync() => DriverStatistics = new ObservableCollection<DriverStatistics>(await _driverService.GetStatisticsAsync(DateTime.Today, DateTime.Today));
    public async Task LoadCustomersAsync() => Customers = new ObservableCollection<Customer>(await _db.Customers.AsNoTracking().Include(x => x.Orders).OrderByDescending(x => x.CreatedAt).ToListAsync());

    [RelayCommand]
    private async Task SaveOrder()
    {
        try
        {
            CustomerName = CustomerName.Trim(); CustomerPhone = CustomerPhone.Trim(); CustomerAddress = CustomerAddress.Trim();
            if (string.IsNullOrWhiteSpace(CustomerName)) throw new InvalidOperationException("يرجى إدخال اسم العميل.");
            if (string.IsNullOrWhiteSpace(CustomerPhone) || CustomerPhone.Length < 8) throw new InvalidOperationException("يرجى إدخال رقم هاتف صحيح.");
            if (OrderValue < 0 || DeliveryFee < 0) throw new InvalidOperationException("القيم المالية لا يمكن أن تكون سالبة.");
            var customer = await _db.Customers.FirstOrDefaultAsync(x => x.Phone == CustomerPhone);
            if (customer is null) { customer = new Customer { Name = CustomerName, Phone = CustomerPhone, DefaultAddress = CustomerAddress }; _db.Customers.Add(customer); await _db.SaveChangesAsync(); }
            else { customer.Name = CustomerName; customer.DefaultAddress = CustomerAddress; }
            var order = new Order { CustomerId = customer.Id, Customer = null!, PhoneSnapshot = customer.Phone, Address = CustomerAddress, Type = SelectedType, OrderValue = OrderValue, DeliveryFee = DeliveryFee, DeliveryManId = SelectedDriver?.Id, DeliveryMan = null, Status = SelectedStatus, CollectedAmount = SelectedStatus == OrderStatus.Delivered ? TotalOrderAmount : 0m, Notes = OrderNotes.Trim() };
            await _orders.CreateAsync(order, CurrentUser.Id); StatusMessage = $"تم حفظ الأوردر {order.OrderNumber} بنجاح"; ClearOrder(); await LoadDashboardAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; Log.Error(ex, "Create order failed"); }
    }
    [RelayCommand] private void ClearOrder() { CustomerName = CustomerPhone = CustomerAddress = OrderNotes = ""; OrderValue = DeliveryFee = 0m; SelectedDriver = null; SelectedType = OrderType.Parcel; SelectedStatus = OrderStatus.New; }
    public async Task ChangeStatus(Order order, OrderStatus status) { try { await _orders.ChangeStatusAsync(order.Id, status, CurrentUser.Id, CurrentUser.Role == UserRole.Admin); StatusMessage = "تم تحديث حالة الأوردر"; await LoadDashboardAsync(); } catch (Exception ex) { StatusMessage = ex.Message; Log.Error(ex, "Change order status failed"); } }

    [RelayCommand]
    private async Task ShowInvoice(Order order)
    {
        try { InvoiceRequested?.Invoke(await _invoices.GetOrCreateForOrderAsync(order.Id)); }
        catch (Exception ex) { StatusMessage = ex.Message; Log.Error(ex, "Open invoice failed"); }
    }

    [RelayCommand]
    private async Task OpenDriverDetails(DriverStatistics driver)
    {
        CurrentPage = new DriverDetailsPageVm(this, _driverService, driver); PageTitle = "تفاصيل المندوب"; await ((DriverDetailsPageVm)CurrentPage).LoadAsync();
    }
}

public partial class SettlementPageVm : ObservableObject
{
    private readonly MainViewModel _parent;
    private readonly ISettlementService _service;
    private readonly IDriverService _driverService;

    [ObservableProperty] private DeliveryMan? selectedDriver;
    [ObservableProperty] private DateTime selectedSettlementDate = DateTime.Today;
    [ObservableProperty] private decimal paidAmount;
    [ObservableProperty] private string settlementNotes = "";
    [ObservableProperty] private SettlementCalculation? settlementCalculation;
    [ObservableProperty] private string message = "";
    [ObservableProperty] private bool hasPreview;
    [ObservableProperty] private bool isClosedSettlement;
    [ObservableProperty] private int settlementId;
    [ObservableProperty] private int totalOrders;
    [ObservableProperty] private int deliveredOrders;
    [ObservableProperty] private int failedOrders;
    [ObservableProperty] private int returnedOrders;

    public ObservableCollection<DeliveryMan> Drivers => _parent.Drivers;

    public SettlementPageVm(MainViewModel parent, ISettlementService service, IDriverService driverService)
    {
        _parent = parent;
        _service = service;
        _driverService = driverService;
    }

    partial void OnSelectedDriverChanged(DeliveryMan? value) => InvalidatePreview();
    partial void OnSelectedSettlementDateChanged(DateTime value) => InvalidatePreview();

    partial void OnPaidAmountChanged(decimal value)
    {
        if (SettlementCalculation is not null)
            SettlementCalculation = SettlementCalculation with { Remaining = SettlementCalculation.Due - value };
    }

    private void InvalidatePreview()
    {
        HasPreview = false;
        IsClosedSettlement = false;
        SettlementId = 0;
        SettlementCalculation = null;
        TotalOrders = DeliveredOrders = FailedOrders = ReturnedOrders = 0;
        Message = "";
    }

    [RelayCommand]
    private async Task PreviewSettlement()
    {
        try
        {
            if (SelectedDriver is null)
                throw new InvalidOperationException("اختر المندوب أولًا.");
            if (PaidAmount < 0)
                throw new InvalidOperationException("المبلغ المدفوع لا يمكن أن يكون سالبًا.");

            var existing = await _service.GetByDriverAndDateAsync(SelectedDriver.Id, SelectedSettlementDate);
            var dayOrders = await _driverService.GetOrdersAsync(SelectedDriver.Id, null, SelectedSettlementDate, SelectedSettlementDate);

            TotalOrders = dayOrders.Count;
            DeliveredOrders = dayOrders.Count(x => x.Status == OrderStatus.Delivered);
            FailedOrders = dayOrders.Count(x => x.Status == OrderStatus.Failed);
            ReturnedOrders = dayOrders.Count(x => x.Status == OrderStatus.Returned);

            if (existing is not null)
            {
                IsClosedSettlement = existing.IsClosed;
                SettlementId = existing.Id;
                PaidAmount = existing.AmountPaid;
                SettlementNotes = existing.Notes;
                SettlementCalculation = new SettlementCalculation(
                    existing.OrdersValue, existing.DeliveryFees, existing.AmountCollected,
                    existing.Commission, existing.ApprovedExpenses, existing.AmountDueToOffice, existing.Remaining);
                HasPreview = true;
                Message = existing.IsClosed
                    ? $"تم العثور على تسوية مغلقة رقم #{existing.Id} — هذه مراجعة فقط ولا يمكن تعديلها."
                    : $"تم العثور على تسوية رقم #{existing.Id}.";
            }
            else
            {
                IsClosedSettlement = false;
                SettlementId = 0;
                SettlementCalculation = await _service.PreviewAsync(SelectedDriver.Id, SelectedSettlementDate, PaidAmount);
                HasPreview = true;
                Message = "تمت معاينة التسوية الجديدة من قاعدة البيانات.";
            }
        }
        catch (Exception ex)
        {
            HasPreview = false;
            SettlementCalculation = null;
            Message = ex.Message;
        }
    }

    [RelayCommand]
    private async Task CreateSettlement()
    {
        try
        {
            if (SelectedDriver is null)
                throw new InvalidOperationException("اختر المندوب أولًا.");
            if (IsClosedSettlement)
                throw new InvalidOperationException("هذه التسوية مغلقة بالفعل. يمكنك مراجعتها فقط ولا يمكن حفظها مرة أخرى.");
            if (!HasPreview || SettlementCalculation is null)
                throw new InvalidOperationException("يجب معاينة التسوية قبل الحفظ.");
            if (PaidAmount < 0)
                throw new InvalidOperationException("المبلغ المدفوع لا يمكن أن يكون سالبًا.");

            await _service.CreateAsync(SelectedDriver.Id, SelectedSettlementDate, PaidAmount, SettlementNotes.Trim(), _parent.CurrentUser.Id);
            Message = "تم حفظ التسوية وإغلاقها بنجاح.";
            HasPreview = false;
            SettlementCalculation = null;
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }
}

public partial class DriverDetailsPageVm : ObservableObject
{
    private readonly MainViewModel _parent; private readonly IDriverService _service;
    public DriverStatistics Driver { get; private set; }
    [ObservableProperty] private ObservableCollection<Order> orders = new();
    [ObservableProperty] private string selectedStatusFilter = "الكل";
    [ObservableProperty] private string selectedDateFilter = "الكل";
    [ObservableProperty] private DateTime? fromDate;
    [ObservableProperty] private DateTime? toDate;
    public ObservableCollection<string> StatusFilters { get; } = new(new[] { "الكل", "جديد", "مع المندوب", "في الطريق", "تم التسليم", "لم يتم التسليم", "مرتجع", "ملغي" });
    public ObservableCollection<string> DateFilters { get; } = new(new[] { "الكل", "اليوم", "أمس", "هذا الأسبوع", "هذا الشهر", "نطاق مخصص" });
    public DriverDetailsPageVm(MainViewModel parent, IDriverService service, DriverStatistics driver) { _parent = parent; _service = service; Driver = driver; }
    [RelayCommand] public async Task LoadAsync() { ResolveDates(); OrderStatus? status = SelectedStatusFilter switch { "جديد" => OrderStatus.New, "مع المندوب" => OrderStatus.Assigned, "في الطريق" => OrderStatus.OutForDelivery, "تم التسليم" => OrderStatus.Delivered, "لم يتم التسليم" => OrderStatus.Failed, "مرتجع" => OrderStatus.Returned, "ملغي" => OrderStatus.Cancelled, _ => null }; Orders = new ObservableCollection<Order>(await _service.GetOrdersAsync(Driver.DriverId, status, FromDate, ToDate)); }
    partial void OnSelectedDateFilterChanged(string value) { if (value != "نطاق مخصص") _ = LoadAsync(); }
    partial void OnSelectedStatusFilterChanged(string value) => _ = LoadAsync();
    private void ResolveDates() { var today=DateTime.Today; FromDate=ToDate=null; switch(SelectedDateFilter){case "اليوم": FromDate=ToDate=today; break; case "أمس": FromDate=ToDate=today.AddDays(-1); break; case "هذا الأسبوع": FromDate=today.AddDays(-(int)today.DayOfWeek+1); ToDate=today; break; case "هذا الشهر": FromDate=new DateTime(today.Year,today.Month,1); ToDate=today; break; case "نطاق مخصص": break;} }
    [RelayCommand] private async Task ApplyCustomDateRange() { if (SelectedDateFilter != "نطاق مخصص") return; if (FromDate.HasValue && ToDate.HasValue && FromDate > ToDate) return; await LoadAsync(); }
    [RelayCommand] private void Back() { _parent.CurrentPage = new DriversPageVm(_parent); _parent.PageTitle = "المناديب"; _ = _parent.LoadDriverStatisticsAsync(); }
}

public sealed record Choice<T>(string Name, T Value)
{
    public override string ToString() => Name;
}
public sealed record DashboardPageVm(MainViewModel Parent);
public sealed record OrdersPageVm(MainViewModel Parent);
public sealed record AddOrderPageVm(MainViewModel Parent);
public sealed record DriversPageVm(MainViewModel Parent);
public sealed record CustomersPageVm(MainViewModel Parent);
public sealed record InfoPageVm(string Title);
