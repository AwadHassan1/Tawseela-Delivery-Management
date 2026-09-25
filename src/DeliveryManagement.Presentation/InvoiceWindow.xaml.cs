using System.Windows;
using DeliveryManagement.Domain;
namespace DeliveryManagement.Presentation;
public partial class InvoiceWindow : Window
{
    public InvoiceWindow(Invoice invoice){ InitializeComponent(); DataContext=invoice; }
    private void Print_Click(object sender,RoutedEventArgs e){ try { var dialog=new System.Windows.Controls.PrintDialog(); if(dialog.ShowDialog()==true) dialog.PrintVisual(InvoicePaper,$"فاتورة {((Invoice)DataContext).InvoiceNumber}"); } catch(Exception ex){ MessageBox.Show($"تعذر الطباعة: {ex.Message}","توصيله",MessageBoxButton.OK,MessageBoxImage.Error); } }
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
}
