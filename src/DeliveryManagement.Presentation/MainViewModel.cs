using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClosedXML.Excel;
using Microsoft.Win32;
using DeliveryManagement.Application;
using DeliveryManagement.Domain;
using DeliveryManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Diagnostics;
using System.IO;

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
    [ObservableProperty]
    private string todayText =
        DateTime.Now.ToString("dddd، dd MMMM yyyy", new CultureInfo("ar-EG"));

    [ObservableProperty] private int todayOrders;
    [ObservableProperty] private int deliveredOrders;
    [ObservableProperty] private int returnedOrders;
    [ObservableProperty] private decimal todayRevenue;
    [ObservableProperty] private decimal todayFees;
    [ObservableProperty] private decimal todayCollected;

    [ObservableProperty]
    private ObservableCollection<Order> latestOrders = new();

    [ObservableProperty]
    private ObservableCollection<DeliveryMan> drivers = new();

    [ObservableProperty]
    private ObservableCollection<DriverStatistics> driverStatistics = new();

    [ObservableProperty]
    private ObservableCollection<Customer> customers = new();

    [ObservableProperty] private string searchText = "";
    private OrderStatus? _selectedOrderStatusFilter;

    public OrderStatus? SelectedOrderStatusFilter
    {
        get => _selectedOrderStatusFilter;
        set
        {
            if (_selectedOrderStatusFilter == value)
                return;

            _selectedOrderStatusFilter = value;
            OnPropertyChanged();
        }
    }

    private int _currentOrdersPage = 1;

    public int CurrentOrdersPage
    {
        get => _currentOrdersPage;
        set
        {
            if (_currentOrdersPage == value)
                return;

            _currentOrdersPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OrdersPageText));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    private int _totalOrdersPages = 1;

    public int TotalOrdersPages
    {
        get => _totalOrdersPages;
        private set
        {
            if (_totalOrdersPages == value)
                return;

            _totalOrdersPages = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OrdersPageText));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    public string OrdersPageText =>
        $"الصفحة {CurrentOrdersPage} من {TotalOrdersPages}";

    public bool CanGoPrevious =>
        CurrentOrdersPage > 1;

    public bool CanGoNext =>
        CurrentOrdersPage < TotalOrdersPages;

    private const int OrdersPageSize = 20;

    private DeliveryMan? _selectedOrderDriverFilter;

    public DeliveryMan? SelectedOrderDriverFilter
    {
        get => _selectedOrderDriverFilter;
        set
        {
            if (_selectedOrderDriverFilter == value)
                return;

            _selectedOrderDriverFilter = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<Choice<OrderStatus?>> OrderStatusFilterOptions { get; } = new()
{
    new("كل الحالات", null),
    new("جديد", OrderStatus.New),
    new("مع المندوب", OrderStatus.Assigned),
    new("في الطريق", OrderStatus.OutForDelivery),
    new("تم التسليم", OrderStatus.Delivered),
    new("لم يتم التسليم", OrderStatus.Failed),
    new("مرتجع", OrderStatus.Returned),
    new("ملغي", OrderStatus.Cancelled)
};
    [ObservableProperty] private string customerName = "";
    [ObservableProperty] private string customerPhone = "";
    [ObservableProperty] private string customerAddress = "";

    [ObservableProperty]
    private OrderType selectedType = OrderType.Parcel;

    [ObservableProperty] private decimal orderValue;
    [ObservableProperty] private decimal deliveryFee;
    [ObservableProperty] private DeliveryMan? selectedDriver;

    [ObservableProperty]
    private OrderStatus selectedStatus = OrderStatus.New;

    [ObservableProperty] private string orderNotes = "";
    [ObservableProperty] private string statusMessage = "";

    public event Action<Invoice>? InvoiceRequested;

    public decimal TotalOrderAmount => OrderValue + DeliveryFee;

    public ObservableCollection<Choice<OrderType>> OrderTypeOptions { get; } = new()
    {
        new("طرد", OrderType.Parcel),
        new("مأكولات", OrderType.Food),
        new("مستندات", OrderType.Documents),
        new("طلبات", OrderType.Requests),
        new("شحن", OrderType.Shipping),
        new("أخرى", OrderType.Other)
    };

    public ObservableCollection<Choice<OrderStatus>> OrderStatusOptions { get; } = new()
    {
        new("جديد", OrderStatus.New),
        new("مع المندوب", OrderStatus.Assigned),
        new("في الطريق", OrderStatus.OutForDelivery),
        new("تم التسليم", OrderStatus.Delivered),
        new("لم يتم التسليم", OrderStatus.Failed),
        new("مرتجع", OrderStatus.Returned),
        new("ملغي", OrderStatus.Cancelled)
    };

    public User CurrentUser { get; private set; } = null!;

    public MainViewModel(
        AppDbContext db,
        IOrderService orders,
        ISettlementService settlements,
        IInvoiceService invoices,
        IDriverService driverService)
    {
        _db = db;
        _orders = orders;
        _settlements = settlements;
        _invoices = invoices;
        _driverService = driverService;
    }

    public void Initialize(User user)
    {
        CurrentUser = user;
        CurrentUserName = user.DisplayName;

        CurrentPage = new DashboardPageVm(this);

        _ = LoadDashboardAsync();
    }

    // =========================================================
    // Authorization
    // =========================================================

    private bool CanAccessPage(string page)
    {
        if (CurrentUser is null)
            return false;

        return CurrentUser.Role switch
        {
            UserRole.Admin => true,

            UserRole.Employee => page switch
            {
                "الرئيسية" => true,
                "الأوردرات" => true,
                "إضافة أوردر" => true,
                "المناديب" => true,
                "العملاء" => true,
                _ => false
            },

            UserRole.Accountant => page switch
            {
                "الرئيسية" => true,
                "الأوردرات" => true,
                "المناديب" => true,
                "العملاء" => true,
                "التسويات المالية" => true,
                _ => false
            },

            _ => false
        };
    }

    public bool HasPermission(UserRole minimumRole)
    {
        if (CurrentUser is null)
            return false;

        return CurrentUser.Role == minimumRole ||
               CurrentUser.Role == UserRole.Admin;
    }

    public bool IsAdmin => CurrentUser?.Role == UserRole.Admin;

    public bool IsEmployee =>
        CurrentUser.Role == UserRole.Employee ||
        CurrentUser.Role == UserRole.Admin;
    public bool IsAccountant =>
    CurrentUser.Role == UserRole.Accountant ||
    CurrentUser.Role == UserRole.Admin;
    // =========================================================
    // Property Changes
    // =========================================================

    partial void OnOrderValueChanged(decimal value)
    {
        OnPropertyChanged(nameof(TotalOrderAmount));
    }

    partial void OnDeliveryFeeChanged(decimal value)
    {
        OnPropertyChanged(nameof(TotalOrderAmount));
    }

    // =========================================================
    // Navigation
    // =========================================================

    [RelayCommand]
    private async Task Navigate(string page)
    {
        try
        {
            // Authorization check
            if (!CanAccessPage(page))
            {
                StatusMessage = "ليس لديك صلاحية للوصول إلى هذه الصفحة.";
                return;
            }

            PageTitle = page;
            StatusMessage = "";

            switch (page)
            {
                case "الرئيسية":
                    CurrentPage = new DashboardPageVm(this);
                    await LoadDashboardAsync();
                    break;

                case "الأوردرات":
                    await LoadDriversAsync();
                    CurrentPage = new OrdersPageVm(this);
                    await LoadDashboardAsync();
                    break;

                case "إضافة أوردر":
                    await LoadDriversAsync();
                    CurrentPage = new AddOrderPageVm(this);
                    break;

                case "المناديب":
                    await LoadDriverStatisticsAsync();
                    CurrentPage = new DriversPageVm(this);
                    break;

                case "العملاء":
                    await LoadCustomersAsync();
                    CurrentPage = new CustomersPageVm(this);
                    break;

                case "التسويات المالية":
                    await LoadDriversAsync();
                    CurrentPage = new SettlementPageVm(
                        this,
                        _settlements,
                        _driverService);
                    break;

                case "التقارير":
                    CurrentPage = new ReportsPageVm(this);
                    await ((ReportsPageVm)CurrentPage).LoadAsync();
                    break;

                default:
                    CurrentPage = new InfoPageVm(page);
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            Log.Error(ex, "Navigation failed for page {Page}", page);
        }
    }

    // =========================================================
    // Dashboard
    // =========================================================

    public async Task LoadDashboardAsync()
    {
        var start = DateTime.Today;
        var end = start.AddDays(1);

        await LoadTodayDriverStatisticsAsync();

        var q = _db.Orders
            .AsNoTracking()
            .Where(o =>
                o.CreatedAt >= start &&
                o.CreatedAt < end);

        TodayOrders = await q.CountAsync();

        DeliveredOrders = await q.CountAsync(
            o => o.Status == OrderStatus.Delivered);

        ReturnedOrders = await q.CountAsync(
            o => o.Status == OrderStatus.Returned);

        TodayRevenue =
            await q.SumAsync(o => (decimal?)o.OrderValue) ?? 0m;

        TodayFees =
            await q.SumAsync(o => (decimal?)o.DeliveryFee) ?? 0m;

        TodayCollected =
            await q
                .Where(o => o.Status == OrderStatus.Delivered)
                .SumAsync(o => (decimal?)o.CollectedAmount) ?? 0m;
        await LoadOrdersPageAsync();
    }
    public async Task LoadOrdersPageAsync()
    {
        try
        {
            var search = SearchText.Trim();

            OrderStatus? status =
                SelectedOrderStatusFilter;

            int? driverId =
                SelectedOrderDriverFilter?.Id;

            var query = _db.Orders
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(o =>
                    o.OrderNumber.Contains(search) ||
                    o.PhoneSnapshot.Contains(search) ||
                    o.Customer.Name.Contains(search) ||
                    (o.DeliveryMan != null &&
                     o.DeliveryMan.Name.Contains(search)));
            }

            if (status.HasValue)
            {
                query = query.Where(o =>
                    o.Status == status.Value);
            }

            if (driverId.HasValue)
            {
                query = query.Where(o =>
                    o.DeliveryManId == driverId.Value);
            }

            var totalOrders =
                await query.CountAsync();

            TotalOrdersPages =
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        totalOrders / (double)OrdersPageSize));

            if (CurrentOrdersPage > TotalOrdersPages)
                CurrentOrdersPage = TotalOrdersPages;

            LatestOrders =
                new ObservableCollection<Order>(
                    await _orders.GetOrdersAsync(
                        search,
                        status,
                        null,
                        driverId,
                        null,
                        null,
                        CurrentOrdersPage,
                        OrdersPageSize));
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;

            Log.Error(
                ex,
                "Load orders page failed");
        }
    }
    [RelayCommand]
    private async Task PreviousOrdersPage()
    {
        if (!CanGoPrevious)
            return;

        CurrentOrdersPage--;

        await LoadOrdersPageAsync();
    }

    [RelayCommand]
    private async Task NextOrdersPage()
    {
        if (!CanGoNext)
            return;

        CurrentOrdersPage++;

        await LoadOrdersPageAsync();
    }

    [RelayCommand]
    private async Task SearchOrders()
    {
        CurrentOrdersPage = 1;
        await LoadDashboardAsync();
    }

    [RelayCommand]
    private async Task ClearOrderFilters()
    {
        SearchText = "";
        SelectedOrderStatusFilter = null;
        SelectedOrderDriverFilter = null;

        CurrentOrdersPage = 1;

        await LoadDashboardAsync();
    }

    [RelayCommand]
    private async Task RefreshOrders()
    {
        await LoadDashboardAsync();
    }

    // =========================================================
    // Drivers
    // =========================================================

    public async Task LoadDriversAsync()
    {
        Drivers =
            new ObservableCollection<DeliveryMan>(
                await _db.DeliveryMen
                    .AsNoTracking()
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.Name)
                    .ToListAsync());
    }

    public async Task LoadDriverStatisticsAsync()
    {
        DriverStatistics =
            new ObservableCollection<DriverStatistics>(
                await _driverService.GetStatisticsAsync());
    }

    public async Task LoadTodayDriverStatisticsAsync()
    {
        DriverStatistics =
            new ObservableCollection<DriverStatistics>(
                await _driverService.GetStatisticsAsync(
                    DateTime.Today,
                    DateTime.Today));
    }

    // =========================================================
    // Customers
    // =========================================================

    public async Task LoadCustomersAsync()
    {
        Customers =
            new ObservableCollection<Customer>(
                await _db.Customers
                    .AsNoTracking()
                    .Include(x => x.Orders)
                    .OrderByDescending(x => x.CreatedAt)
                    .ToListAsync());
    }

    // =========================================================
    // Orders
    // =========================================================

    [RelayCommand]
    private async Task SaveOrder()
    {
        try
        {
            CustomerName = CustomerName.Trim();
            CustomerPhone = CustomerPhone.Trim();
            CustomerAddress = CustomerAddress.Trim();

            if (string.IsNullOrWhiteSpace(CustomerName))
                throw new InvalidOperationException(
                    "يرجى إدخال اسم العميل.");

            if (string.IsNullOrWhiteSpace(CustomerPhone) ||
                CustomerPhone.Length < 8)
            {
                throw new InvalidOperationException(
                    "يرجى إدخال رقم هاتف صحيح.");
            }

            if (OrderValue < 0 || DeliveryFee < 0)
            {
                throw new InvalidOperationException(
                    "القيم المالية لا يمكن أن تكون سالبة.");
            }

            var customer =
                await _db.Customers
                    .FirstOrDefaultAsync(
                        x => x.Phone == CustomerPhone);

            if (customer is null)
            {
                customer = new Customer
                {
                    Name = CustomerName,
                    Phone = CustomerPhone,
                    DefaultAddress = CustomerAddress
                };

                _db.Customers.Add(customer);

                await _db.SaveChangesAsync();
            }
            else
            {
                customer.Name = CustomerName;
                customer.DefaultAddress = CustomerAddress;
            }

            var order = new Order
            {
                CustomerId = customer.Id,
                Customer = null!,
                PhoneSnapshot = customer.Phone,
                Address = CustomerAddress,
                Type = SelectedType,
                OrderValue = OrderValue,
                DeliveryFee = DeliveryFee,
                DeliveryManId = SelectedDriver?.Id,
                DeliveryMan = null,
                Status = SelectedStatus,
                CollectedAmount =
                    SelectedStatus == OrderStatus.Delivered
                        ? TotalOrderAmount
                        : 0m,
                Notes = OrderNotes.Trim()
            };

            await _orders.CreateAsync(
                order,
                CurrentUser.Id);

            StatusMessage =
                $"تم حفظ الأوردر {order.OrderNumber} بنجاح";

            ClearOrder();

            await LoadDashboardAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            Log.Error(ex, "Create order failed");
        }
    }

    [RelayCommand]
    private void ClearOrder()
    {
        CustomerName = "";
        CustomerPhone = "";
        CustomerAddress = "";
        OrderNotes = "";

        OrderValue = 0m;
        DeliveryFee = 0m;

        SelectedDriver = null;
        SelectedType = OrderType.Parcel;
        SelectedStatus = OrderStatus.New;
    }

    public async Task ChangeStatus(
        Order order,
        OrderStatus status)
    {
        try
        {
            await _orders.ChangeStatusAsync(
                order.Id,
                status,
                CurrentUser.Id,
                CurrentUser.Role == UserRole.Admin);

            StatusMessage = "تم تحديث حالة الأوردر";

            await LoadDashboardAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;

            Log.Error(
                ex,
                "Change order status failed");
        }
    }

    // =========================================================
    // Invoice
    // =========================================================

    [RelayCommand]
    private async Task ShowInvoice(Order order)
    {
        try
        {
            InvoiceRequested?.Invoke(
                await _invoices.GetOrCreateForOrderAsync(
                    order.Id));
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;

            Log.Error(
                ex,
                "Open invoice failed");
        }
    }

    // =========================================================
    // Driver Details
    // =========================================================

    [RelayCommand]
    private async Task OpenDriverDetails(
        DriverStatistics driver)
    {
        CurrentPage =
            new DriverDetailsPageVm(
                this,
                _driverService,
                driver);

        PageTitle = "تفاصيل المندوب";

        await ((DriverDetailsPageVm)CurrentPage)
            .LoadAsync();
    }

    internal AppDbContext GetDbContext() => _db;
}

