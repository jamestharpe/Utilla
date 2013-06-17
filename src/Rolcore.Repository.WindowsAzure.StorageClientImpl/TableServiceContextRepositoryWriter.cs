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
using System.ComponentModel.Composition;

namespace Rolcore.Repository.WindowsAzure.StorageClientImpl
{
    /// <summary>
    /// Implements <see cref="IRepositoryWriter{}"/> using a <see cref="TableServiceContext"/> as 
    /// the storage mechanism.
    /// </summary>
    /// <typeparam name="TItem">Specifies the type of item stored in the repository.</typeparam>
    public class TableServiceContextRepositoryWriter<TItem> 
        : TableServiceContextRepositoryBase, 
          IRepositoryWriter<TItem, DateTime>
        where TItem : class
    {
        #region DataServiceClientException Handling Methods
        /// <summary>
        /// Works around issues with the Azure storage emulator that cause a HTTP 400 response.
        /// </summary>
        /// <param name="items">Specifies the items on which the exception occurred.</param>
        /// <param name="ex">Specifies the exception</param>
        /// <returns>The persisted items.</returns>
        private TItem[] Handle400DataServiceClientException(TItem[] items, DataServiceRequestException ex)
        {
            // This sometimes happens during an update in dev storage during upsert, though it's 
            // not really clear why.

            Trace.TraceWarning("Local dev storage detected. If you are reading this in production, you may wish to freak out.");
            Trace.Indent();
            Trace.TraceError(ex.ToString());
            Trace.Unindent();

            return this.Update(items);
        }

        /// <summary>
        /// Force "insert or replace" to work on the local storage emulator. From 
        /// http://www.windowsazure.com/en-us/develop/net/how-to-guides/table-services/#insert-entity:
        /// "Note that insert-or-replace is not supported on the local storage emulator, so this 
        /// code runs only when using an account on the table service."
        /// </summary>
        /// <param name="items">Specifies the items on which the exception occurred.</param>
        /// <param name="ex">Specifies the exception</param>
        /// <returns>The persisted items</returns>
        private TItem[] Handle404DataServiceClientException(TItem[] items, DataServiceRequestException ex)
        {
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
        /// <summary>
        /// Initializes a new instance of the <see cref="TableServiceContextRepositoryWriter"/> class.
        /// </summary>
        /// <param name="context">The value for <see cref="Context"/>.</param>
        /// <param name="entitySetName">The value for <see cref="EntitySetName"/>.</param>
        public TableServiceContextRepositoryWriter(TableServiceContext context, string entitySetName)
            : base(context, entitySetName)
        {
            
        } // Tested

        /// <summary>
        /// Initializes a new instance of the <see cref="TableServiceContextRepositoryWriter"/> class.
        /// </summary>
        /// <param name="client">A <see cref="CloudTableClient"/> that provides access to the 
        /// backing <see cref="TableServiceContext"/></param>
        /// <param name="entitySetName">The value for <see cref="EntitySetName"/>.</param>
        public TableServiceContextRepositoryWriter(CloudTableClient client, string entitySetName)
            : base(client, entitySetName)
        {
        } // Tested

        /// <summary>
        /// Initializes a new instance of the <see cref="TableServiceContextRepositoryWriter"/> class.
        /// </summary>
        /// <param name="storageAccount">Specifies the <see cref="CloudStorageAccount"/> in which 
        /// entities are to be stored.</param>
        /// <param name="entitySetName">The value for <see cref="EntitySetName"/>.</param>
        public TableServiceContextRepositoryWriter(CloudStorageAccount storageAccount, string entitySetName)
            : base(storageAccount, entitySetName)
        {
        } // Tested

        /// <summary>
        /// Initializes a new <see cref="TableServiceContextRepositoryBase"/>.
        /// </summary>
        /// <param name="connectionString">Specifies the connection string to the cloud storage 
        /// account in which entities are to be stored.</param>
        /// <param name="entitySetName">The value for <see cref="EntitySetName"/>.</param>
        public TableServiceContextRepositoryWriter(string connectionString, string entitySetName)
            : base(connectionString, entitySetName)
        {
        } // Tested
        #endregion Constructors

        #region Private Methods
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

        /// <summary>
        /// Clones the backing <see cref="TableServiceContext"/>.
        /// </summary>
        /// <returns>A copy of <see cref="Context"/>.</returns>
        private TableServiceContext CloneContext()
        {
            var result = new TableServiceContext(this.Context.BaseUri.ToString(), Context.StorageCredentials);
            Context.CopyMatchingObjectPropertiesTo(result);
            result.IgnoreResourceNotFoundException = true; // prevents SingleOrDefault from throwing an exception
            return result;
        }

        /// <summary>
        /// Gets the specified items ETag.
        /// </summary>
        /// <param name="item">Specifies the item.</param>
        /// <returns>The ETag value of the item.</returns>
        private string GetETag(TItem item)
        {
            dynamic dynItem = item;
            return Context.Entities
                .Where(e =>
                    ((dynamic)e.Entity).PartitionKey == dynItem.PartitionKey
                    && ((dynamic)e.Entity).RowKey == dynItem.RowKey)
                .Select(e => e.ETag)
                .SingleOrDefault();
        }

        /// <summary>
        /// Attaches the specified item to the specified context.
        /// </summary>
        /// <param name="context">The context to attach the item to.</param>
        /// <param name="item">The item to attach.</param>
        private void AttachTo(TableServiceContext context, TItem item)
        {
            EnsureRowKey(item);

            var eTag = GetETag(item);

            if (eTag != null)
                context.AttachTo(EntitySetName, item, eTag);
            else
                context.AttachTo(EntitySetName, item);
        }
        #endregion Private Methods

        /// <summary>
        /// Applies associated <see cref="Rules"/> to the specified items.
        /// </summary>
        /// <param name="items">Specifies the items to apply rules to.</param>
        public void ApplyRules(params TItem[] items)
        {
            this.ApplyRulesDefaultImplementation(items);
        }

        /// <summary>
        /// Inserts or updates the specified items in the repository.
        /// </summary>
        /// <param name="items">Specifies the items to insert or update.</param>
        /// <returns>The saved items. Note that depending on the implementation, the result may 
        /// be copies of items passed in; the items therefore may not reflect changes caused by
        /// the backing repository (for example, an auto-generated key).</returns>
        public TItem[] Save(params TItem[] items)
        {
            this.ApplyRules(items);
            var context = CloneContext();
            foreach (TItem item in items)
            {
                this.AttachTo(context, item);
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

                if (innerException == null)
                {
                    throw;
                }
                // Exceptions: http://technet.microsoft.com/en-us/library/dd179438.aspx
                else if (innerException.StatusCode == 400) // 400 = "Bad Request"
                {
                    return Handle400DataServiceClientException(items, ex);
                }
                else if (innerException.StatusCode == 404 || innerException.StatusCode == 400) // 404 = "Not Found", 400 = "Bad Request"
                {
                    return Handle404DataServiceClientException(items, ex);
                }
                else if (innerException.StatusCode == 412) // UpdateConditionNotSatisfied (concurrency)
                {
                    throw new DBConcurrencyException(
                        "Record has been modified outside the current save operation.",
                        innerException);
                }
                else
                    throw;
            }
        } // Tested

        /// <summary>
        /// Inserts the specified items.
        /// </summary>
        /// <param name="items">Specifies the items to insert.</param>
        /// <returns>The inserted items.</returns>
        public TItem[] Insert(params TItem[] items)
        {
            this.ApplyRules(items);
            var context = CloneContext();
            var result = new List<TItem>(items.Length);
            foreach (TItem item in items)
            {
                EnsureRowKey(item);
                context.AddObject(EntitySetName, item);
                result.Add(item);
            }

            context.SaveChangesWithRetries(SaveChangesOptions.Batch);

            return result.ToArray();
        } // Tested

        /// <summary>
        /// Inserts the specified items.
        /// </summary>
        /// <param name="items">Specifies the items to insert.</param>
        /// <returns>The inserted items.</returns>
        public TItem[] Update(params TItem[] items)
        {
            this.ApplyRules(items);
            var context = CloneContext();
            var result = new List<TItem>(items.Length);
            foreach (TItem item in items)
            {
                this.AttachTo(context, item);
                context.UpdateObject(item);
                result.Add(item);
            }

            context.SaveChangesWithRetries(SaveChangesOptions.Batch);

            return result.ToArray();
        } // TODO: Test

        /// <summary>
        /// Deletes the specified items in the repository and returns the number of items deleted.
        /// </summary>
        /// <param name="items">Specifies the items to delete.</param>
        /// <returns>The number of items deleted.</returns>
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

        /// <summary>
        /// Deletes the items specified by the given row key, concurrency, and (optional) partition 
        /// key values and returns the number of items deleted. Note that though the most common 
        /// use for this method is to delete a single item, this MAY result in multiple items being
        /// deleted if the partitionKey argument is not specified.
        /// </summary>
        /// <param name="rowKey">Specifies the row key (unique identifier) of the item to delete.</param>
        /// <param name="concurrency">Specifies the value to check for optimistic concurrency.</param>
        /// <param name="partitionKey">Specifies the partition on which the item exists; typically, 
        /// this argument is only used distributed repositories such as Azure's table service.</param>
        /// <returns>The number of items deleted.</returns>
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

        /// <summary>
        /// Gets or sets the rules to apply to items prior to insert or update operations.
        /// </summary>
        [ImportMany]
        public IEnumerable<IRepositoryItemRule<TItem>> Rules { get; set; }
    }
}
