using System.Windows;
using DeliveryManagement.Domain;
using DeliveryManagement.Persistence;
using DeliveryManagement.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DeliveryManagement.Presentation;
public partial class LoginWindow : Window
{
    public LoginWindow(){InitializeComponent();}
    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var scope = App.Host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pass = scope.ServiceProvider.GetRequiredService<IPasswordService>();
            var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == UserName.Text.Trim() && u.IsActive);
            if (user is null || !pass.Verify(Password.Password, user.PasswordHash)) { ErrorText.Text = "اسم المستخدم أو كلمة المرور غير صحيحة."; return; }
            var main = App.Host.Services.GetRequiredService<MainWindow>();
            main.DataContext = App.Host.Services.GetRequiredService<MainViewModel>();
            ((MainViewModel)main.DataContext).Initialize(user);
            main.Show(); Close();
        }
        catch(Exception ex){ErrorText.Text = "حدث خطأ أثناء الدخول. تم تسجيل التفاصيل."; Serilog.Log.Error(ex,"Login error");}

    }
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