// =============================================================
// Reports Page ViewModel
// =============================================================

public partial class ReportsPageVm : ObservableObject
{
    private readonly AppDbContext _db;

    public DateTime FromDate { get; set; } = new DateTime(
        DateTime.Today.Year,
        DateTime.Today.Month,
        1);

    public DateTime ToDate { get; set; } = DateTime.Today;

    public int TotalOrders { get; private set; }
    public int DeliveredOrders { get; private set; }
    public int FailedOrders { get; private set; }
    public int ReturnedOrders { get; private set; }
    public int CancelledOrders { get; private set; }

    public decimal TotalOrderValue { get; private set; }
    public decimal TotalDeliveryFees { get; private set; }
    public decimal TotalCollected { get; private set; }

    public string Message { get; private set; } = "";

    public ObservableCollection<ReportDriverRow> DriverRows { get; private set; } = new();

    public ReportsPageVm(MainViewModel parent)
    {
        _db = parent.GetDbContext();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            Message = "";
            OnPropertyChanged(nameof(Message));

            if (FromDate.Date > ToDate.Date)
                throw new InvalidOperationException(
                    "تاريخ البداية يجب أن يكون قبل أو يساوي تاريخ النهاية.");

            var start = FromDate.Date;
            var end = ToDate.Date.AddDays(1);

            // Load the filtered orders into memory first.
            // This prevents EF Core from trying to translate the
            // driver GroupBy/aggregation into SQL.
            var orders = await _db.Orders
                .AsNoTracking()
                .Include(o => o.DeliveryMan)
                .Where(o =>
                    o.CreatedAt >= start &&
                    o.CreatedAt < end)
                .ToListAsync();

            // General report statistics
            TotalOrders = orders.Count;

            DeliveredOrders = orders.Count(
                o => o.Status == OrderStatus.Delivered);

            FailedOrders = orders.Count(
                o => o.Status == OrderStatus.Failed);

            ReturnedOrders = orders.Count(
                o => o.Status == OrderStatus.Returned);

            CancelledOrders = orders.Count(
                o => o.Status == OrderStatus.Cancelled);

            TotalOrderValue = orders.Sum(
                o => o.OrderValue);

            TotalDeliveryFees = orders.Sum(
                o => o.DeliveryFee);

            TotalCollected = orders
                .Where(o => o.Status == OrderStatus.Delivered)
                .Sum(o => o.CollectedAmount);

            // Driver report is calculated in memory after ToListAsync().
            DriverRows = new ObservableCollection<ReportDriverRow>(
                orders
                    .GroupBy(o => new
                    {
                        o.DeliveryManId,
                        DriverName = o.DeliveryMan != null
                            ? o.DeliveryMan.Name
                            : "غير محدد"
                    })
                    .Select(g => new ReportDriverRow(
                        g.Key.DeliveryManId,
                        g.Key.DriverName,
                        g.Count(),
                        g.Count(o => o.Status == OrderStatus.Delivered),
                        g.Count(o => o.Status == OrderStatus.Failed),
                        g.Count(o => o.Status == OrderStatus.Returned),
                        g.Sum(o => o.OrderValue),
                        g.Sum(o => o.DeliveryFee),
                        g.Where(o => o.Status == OrderStatus.Delivered)
                            .Sum(o => o.CollectedAmount)))
                    .OrderByDescending(x => x.Orders));

            Message =
                $"تم تحميل التقرير من {FromDate:yyyy-MM-dd} إلى {ToDate:yyyy-MM-dd}.";

            OnPropertyChanged(string.Empty);
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            OnPropertyChanged(nameof(Message));
            Log.Error(ex, "Load reports failed");
        }
    }

    [RelayCommand]
    private async Task ExportExcel()
    {
        try
        {
            if (FromDate.Date > ToDate.Date)
                throw new InvalidOperationException(
                    "تاريخ البداية يجب أن يكون قبل أو يساوي تاريخ النهاية.");

            var dialog = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                AddExtension = true,
                FileName =
                    $"تقرير_التوصيل_{FromDate:yyyy-MM-dd}_{ToDate:yyyy-MM-dd}.xlsx"
            };

            if (dialog.ShowDialog() != true)
                return;

            var start = FromDate.Date;
            var end = ToDate.Date.AddDays(1);

            var orders = await _db.Orders
                .AsNoTracking()
                .Include(o => o.DeliveryMan)
                .Where(o =>
                    o.CreatedAt >= start &&
                    o.CreatedAt < end)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("التقرير");

            sheet.Cell(1, 1).Value = "تقرير توصيله";
            sheet.Range(1, 1, 1, 8).Merge();
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Font.FontSize = 18;
            sheet.Cell(1, 1).Style.Alignment.Horizontal =
                XLAlignmentHorizontalValues.Center;

            sheet.Cell(2, 1).Value = "الفترة";
            sheet.Cell(2, 2).Value =
                $"{FromDate:yyyy-MM-dd} إلى {ToDate:yyyy-MM-dd}";
            sheet.Range(2, 2, 2, 8).Merge();

            sheet.Cell(4, 1).Value = "إجمالي الأوردرات";
            sheet.Cell(4, 2).Value = TotalOrders;
            sheet.Cell(4, 3).Value = "تم التسليم";
            sheet.Cell(4, 4).Value = DeliveredOrders;
            sheet.Cell(4, 5).Value = "لم يتم التسليم";
            sheet.Cell(4, 6).Value = FailedOrders;
            sheet.Cell(4, 7).Value = "مرتجع";
            sheet.Cell(4, 8).Value = ReturnedOrders;

            sheet.Cell(5, 1).Value = "ملغي";
            sheet.Cell(5, 2).Value = CancelledOrders;
            sheet.Cell(5, 3).Value = "قيمة الأوردرات";
            sheet.Cell(5, 4).Value = TotalOrderValue;
            sheet.Cell(5, 5).Value = "رسوم التوصيل";
            sheet.Cell(5, 6).Value = TotalDeliveryFees;
            sheet.Cell(5, 7).Value = "إجمالي التحصيل";
            sheet.Cell(5, 8).Value = TotalCollected;

            for (int column = 1; column <= 8; column++)
            {
                sheet.Cell(4, column).Style.Font.Bold = true;
                sheet.Cell(5, column).Style.Font.Bold = true;
            }

            sheet.Cell(7, 1).Value = "أداء المناديب";

            string[] headers =
            {
                "المندوب",
                "الأوردرات",
                "تم التسليم",
                "لم يتم",
                "مرتجع",
                "قيمة الأوردرات",
                "رسوم التوصيل",
                "التحصيل"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                sheet.Cell(8, i + 1).Value = headers[i];
                sheet.Cell(8, i + 1).Style.Font.Bold = true;
            }

            var driverRows = orders
                .GroupBy(o => new
                {
                    o.DeliveryManId,
                    DriverName = o.DeliveryMan != null
                        ? o.DeliveryMan.Name
                        : "غير محدد"
                })
                .Select(g => new
                {
                    g.Key.DriverName,
                    Orders = g.Count(),
                    Delivered = g.Count(
                        o => o.Status == OrderStatus.Delivered),
                    Failed = g.Count(
                        o => o.Status == OrderStatus.Failed),
                    Returned = g.Count(
                        o => o.Status == OrderStatus.Returned),
                    OrderValue = g.Sum(o => o.OrderValue),
                    DeliveryFees = g.Sum(o => o.DeliveryFee),
                    Collected = g
                        .Where(o => o.Status == OrderStatus.Delivered)
                        .Sum(o => o.CollectedAmount)
                })
                .OrderByDescending(x => x.Orders)
                .ToList();

            var row = 9;

            foreach (var driver in driverRows)
            {
                sheet.Cell(row, 1).Value = driver.DriverName;
                sheet.Cell(row, 2).Value = driver.Orders;
                sheet.Cell(row, 3).Value = driver.Delivered;
                sheet.Cell(row, 4).Value = driver.Failed;
                sheet.Cell(row, 5).Value = driver.Returned;
                sheet.Cell(row, 6).Value = driver.OrderValue;
                sheet.Cell(row, 7).Value = driver.DeliveryFees;
                sheet.Cell(row, 8).Value = driver.Collected;

                row++;
            }

            if (row > 9)
            {
                sheet.Range(9, 6, row - 1, 8)
                    .Style.NumberFormat.Format = "#,##0.00";
            }

            sheet.Range(8, 1, Math.Max(8, row - 1), 8)
                .Style.Alignment.Horizontal =
                XLAlignmentHorizontalValues.Center;

            sheet.Columns().AdjustToContents();

            workbook.SaveAs(dialog.FileName);

            Message = "تم تصدير التقرير إلى Excel بنجاح.";
            OnPropertyChanged(nameof(Message));
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            OnPropertyChanged(nameof(Message));
            Log.Error(ex, "Export reports to Excel failed");
        }
    }

    [RelayCommand]
    private async Task ExportPdf()
    {
        try
        {
            if (FromDate.Date > ToDate.Date)
                throw new InvalidOperationException(
                    "تاريخ البداية يجب أن يكون قبل أو يساوي تاريخ النهاية.");

            var dialog = new SaveFileDialog
            {
                Filter = "PDF Document (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                AddExtension = true,
                FileName =
                    $"تقرير_التوصيل_{FromDate:yyyy-MM-dd}_{ToDate:yyyy-MM-dd}.pdf"
            };

            if (dialog.ShowDialog() != true)
                return;

            var start = FromDate.Date;
            var end = ToDate.Date.AddDays(1);

            var orders = await _db.Orders
                .AsNoTracking()
                .Include(o => o.DeliveryMan)
                .Where(o =>
                    o.CreatedAt >= start &&
                    o.CreatedAt < end)
                .ToListAsync();

            GenerateReportPdf(dialog.FileName, orders);

            Message = "تم تصدير التقرير إلى PDF بنجاح.";
            OnPropertyChanged(nameof(Message));
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            OnPropertyChanged(nameof(Message));
            Log.Error(ex, "Export reports to PDF failed");
        }
    }

    [RelayCommand]
    private async Task PrintReport()
    {
        string? tempFile = null;

        try
        {
            if (FromDate.Date > ToDate.Date)
                throw new InvalidOperationException(
                    "تاريخ البداية يجب أن يكون قبل أو يساوي تاريخ النهاية.");

            var start = FromDate.Date;
            var end = ToDate.Date.AddDays(1);

            var orders = await _db.Orders
                .AsNoTracking()
                .Include(o => o.DeliveryMan)
                .Where(o =>
                    o.CreatedAt >= start &&
                    o.CreatedAt < end)
                .ToListAsync();

            tempFile = Path.Combine(
                Path.GetTempPath(),
                $"تقرير_التوصيل_{Guid.NewGuid():N}.pdf");

            GenerateReportPdf(tempFile, orders);

            Process.Start(new ProcessStartInfo
            {
                FileName = tempFile,
                UseShellExecute = true
            });

        }
        catch (Exception ex)
        {
            Message = $"تعذر إرسال التقرير للطباعة: {ex.Message}";
            OnPropertyChanged(nameof(Message));
            Log.Error(ex, "Print reports failed");
        }
    }

    private void GenerateReportPdf(
        string filePath,
        List<Order> orders)
    {
        // The Community license is free for eligible individual/academic use.
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = true;

        var driverRows = orders
            .GroupBy(o => new
            {
                o.DeliveryManId,
                DriverName = o.DeliveryMan != null
                    ? o.DeliveryMan.Name
                    : "غير محدد"
            })
            .Select(g => new
            {
                g.Key.DriverName,
                Orders = g.Count(),
                Delivered = g.Count(
                    o => o.Status == OrderStatus.Delivered),
                Failed = g.Count(
                    o => o.Status == OrderStatus.Failed),
                Returned = g.Count(
                    o => o.Status == OrderStatus.Returned),
                OrderValue = g.Sum(o => o.OrderValue),
                DeliveryFees = g.Sum(o => o.DeliveryFee),
                Collected = g
                    .Where(o => o.Status == OrderStatus.Delivered)
                    .Sum(o => o.CollectedAmount)
            })
            .OrderByDescending(x => x.Orders)
            .ToList();

        Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(25);
                page.ContentFromRightToLeft();

                page.DefaultTextStyle(style =>
                    style
                        .FontFamily("Segoe UI", "Arial", "Tahoma")
                        .FontSize(10));

                page.Header()
                    .Column(header =>
                    {
                        header.Item()
                            .AlignCenter()
                            .Text(text =>
                            {
                                text.Span("تقرير توصيله")
                                    .FontSize(22)
                                    .Bold();
                            });

                        header.Item()
                            .AlignCenter()
                            .Text(text =>
                            {
                                text.Span(
                                    $"الفترة: {FromDate:yyyy-MM-dd} إلى {ToDate:yyyy-MM-dd}")
                                    .FontSize(11);
                            });
                    });

                page.Content()
                    .PaddingVertical(15)
                    .Column(column =>
                    {
                        column.Spacing(10);

                        column.Item().Row(row =>
                        {
                            AddKpi(row, "إجمالي الأوردرات", TotalOrders.ToString());
                            AddKpi(row, "تم التسليم", DeliveredOrders.ToString());
                            AddKpi(row, "لم يتم التسليم", FailedOrders.ToString());
                            AddKpi(row, "مرتجع", ReturnedOrders.ToString());
                            AddKpi(row, "ملغي", CancelledOrders.ToString());
                        });

                        column.Item().Row(row =>
                        {
                            AddKpi(row, "قيمة الأوردرات",
                                $"{TotalOrderValue:N2} جنيه");
                            AddKpi(row, "رسوم التوصيل",
                                $"{TotalDeliveryFees:N2} جنيه");
                            AddKpi(row, "إجمالي التحصيل",
                                $"{TotalCollected:N2} جنيه");
                        });

                        column.Item()
                            .PaddingTop(8)
                            .Text(text =>
                            {
                                text.Span("أداء المناديب")
                                    .FontSize(15)
                                    .Bold();
                            });

                        column.Item()
                            .Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(2.2f);
                                    columns.RelativeColumn();
                                    columns.RelativeColumn();
                                    columns.RelativeColumn();
                                    columns.RelativeColumn();
                                    columns.RelativeColumn(1.4f);
                                    columns.RelativeColumn(1.4f);
                                    columns.RelativeColumn(1.4f);
                                });

                                string[] headers =
                                {
                                    "المندوب",
                                    "الأوردرات",
                                    "تم التسليم",
                                    "لم يتم",
                                    "مرتجع",
                                    "قيمة الأوردرات",
                                    "رسوم التوصيل",
                                    "التحصيل"
                                };

                                table.Header(header =>
                                {
                                    foreach (var headerText in headers)
                                    {
                                        header.Cell()
                                            .Background(Colors.Grey.Lighten2)
                                            .Border(1)
                                            .Padding(5)
                                            .AlignCenter()
                                            .Text(text =>
                                            {
                                                text.Span(headerText).Bold();
                                            });
                                    }
                                });

                                foreach (var driver in driverRows)
                                {
                                    AddPdfCell(table, driver.DriverName);
                                    AddPdfCell(table, driver.Orders.ToString());
                                    AddPdfCell(table, driver.Delivered.ToString());
                                    AddPdfCell(table, driver.Failed.ToString());
                                    AddPdfCell(table, driver.Returned.ToString());
                                    AddPdfCell(table, driver.OrderValue.ToString("N2"));
                                    AddPdfCell(table, driver.DeliveryFees.ToString("N2"));
                                    AddPdfCell(table, driver.Collected.ToString("N2"));
                                }
                            });

                        if (driverRows.Count == 0)
                        {
                            column.Item()
                                .PaddingTop(10)
                                .AlignCenter()
                                .Text("لا توجد بيانات للمناديب في الفترة المحددة.");
                        }
                    });

                page.Footer()
                    .AlignCenter()
                    .Text(text =>
                    {
                        text.Span("توصيله - صفحة ");
                        text.CurrentPageNumber();
                        text.Span(" من ");
                        text.TotalPages();
                    });
            });
        }).GeneratePdf(filePath);
    }

    private static void AddKpi(
        QuestPDF.Fluent.RowDescriptor row,
        string title,
        string value)
    {
        row.RelativeItem()
            .Border(1)
            .Padding(8)
            .Column(column =>
            {
                column.Item()
                    .AlignCenter()
                    .Text(text =>
                    {
                        text.Span(title)
                            .FontSize(9);
                    });

                column.Item()
                .AlignCenter()
                .Text(text =>
                {
                    text.Span(value)
                        .FontSize(14)
                        .Bold();
                });
            });
    }

    private static void AddPdfCell(
        QuestPDF.Fluent.TableDescriptor table,
        string value)
    {
        table.Cell()
            .Border(1)
            .Padding(5)
            .AlignCenter()
            .Text(value);
    }

    [RelayCommand]
    private async Task Reset()
    {
        FromDate = new DateTime(
            DateTime.Today.Year,
            DateTime.Today.Month,
            1);

        ToDate = DateTime.Today;

        OnPropertyChanged(nameof(FromDate));
        OnPropertyChanged(nameof(ToDate));

        await LoadAsync();
    }
}

