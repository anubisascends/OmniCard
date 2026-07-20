using Moq;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.Sales;
using Xunit;

namespace OmniCard.Tests.Views.Sales;

public class CustomersViewModelTests
{
    private static Customer Cust(int id, string name) => new() { Id = id, Name = name };

    [Fact]
    public void Load_PopulatesCustomers()
    {
        var service = new Mock<ICustomerService>();
        service.Setup(s => s.GetAll()).Returns([Cust(1, "Alice"), Cust(2, "Bob")]);

        var vm = new CustomersViewModel(service.Object);
        vm.Load();

        Assert.Equal(2, vm.Customers.Count);
        Assert.Equal("Alice", vm.Customers[0].Name);
    }

    [Fact]
    public void NewCustomer_SetsSelectedCustomer_ToUnsavedNewCustomer()
    {
        var service = new Mock<ICustomerService>();
        var vm = new CustomersViewModel(service.Object);

        vm.NewCustomer();

        Assert.NotNull(vm.SelectedCustomer);
        Assert.Equal(0, vm.SelectedCustomer!.Id);
        Assert.Equal("New Customer", vm.SelectedCustomer.Name);
    }

    [Fact]
    public void Save_WithNewCustomer_CallsCreate_ThenReloads()
    {
        var service = new Mock<ICustomerService>();
        service.Setup(s => s.GetAll()).Returns([Cust(1, "Alice")]);

        var vm = new CustomersViewModel(service.Object);
        vm.SelectedCustomer = new Customer { Id = 0, Name = "Alice" };

        vm.Save();

        service.Verify(s => s.Create(It.Is<Customer>(c => c.Name == "Alice")), Times.Once);
        service.Verify(s => s.Update(It.IsAny<Customer>()), Times.Never);
        Assert.Single(vm.Customers);
    }

    [Fact]
    public void Save_WithExistingCustomer_CallsUpdate()
    {
        var service = new Mock<ICustomerService>();
        service.Setup(s => s.GetAll()).Returns([]);

        var vm = new CustomersViewModel(service.Object);
        vm.SelectedCustomer = new Customer { Id = 5, Name = "Bob" };

        vm.Save();

        service.Verify(s => s.Update(It.Is<Customer>(c => c.Id == 5)), Times.Once);
        service.Verify(s => s.Create(It.IsAny<Customer>()), Times.Never);
    }

    [Fact]
    public void Save_WithBlankName_DoesNotCallService()
    {
        var service = new Mock<ICustomerService>();
        var vm = new CustomersViewModel(service.Object);
        vm.SelectedCustomer = new Customer { Name = "  " };

        vm.Save();

        service.Verify(s => s.Create(It.IsAny<Customer>()), Times.Never);
        service.Verify(s => s.Update(It.IsAny<Customer>()), Times.Never);
    }

    [Fact]
    public void Save_WithNoSelectedCustomer_DoesNotThrow_AndDoesNotCallService()
    {
        var service = new Mock<ICustomerService>();
        var vm = new CustomersViewModel(service.Object);

        var ex = Record.Exception(() => vm.Save());

        Assert.Null(ex);
        service.Verify(s => s.Create(It.IsAny<Customer>()), Times.Never);
    }

    [Fact]
    public void Delete_WithSavedCustomerSelected_CallsDelete_ClearsSelection_AndReloads()
    {
        var service = new Mock<ICustomerService>();
        service.Setup(s => s.GetAll()).Returns([]);

        var vm = new CustomersViewModel(service.Object);
        vm.SelectedCustomer = Cust(7, "Alice");

        vm.Delete();

        service.Verify(s => s.Delete(7), Times.Once);
        Assert.Null(vm.SelectedCustomer);
    }

    [Fact]
    public void Delete_WithUnsavedCustomerSelected_DoesNotCallService()
    {
        var service = new Mock<ICustomerService>();
        var vm = new CustomersViewModel(service.Object);
        vm.SelectedCustomer = new Customer { Id = 0, Name = "New Customer" };

        vm.Delete();

        service.Verify(s => s.Delete(It.IsAny<int>()), Times.Never);
        Assert.NotNull(vm.SelectedCustomer);
    }
}
