using System.Globalization;
using System.Windows.Data;
using DeliveryManagement.Domain;

namespace DeliveryManagement.Presentation;

public sealed class OrderStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        OrderStatus.New => "جديد",
        OrderStatus.Assigned => "مع المندوب",
        OrderStatus.OutForDelivery => "في الطريق",
        OrderStatus.Delivered => "تم التسليم",
        OrderStatus.Failed => "لم يتم التسليم",
        OrderStatus.Returned => "مرتجع",
        OrderStatus.Cancelled => "ملغي",
        _ => value?.ToString() ?? ""
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class OrderTypeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        OrderType.Parcel => "طرد",
        OrderType.Food => "مأكولات",
        OrderType.Documents => "مستندات",
        OrderType.Requests => "طلبات",
        OrderType.Shipping => "شحن",
        OrderType.Other => "أخرى",
        _ => value?.ToString() ?? ""
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CommissionTypeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        CommissionType.None => "بدون عمولة",
        CommissionType.FixedPerOrder => "مبلغ ثابت / أوردر",
        CommissionType.PercentageOfDeliveryFee => "نسبة من التوصيل",
        CommissionType.PercentageOfOrderValue => "نسبة من قيمة الأوردر",
        _ => value?.ToString() ?? ""
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}



public sealed class ComboBoxDisplayValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return string.Empty;
        if (value is string text) return text;

        var nameProperty = value.GetType().GetProperty("Name");
        var name = nameProperty?.GetValue(value)?.ToString();
        if (!string.IsNullOrWhiteSpace(name)) return name;

        return value.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StringEqualsVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