public sealed record ReportDriverRow(
    int? DriverId,
    string DriverName,
    int Orders,
    int Delivered,
    int Failed,
    int Returned,
    decimal OrderValue,
    decimal DeliveryFees,
    decimal Collected);

// =============================================================
// Settlement Page ViewModel
// =============================================================

public partial class SettlementPageVm : ObservableObject
{
    private readonly MainViewModel _parent;
    private readonly ISettlementService _service;
    private readonly IDriverService _driverService;

    [ObservableProperty]
    private DeliveryMan? selectedDriver;

    [ObservableProperty]
    private DateTime selectedSettlementDate = DateTime.Today;

    [ObservableProperty]
    private decimal paidAmount;

    [ObservableProperty]
    private string settlementNotes = "";

    [ObservableProperty]
    private SettlementCalculation? settlementCalculation;

    [ObservableProperty]
    private string message = "";

    [ObservableProperty]
    private bool hasPreview;

    [ObservableProperty]
    private bool isClosedSettlement;

    [ObservableProperty]
    private int settlementId;

    [ObservableProperty]
    private int totalOrders;

    [ObservableProperty]
    private int deliveredOrders;

    [ObservableProperty]
    private int failedOrders;

    [ObservableProperty]
    private int returnedOrders;

    public ObservableCollection<DeliveryMan> Drivers =>
        _parent.Drivers;

