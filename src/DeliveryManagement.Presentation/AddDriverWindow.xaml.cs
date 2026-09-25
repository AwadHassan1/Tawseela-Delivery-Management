using DeliveryManagement.Domain;
using DeliveryManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace DeliveryManagement.Presentation;

public partial class AddDriverWindow : Window
{
    public AddDriverWindow()
    {
        InitializeComponent();

        StartDatePicker.SelectedDate = DateTime.Today;
        CommissionTypeBox.SelectedIndex = 0;
        CommissionValueBox.Text = "0";

        Loaded += (_, _) => NameBox.Focus();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;

            // قراءة البيانات
            var name = NameBox.Text.Trim();
            var phone = PhoneBox.Text.Trim();
            var address = AddressBox.Text.Trim();
            var notes = NotesBox.Text.Trim();

            // =========================
            // التحقق من الاسم
            // =========================
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowError("يرجى إدخال اسم المندوب.");
                NameBox.Focus();
                return;
            }

            // =========================
            // التحقق من الهاتف
            // =========================
            if (string.IsNullOrWhiteSpace(phone) || phone.Length < 8)
            {
                ShowError("يرجى إدخال رقم هاتف صحيح.");
                PhoneBox.Focus();
                return;
            }

            // =========================
            // قراءة نوع العمولة
            // =========================
            if (CommissionTypeBox.SelectedItem is not ComboBoxItem selectedItem)
            {
                ShowError("من فضلك اختر نوع العمولة.");
                CommissionTypeBox.Focus();
                return;
            }

            var commissionTag = selectedItem.Tag?.ToString() ?? "None";

            if (!Enum.TryParse<CommissionType>(
                    commissionTag,
                    ignoreCase: true,
                    out var commissionType))
            {
                ShowError("نوع العمولة غير صحيح.");
                CommissionTypeBox.Focus();
                return;
            }

            // =========================
            // قراءة قيمة العمولة
            // =========================
            if (!decimal.TryParse(
                    CommissionValueBox.Text.Trim(),
                    NumberStyles.Number,
                    CultureInfo.CurrentCulture,
                    out var commissionValue))
            {
                ShowError("قيمة العمولة يجب أن تكون رقمًا صحيحًا أو عشريًا.");
                CommissionValueBox.Focus();
                return;
            }

            if (commissionValue < 0)
            {
                ShowError("قيمة العمولة لا يمكن أن تكون أقل من صفر.");
                CommissionValueBox.Focus();
                return;
            }

            // =========================
            // التحقق من النسبة
            // =========================
            if (commissionType is CommissionType.PercentageOfDeliveryFee
                or CommissionType.PercentageOfOrderValue)
            {
                if (commissionValue > 100)
                {
                    ShowError("نسبة العمولة لا يمكن أن تتجاوز 100%.");
                    CommissionValueBox.Focus();
                    return;
                }
            }

            // لو بدون عمولة نخلي القيمة صفر
            if (commissionType == CommissionType.None)
            {
                commissionValue = 0;
            }

            // =========================
            // إنشاء Scope
            // =========================
            using var scope = App.Host.Services.CreateScope();

            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            // =========================
            // منع تكرار رقم الهاتف
            // =========================
            var exists = await db.DeliveryMen
                .AnyAsync(x => x.Phone == phone);

            if (exists)
            {
                ShowError("يوجد مندوب مسجل بالفعل بنفس رقم الهاتف.");
                PhoneBox.Focus();
                return;
            }

            // =========================
            // إنشاء المندوب
            // =========================
            var driver = new DeliveryMan
            {
                Name = name,
                Phone = phone,
                Address = address,
                StartDate = StartDatePicker.SelectedDate ?? DateTime.Today,
                IsActive = ActiveBox.IsChecked == true,
                CommissionType = commissionType,
                CommissionValue = commissionValue,
                Notes = notes
            };

            // =========================
            // الحفظ
            // =========================
            db.DeliveryMen.Add(driver);

            await db.SaveChangesAsync();

            // =========================
            // نجاح الحفظ
            // =========================
            MessageBox.Show(
                "تم إضافة المندوب بنجاح.",
                "توصيله",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Create delivery man failed");

            ShowError(
                "تعذر حفظ المندوب.\n\n" +
                "تأكد من اتصال قاعدة البيانات ثم حاول مرة أخرى.");
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}