using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Sales;

/// <summary>
/// Backs the Customers tab: a simple CRUD editor over <see cref="ICustomerService"/>.
/// </summary>
public partial class CustomersViewModel(ICustomerService customerService) : ObservableObject
{
    public ObservableCollection<Customer> Customers { get; } = [];

    [ObservableProperty]
    public partial Customer? SelectedCustomer { get; set; }

    /// <summary>Loads all customers. Safe to call repeatedly (e.g. on every tab activation).</summary>
    public void Load()
    {
        Customers.Clear();
        foreach (var c in customerService.GetAll())
            Customers.Add(c);
    }

    [RelayCommand]
    public void NewCustomer() => SelectedCustomer = new Customer { Name = "New Customer" };

    [RelayCommand]
    public void Save()
    {
        if (SelectedCustomer is null || string.IsNullOrWhiteSpace(SelectedCustomer.Name)) return;

        int savedId;
        if (SelectedCustomer.Id == 0)
        {
            var saved = customerService.Create(SelectedCustomer);
            savedId = saved.Id;
        }
        else
        {
            customerService.Update(SelectedCustomer);
            savedId = SelectedCustomer.Id;
        }

        Load();
        SelectedCustomer = Customers.FirstOrDefault(c => c.Id == savedId);
    }

    [RelayCommand]
    public void Delete()
    {
        if (SelectedCustomer is { Id: > 0 } c)
        {
            customerService.Delete(c.Id);
            SelectedCustomer = null;
            Load();
        }
    }
}
