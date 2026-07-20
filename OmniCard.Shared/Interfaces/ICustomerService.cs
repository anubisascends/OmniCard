using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface ICustomerService
{
    List<Customer> GetAll();
    Customer? Get(int id);
    Customer Create(Customer customer);
    void Update(Customer customer);
    void Delete(int id);
}
