/* Safe SQL migration for databases already created by Tawseela.
   Does not drop/reset existing tables or data. */
IF OBJECT_ID(N'Invoices', N'U') IS NULL
BEGIN
    CREATE TABLE Invoices(
        Id INT IDENTITY PRIMARY KEY,
        InvoiceNumber NVARCHAR(100) NOT NULL,
        OrderId INT NOT NULL,
        CustomerName NVARCHAR(200) NOT NULL,
        CustomerPhone NVARCHAR(30) NOT NULL,
        CustomerAddress NVARCHAR(500) NOT NULL,
        Date DATETIME2 NOT NULL,
        OrderType INT NOT NULL,
        OrderValue DECIMAL(18,2) NOT NULL,
        DeliveryFee DECIMAL(18,2) NOT NULL,
        Total DECIMAL(18,2) NOT NULL,
        OrderStatus INT NOT NULL,
        DeliveryManName NVARCHAR(200) NOT NULL,
        CollectedAmount DECIMAL(18,2) NOT NULL,
        IsCollected BIT NOT NULL,
        Notes NVARCHAR(1000) NOT NULL,
        CONSTRAINT FK_Invoices_Orders FOREIGN KEY(OrderId) REFERENCES Orders(Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX IX_Invoices_InvoiceNumber ON Invoices(InvoiceNumber);
    CREATE UNIQUE INDEX IX_Invoices_OrderId ON Invoices(OrderId);
END
GO
