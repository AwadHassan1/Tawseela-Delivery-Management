# توصيله | Tawseela Delivery Management

برنامج Desktop احترافي لإدارة مكتب الشحن والتوصيل والطرود والمأكولات.

## التقنية
- .NET 10 LTS + WPF (Windows only)
- MVVM + Clean Architecture
- Entity Framework Core + SQL Server
- Dependency Injection
- Serilog
- ClosedXML
- BCrypt password hashing
- xUnit tests

.NET 10 هو إصدار LTS الحالي، وWPF مدعوم رسميًا على .NET. 

## اسم المنتج
**توصيله** هو الاسم الرسمي للبرنامج والشركة ويظهر في شاشة الدخول والواجهة والإعدادات الافتراضية.

## المشاريع
- DeliveryManagement.Domain: Entities, enums, financial rules
- DeliveryManagement.Application: use-case contracts/services
- DeliveryManagement.Infrastructure: business services, logging/password/audit
- DeliveryManagement.Persistence: EF Core DbContext/repositories/seeding
- DeliveryManagement.Presentation: WPF Arabic RTL UI
- DeliveryManagement.UnitTests: financial and status business tests

## التشغيل
1. Windows 10/11 64-bit.
2. Visual Studio 2022/2026 أو .NET 10 SDK.
3. SQL Server Express/Developer.
4. افتح `Tawseela.sln`.
5. نفذ:

```powershell
dotnet restore
dotnet build Tawseela.sln
dotnet test Tawseela.sln
```

6. اضبط الاتصال بقاعدة البيانات من متغير البيئة:

```powershell
$env:TAWSEELA_CONNECTION="Server=.\SQLEXPRESS;Database=TawseelaDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
```

أو عدّل القيمة داخل `App.xaml.cs`.

## قاعدة البيانات
التطبيق ينشئ الجداول تلقائيًا في أول تشغيل باستخدام `EnsureCreated` لتسهيل التشغيل الأول. يوجد أيضًا `scripts/InitialSchema.sql` كـ schema كامل قابل للتنفيذ على SQL Server.

للانتقال إلى EF Core migrations في بيئة التطوير:

```powershell
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate --project src/DeliveryManagement.Persistence --startup-project src/DeliveryManagement.Presentation --output-dir Migrations
dotnet ef database update --project src/DeliveryManagement.Persistence --startup-project src/DeliveryManagement.Presentation
```

في بيئة الإنتاج يفضل استخدام migrations بدل EnsureCreated بعد اعتماد أول Migration.

## الدخول الافتراضي
- Username: `admin`
- Password: `Admin@12345`
- Role: Admin

يجب إجبار المستخدم على تغيير كلمة المرور في أول Login قبل الإنتاج.

## أهم قواعد المال
كل الحسابات المالية تستخدم `decimal` فقط.

`AmountDueToOffice = AmountCollected - DriverCommission - ApprovedExpenses`

`Remaining = AmountDueToOffice - AmountPaid`

لا يدخل في التحصيل إلا الأوردرات ذات الحالة `Delivered`. توجد حماية من تكرار التسوية اليومية للمندوب والتاريخ نفسه.

## التقارير والتصدير
بنية الخدمة مجهزة لإضافة تقارير Excel/طباعة A4 والإيصال الحراري. ClosedXML موجود كاعتماد للمشروع لتوليد ملفات Excel.

## Backup
يمكن ربط خدمة Backup بملفات SQL Server `.bak` من شاشة النسخ الاحتياطي. في بيئة الإنتاج يفضل استخدام SQL Server backup/restore الرسمي مع حساب لديه صلاحيات محددة.

## النشر

```powershell
dotnet publish src/DeliveryManagement.Presentation/DeliveryManagement.Presentation.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

الناتج يكون داخل `bin/Release/net10.0-windows/win-x64/publish`.

## الاختبارات
الاختبارات الحالية تغطي:
- العمولة الثابتة.
- العمولة كنسبة من رسوم التوصيل.
- استبعاد المرتجع من التحصيل.
- منع الانتقال غير المسموح للحالات.

## ملاحظات التطوير
الواجهة الأساسية جاهزة RTL وتستخدم Sidebar وCards وDataGrid وتصميم داكن مريح. الوحدات المالية والأوردرات والبيانات الأساسية مبنية على طبقات منفصلة وقابلة للتوسع.


## UI/UX
تم تصميم واجهة WPF حديثة RTL باسم **توصيله** مع Sidebar يمين، بطاقات Dashboard، جداول DataGrid مخصصة، نماذج إدخال حديثة، حالات وأيقونات واضحة، ومساحات وألوان متناسقة للاستخدام المكتبي الطويل.

## Invoice migration
- Added `src/DeliveryManagement.Persistence/Migrations/20260907185000_AddInvoices.cs` for EF Core migration-based deployments.
- Existing installations that were created with `EnsureCreated` are protected by an idempotent startup schema patch that creates only the missing `Invoices` table and indexes; it does not drop/reset existing data.
- A standalone safe SQL script is available at `scripts/AddInvoiceMigration.sql`.
