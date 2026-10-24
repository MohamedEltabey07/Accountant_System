using DAL.IRepos;
using System;
using System.Collections.Generic;
using System.Text;

namespace DAL.IConfiguration
{
    public interface IUnitOfWork : IDisposable
    {
        public IProductRepo ProductRepo { get; }
        public ICustomerRepo CustomerRepo { get; }
        public IVendorRepo Vendors { get; }
        public IInvoiceRepo InvoiceRepo { get; }
        Task SaveChangesAsync();
        Task BeginTransactionAsync();
        Task CommitTransactionAsync();
        Task RollbackTransactionAsync();
    }
}
