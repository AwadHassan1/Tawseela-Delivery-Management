using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace DeliveryManagement.Presentation;

public partial class MainWindow : Window
{
    private async void OrderStatusComboBox_DropDownClosed(
    object sender,
    EventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox combo)
            return;

        if (combo.DataContext is not DeliveryManagement.Domain.Order order)
            return;

        if (combo.SelectedValue is not DeliveryManagement.Domain.OrderStatus newStatus)
            return;

        // لا يوجد تغيير
        if (newStatus == order.Status)
            return;

        if (DataContext is not MainViewModel vm)
            return;

        await vm.ChangeStatus(order, newStatus);

        // إذا لم يتم التغيير فعليًا بسبب Business Rules
        // نعيد الـComboBox للحالة الأصلية
        if (order.Status != newStatus)
        {
            combo.SelectedValue = order.Status;
        }
    }
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm) oldVm.InvoiceRequested -= ShowInvoice;
        if (e.NewValue is MainViewModel newVm) newVm.InvoiceRequested += ShowInvoice;
    }

    private void ShowInvoice(DeliveryManagement.Domain.Invoice invoice)
    {
        var window = new InvoiceWindow(invoice) { Owner = this };
        window.ShowDialog();
    }

    private async void AddDriver_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddDriverWindow { Owner = this };
        if (dialog.ShowDialog() == true && DataContext is MainViewModel vm)
        {
            await vm.LoadDriversAsync();
            await vm.LoadDriverStatisticsAsync();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.InvoiceRequested -= ShowInvoice;
        base.OnClosed(e);
    }
}