    public SettlementPageVm(
        MainViewModel parent,
        ISettlementService service,
        IDriverService driverService)
    {
        _parent = parent;
        _service = service;
        _driverService = driverService;
    }

    partial void OnSelectedDriverChanged(
        DeliveryMan? value)
    {
        InvalidatePreview();
    }

    partial void OnSelectedSettlementDateChanged(
        DateTime value)
    {
        InvalidatePreview();
    }

    partial void OnPaidAmountChanged(decimal value)
    {
        if (SettlementCalculation is null)
            return;

        if (value > SettlementCalculation.Due)
        {
            Message =
                $"المبلغ المدفوع لا يمكن أن يكون أكبر من المستحق للمكتب ({SettlementCalculation.Due:N2} جنيه).";

            OnPropertyChanged(nameof(Message));

            SettlementCalculation =
                SettlementCalculation with
                {
                    Remaining = 0m
                };

            return;
        }

        if (value < 0)
        {
            Message = "المبلغ المدفوع لا يمكن أن يكون سالبًا.";
            OnPropertyChanged(nameof(Message));

            SettlementCalculation =
                SettlementCalculation with
                {
                    Remaining = SettlementCalculation.Due
                };

            return;
        }

        Message = "";

        SettlementCalculation =
            SettlementCalculation with
            {
                Remaining = SettlementCalculation.Due - value
            };
    }

