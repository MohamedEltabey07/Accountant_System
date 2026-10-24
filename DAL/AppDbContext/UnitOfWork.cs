using DAL.Entities;
using DAL.IConfiguration;
using DAL.IRepos;
using DAL.Repos;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace DAL.AppDbContext
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly AppDbcontext _context;

        public IProductRepo ProductRepo { get; }

        public ICustomerRepo CustomerRepo { get; }

        public IVendorRepo Vendors { get; }

        public IInvoiceRepo InvoiceRepo { get; }

        public UnitOfWork(AppDbcontext context)
        {
            _context = context;
            ProductRepo = new ProductRepo(context);
            CustomerRepo = new CustomerRepo(context);
            Vendors = new VendorRepo(context);
            InvoiceRepo = new InvoiceRepo(context);
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }

        public async Task BeginTransactionAsync()
        {
            await _context.Database.BeginTransactionAsync();
        }

        public async Task CommitTransactionAsync()
        {
            if (_context.Database.CurrentTransaction != null)
                await _context.Database.CommitTransactionAsync();
        }

        public async Task RollbackTransactionAsync()
        {
            if (_context.Database.CurrentTransaction != null)
                await _context.Database.RollbackTransactionAsync();
        }

        public void Dispose()
        {
            _context.Dispose();
        }
    }
}
