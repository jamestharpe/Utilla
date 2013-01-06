﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure.StorageClient;
using System.Data.Services.Client;
using System.Data;
using System.Diagnostics;
using Microsoft.WindowsAzure;
using Rolcore.Reflection;

namespace Rolcore.Repository.WindowsAzure.StorageClientImpl
{
    public class TableServiceContextRepositoryWriter<TItem> 
        : TableServiceContextRepositoryBase, 
          IRepositoryWriter<TItem, DateTime>
        where TItem : class
    {
        #region DataServiceClientException Handling Methods
        private IEnumerable<TItem> Handle404DataServiceClientException(TItem[] items, DataServiceRequestException ex)
        {
            //
            // Force "insert or replace" to work on the local storage emulator!
            // From http://www.windowsazure.com/en-us/develop/net/how-to-guides/table-services/#insert-entity:
            // "Note that insert-or-replace is not supported on the local storage emulator,
            // so this code runs only when using an account on the table service."

            Trace.TraceWarning("Local dev storage detected. If you are reading this in production, you may wish to freak out.");
            Trace.Indent();
            Trace.TraceError(ex.ToString());
            Trace.TraceInformation("More information at http://www.windowsazure.com/en-us/develop/net/how-to-guides/table-services/#insert-entity");
            Trace.Unindent();

            var context = CloneContext();

            foreach (dynamic item in items)
            {
                var entityCheat = new EntityCheat() { PartitionKey = item.PartitionKey, RowKey = item.RowKey };
                var existingEntity = context.CreateQuery<EntityCheat>(EntitySetName)
                    .Where(e =>
                        e.PartitionKey == entityCheat.PartitionKey
                     && e.RowKey == entityCheat.RowKey)
                    .SingleOrDefault();
                if (existingEntity == null)
                {
                    Context.Detach(item);
                    Context.AddObject(EntitySetName, item);
                }
                else
                {
                    Context.Detach(existingEntity);
                    Context.AttachTo(EntitySetName, item, "*");
                    Context.UpdateObject(item);
                }
            }

            Context.SaveChangesWithRetries(SaveChangesOptions.ReplaceOnUpdate);
            return items;
        }
        #endregion DataServiceClientException Handling Methods

        #region Constructors
        public TableServiceContextRepositoryWriter(TableServiceContext context, string entitySetName)
            : base(context, entitySetName)
        {
            
        } // Tested

        public TableServiceContextRepositoryWriter(CloudTableClient client, string entitySetName)
            : base(client, entitySetName)
        {
        } // Tested

        public TableServiceContextRepositoryWriter(CloudStorageAccount storageAccount, string entitySetName)
            : base(storageAccount, entitySetName)
        {
        } // Tested

        public TableServiceContextRepositoryWriter(string connectionString, string entitySetName)
            : base(connectionString, entitySetName)
        {
        } // Tested
        #endregion Constructors

        TableServiceContext CloneContext()
        {
            var result = new TableServiceContext(Context.BaseUri.ToString(), Context.StorageCredentials);
            Context.CopyMatchingObjectPropertiesTo(result);
            result.IgnoreResourceNotFoundException = true; // prevents SingleOrDefault from throwing an exception
            return result;
        }

        string GetETag(TItem item)
        {
            dynamic dynItem = item;
            return Context.Entities
                .Where(e =>
                    ((dynamic)e.Entity).PartitionKey == dynItem.PartitionKey
                    && ((dynamic)e.Entity).RowKey == dynItem.RowKey)
                .Select(e => e.ETag)
                .SingleOrDefault();
        }

        void AttachTo(TableServiceContext context, TItem item)
        {
            EnsureRowKey(item);

            var eTag = GetETag(item);

            if (eTag != null)
                context.AttachTo(EntitySetName, item, eTag);
            else
                context.AttachTo(EntitySetName, item);
        }

        private static void EnsureRowKey(TItem item)
        {
            dynamic dynItem = (dynamic)item;
            if (string.IsNullOrEmpty(dynItem.RowKey))
            {
                var rowKey = Guid.NewGuid().ToString();
                dynItem.RowKey = rowKey;
                Trace.TraceWarning(
                    string.Format(
                        "Item {0} probably should have had a value for RowKey when it was saved but didn't, so one was generated.",
                        rowKey));
            }
        }

        public IEnumerable<TItem> Save(params TItem[] items)
        {
            var context = CloneContext();
            foreach (TItem item in items)
            {
                AttachTo(context, item);
                context.UpdateObject(item);
            }

            try
            {
                context.SaveChangesWithRetries(SaveChangesOptions.ReplaceOnUpdate);
                return items;
            }
            catch (DataServiceRequestException ex)
            {
                var innerException = ex.InnerException as DataServiceClientException;

                if(innerException == null)
                    throw;

                //
                // Exceptions: http://technet.microsoft.com/en-us/library/dd179438.aspx

                else if (innerException.StatusCode == 404) // 404 = "Not Found"
                    return Handle404DataServiceClientException(items, ex);
                else if (innerException.StatusCode == 412) // UpdateConditionNotSatisfied (concurrency)
                {
                    throw new DBConcurrencyException(
                        "Record has been modified outside the current save operation.", 
                        innerException);
                }
                else
                    throw;
            }
        }// Tested

        public IEnumerable<TItem> Insert(params TItem[] items)
        {
            var context = CloneContext();
            var result = new List<TItem>(items.Length);
            foreach (TItem item in items)
            {
                EnsureRowKey(item);
                context.AddObject(EntitySetName, item);
            }

            context.SaveChangesWithRetries(SaveChangesOptions.Batch);

            return result;
        } //TODO: Test

        public IEnumerable<TItem> Update(params TItem[] items)
        {
            var context = CloneContext();
            var result = new List<TItem>(items.Length);
            foreach (TItem item in items)
                context.UpdateObject(item);

            context.SaveChangesWithRetries(SaveChangesOptions.Batch);

            return result;
        }

        public int Delete(params TItem[] items)
        {
            var context = CloneContext();
            foreach (var item in items)
            {
                AttachTo(context, item);
                context.DeleteObject(item);
            }

            context.SaveChangesWithRetries(SaveChangesOptions.Batch);

            return items.Count();

            
        }// Tested

        public int Delete(string rowKey, DateTime concurrency, string partitionKey)
        {

            var itemsToDelete = Context.CreateQuery<TItem>(EntitySetName).AsEnumerable()
                .Where(item => 
                    (item as dynamic).RowKey == rowKey &&
                    (item as dynamic).Timestamp == concurrency);

            if (partitionKey != null)
                itemsToDelete = itemsToDelete
                    .Where(item =>
                        (item as dynamic).PartitionKey == partitionKey);


            if (itemsToDelete.Any())
                return Delete(itemsToDelete.ToArray());

            return 0;
        } // Tested
    }
}