    private void InvalidatePreview()
    {
        HasPreview = false;
        IsClosedSettlement = false;
        SettlementId = 0;
        SettlementCalculation = null;

        TotalOrders =
            DeliveredOrders =
            FailedOrders =
            ReturnedOrders = 0;

        Message = "";
    }

    [RelayCommand]
    private async Task PreviewSettlement()
    {
        try
        {
            if (SelectedDriver is null)
                throw new InvalidOperationException(
                    "اختر المندوب أولًا.");

            if (PaidAmount < 0)
                throw new InvalidOperationException(
                    "المبلغ المدفوع لا يمكن أن يكون سالبًا.");

            var existing =
                await _service.GetByDriverAndDateAsync(
                    SelectedDriver.Id,
                    SelectedSettlementDate);

            var dayOrders =
                await _driverService.GetOrdersAsync(
                    SelectedDriver.Id,
                    null,
                    SelectedSettlementDate,
                    SelectedSettlementDate);

            TotalOrders = dayOrders.Count;

            DeliveredOrders =
                dayOrders.Count(
                    x => x.Status == OrderStatus.Delivered);

            FailedOrders =
                dayOrders.Count(
                    x => x.Status == OrderStatus.Failed);

            ReturnedOrders =
                dayOrders.Count(
                    x => x.Status == OrderStatus.Returned);

            if (existing is not null)
            {
                IsClosedSettlement = existing.IsClosed;
                SettlementId = existing.Id;
                PaidAmount = existing.AmountPaid;
                SettlementNotes = existing.Notes;

                SettlementCalculation =
                    new SettlementCalculation(
                        existing.OrdersValue,
                        existing.DeliveryFees,
                        existing.AmountCollected,
                        existing.Commission,
                        existing.ApprovedExpenses,
                        existing.AmountDueToOffice,
                        existing.Remaining);

                HasPreview = true;

                Message = existing.IsClosed
                    ? $"تم العثور على تسوية مغلقة رقم #{existing.Id} — هذه مراجعة فقط ولا يمكن تعديلها."
                    : $"تم العثور على تسوية رقم #{existing.Id}.";
            }
            else
            {
                IsClosedSettlement = false;
                SettlementId = 0;

                SettlementCalculation =
                    await _service.PreviewAsync(
                        SelectedDriver.Id,
                        SelectedSettlementDate,
                        PaidAmount);

                HasPreview = true;

                Message =
                    "تمت معاينة التسوية الجديدة من قاعدة البيانات.";
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
                throw new InvalidOperationException(
                    "اختر المندوب أولًا.");

            if (IsClosedSettlement)
                throw new InvalidOperationException(
                    "هذه التسوية مغلقة بالفعل. يمكنك مراجعتها فقط ولا يمكن حفظها مرة أخرى.");

            if (!HasPreview ||
                SettlementCalculation is null)
            {
                throw new InvalidOperationException(
                    "يجب معاينة التسوية قبل الحفظ.");
            }

            if (PaidAmount < 0)
                throw new InvalidOperationException(
                    "المبلغ المدفوع لا يمكن أن يكون سالبًا.");

            await _service.CreateAsync(
                SelectedDriver.Id,
                SelectedSettlementDate,
                PaidAmount,
                SettlementNotes.Trim(),
                _parent.CurrentUser.Id);

            Message =
                "تم حفظ التسوية وإغلاقها بنجاح.";

            HasPreview = false;
            SettlementCalculation = null;
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }
}

// =============================================================
// Driver Details Page ViewModel
// =============================================================

public partial class DriverDetailsPageVm : ObservableObject
{
    private readonly MainViewModel _parent;
    private readonly IDriverService _service;

    public DriverStatistics Driver { get; private set; }

    [ObservableProperty]
    private ObservableCollection<Order> orders = new();

    [ObservableProperty]
    private string selectedStatusFilter = "الكل";

    [ObservableProperty]
    private string selectedDateFilter = "الكل";

    [ObservableProperty]
    private DateTime? fromDate;

    [ObservableProperty]
    private DateTime? toDate;

    public ObservableCollection<string> StatusFilters { get; } =
        new(new[]
        {
            "الكل",
            "جديد",
            "مع المندوب",
            "في الطريق",
            "تم التسليم",
            "لم يتم التسليم",
            "مرتجع",
            "ملغي"
        });

    public ObservableCollection<string> DateFilters { get; } =
        new(new[]
        {
            "الكل",
            "اليوم",
            "أمس",
            "هذا الأسبوع",
            "هذا الشهر",
            "نطاق مخصص"
        });

    public DriverDetailsPageVm(
        MainViewModel parent,
        IDriverService service,
        DriverStatistics driver)
    {
        _parent = parent;
        _service = service;
        Driver = driver;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        ResolveDates();

        OrderStatus? status =
            SelectedStatusFilter switch
            {
                "جديد" => OrderStatus.New,
                "مع المندوب" => OrderStatus.Assigned,
                "في الطريق" => OrderStatus.OutForDelivery,
                "تم التسليم" => OrderStatus.Delivered,
                "لم يتم التسليم" => OrderStatus.Failed,
                "مرتجع" => OrderStatus.Returned,
                "ملغي" => OrderStatus.Cancelled,
                _ => null
            };

        Orders =
            new ObservableCollection<Order>(
                await _service.GetOrdersAsync(
                    Driver.DriverId,
                    status,
                    FromDate,
                    ToDate));
    }

    partial void OnSelectedDateFilterChanged(
        string value)
    {
        if (value != "نطاق مخصص")
            _ = LoadAsync();
    }

    partial void OnSelectedStatusFilterChanged(
        string value)
    {
        _ = LoadAsync();
    }

    private void ResolveDates()
    {
        var today = DateTime.Today;

        FromDate = null;
        ToDate = null;

        switch (SelectedDateFilter)
        {
            case "اليوم":
                FromDate = ToDate = today;
                break;

            case "أمس":
                FromDate = ToDate = today.AddDays(-1);
                break;

            case "هذا الأسبوع":
                FromDate =
                    today.AddDays(
                        -(int)today.DayOfWeek + 1);

                ToDate = today;
                break;

            case "هذا الشهر":
                FromDate =
                    new DateTime(
                        today.Year,
                        today.Month,
                        1);

                ToDate = today;
                break;

            case "نطاق مخصص":
                break;
        }
    }

    [RelayCommand]
    private async Task ApplyCustomDateRange()
    {
        if (SelectedDateFilter != "نطاق مخصص")
            return;

        if (FromDate.HasValue &&
            ToDate.HasValue &&
            FromDate > ToDate)
        {
            return;
        }

        await LoadAsync();
    }

    [RelayCommand]
    private void Back()
    {
        _parent.CurrentPage =
            new DriversPageVm(_parent);

        _parent.PageTitle = "المناديب";

        _ = _parent.LoadDriverStatisticsAsync();
    }
}

// =============================================================
// Helper Records
// =============================================================

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