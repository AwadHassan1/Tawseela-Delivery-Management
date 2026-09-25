using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace DeliveryManagement.Presentation;

public partial class MainWindow : Window
{
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
