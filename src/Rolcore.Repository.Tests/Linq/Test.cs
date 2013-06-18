using Rolcore.Repository.Tests.Mocks;
using System;
using System.Linq;
using System.Data.Linq;
using Rolcore.Repository.LinqImpl;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;

namespace Rolcore.Repository.Tests.Linq
{
    public sealed partial class TestDataContext : IRepository<MockEntity<Binary>, Binary>
    {
        Repository<MockEntity<System.Data.Linq.Binary>, Binary> _TestItemsRepository;

        partial void OnCreated()
        {
            _TestItemsRepository = new LinqRepository<TestItem, MockEntity<Binary>, Binary>(
                TestItems,
                (entity, key, concurrency, pKey) =>
                {
                    entity.RowKey = key;
                    entity.Timestamp = concurrency;
                },
                (entity) => 
                    (entity.RowKey != null) && (_TestItemsRepository.Items.Any(item => item.RowKey == entity.RowKey)));
        }

        IQueryable<MockEntity<Binary>> IRepositoryReader<MockEntity<Binary>>.Items
        {
            get { return _TestItemsRepository.Items; }
        }

        void IRepositoryWriter<MockEntity<Binary>, Binary>.ApplyRules(params MockEntity<Binary>[] items)
        {
            this.ApplyRulesDefaultImplementation(items);
        }

        MockEntity<Binary>[] IRepositoryWriter<MockEntity<Binary>, Binary>.Insert(params MockEntity<Binary>[] items)
        {
            return _TestItemsRepository.Insert(items);
        }

        MockEntity<Binary>[] IRepositoryWriter<MockEntity<Binary>, Binary>.Update(params MockEntity<Binary>[] items)
        {
            return _TestItemsRepository.Update(items);
        }

        MockEntity<Binary>[] IRepositoryWriter<MockEntity<Binary>, Binary>.Save(params MockEntity<Binary>[] items)
        {
            return _TestItemsRepository.Save(items);
        }

        int IRepositoryWriter<MockEntity<Binary>, Binary>.Delete(params MockEntity<Binary>[] items)
        {
            return _TestItemsRepository.Delete(items);
        }

        int IRepositoryWriter<MockEntity<Binary>, Binary>.Delete(string rowKey, Binary concurrency, string partitionKey)
        {
            return _TestItemsRepository.Delete(rowKey, concurrency, partitionKey);
        }

        [ImportMany]
        IEnumerable<IRepositoryItemRule<MockEntity<Binary>>> IRepositoryWriter<MockEntity<Binary>, Binary>.Rules
        {
            get
            {
                return _TestItemsRepository.Rules;
            }
            set
            {
                _TestItemsRepository.Rules = value;
            }
        }
    }

    public partial class TestItem : MockEntity<Binary>
    {
        partial void OnValidate(ChangeAction action)
        {
            if (action == ChangeAction.Insert && RowKey == null)
                RowKey = Guid.NewGuid().ToString();
        }

        public override string ToString()
        {
            return string.Format("RowKey: {0} PartitionKey: {1}", this.RowKey, this.PartitionKey);
        }
    }
}
