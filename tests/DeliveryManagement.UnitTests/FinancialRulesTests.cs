using Xunit;
using DeliveryManagement.Domain;
namespace DeliveryManagement.UnitTests;
public class FinancialRulesTests
{
    [Fact] public void FixedCommission_IsCalculated(){var d=new DeliveryMan{CommissionType=CommissionType.FixedPerOrder,CommissionValue=10};var c=FinancialRules.CalculateCommission(d,5,500,150);Assert.Equal(50,c);}
    [Fact] public void PercentageDeliveryFee_IsCalculated(){var d=new DeliveryMan{CommissionType=CommissionType.PercentageOfDeliveryFee,CommissionValue=20};Assert.Equal(30,FinancialRules.CalculateCommission(d,5,500,150));}
    [Fact] public void Settlement_ExcludesNonDelivered(){var d=new DeliveryMan{CommissionType=CommissionType.None};var orders=new[]{new Order{Status=OrderStatus.Delivered,OrderValue=500,DeliveryFee=30,CollectedAmount=530},new Order{Status=OrderStatus.Returned,OrderValue=100,DeliveryFee=20,CollectedAmount=0}};var c=FinancialRules.CalculateSettlement(d,orders,10,500);Assert.Equal(530,c.Collected);Assert.Equal(520,c.Due);Assert.Equal(20,c.Remaining);}
    [Fact] public void InvalidStatusTransition_IsRejected(){Assert.False(FinancialRules.CanTransition(OrderStatus.Cancelled,OrderStatus.Delivered,false));}
}
