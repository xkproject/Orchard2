using Dapper;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using YesSql.Commands;
using YesSql.Data;
using YesSql.Indexes;
using YesSql.Services;

namespace YesSql
{
    public class Session : ISession
    {
        private DbTransaction _transaction;

        internal readonly List<IIndexCommand> _commands = new List<IIndexCommand>();
        private readonly Dictionary<string, SessionState> CollectionStates;
        private readonly SessionState _defaultState;
        protected readonly Dictionary<string, IEnumerable<IndexDescriptor>> _descriptors = new Dictionary<string, IEnumerable<IndexDescriptor>>();
        internal readonly Store _store;
        private volatile bool _disposed;
        private bool _flushing;
        private IsolationLevel _isolationLevel;
        private DbConnection _connection;
        protected bool _cancel;
        protected List<IIndexProvider> _indexes;

        protected string _tablePrefix;
        private ISqlDialect _dialect;
        private ILogger _logger;

        public Session(Store store, IsolationLevel isolationLevel)
        {
            _store = store;
            _isolationLevel = isolationLevel;
            _tablePrefix = _store.Configuration.TablePrefix;
            _dialect = store.Dialect;
            _logger = store.Configuration.Logger;

            _defaultState = new SessionState();
            CollectionStates = new Dictionary<string, SessionState>()
            {
                [""] = _defaultState
            };
        }

        public ISession RegisterIndexes(IIndexProvider[] indexProviders, string collection = null)
        {
            foreach (var indexProvider in indexProviders)
            {
                if (indexProvider.CollectionName == null)
                {
                    indexProvider.CollectionName = collection ?? "";
                }
            }

            if (_indexes == null)
            {
                _indexes = new List<IIndexProvider>();
            }

            _indexes.AddRange(indexProviders);

            return this;
        }

        private SessionState GetState(string collection)
        {
            if (String.IsNullOrEmpty(collection))
            {
                return _defaultState;
            }

            if (!CollectionStates.TryGetValue(collection, out var state))
            {
                state = new SessionState();
                CollectionStates[collection] = state;
            }

            return state;
        }

        public void Save(object entity, bool checkConcurrency = false, string collection = null)
        {
            var state = GetState(collection);

            CheckDisposed();

            // already being saved or updated or tracked?
            if (state.Saved.Contains(entity) || state.Updated.Contains(entity))
            {
                return;
            }

            // remove from tracked entities if explicitly saved
            state.Tracked.Remove(entity);

            // is it a new object?
            if (state.IdentityMap.TryGetDocumentId(entity, out var id))
            {
                state.Updated.Add(entity);

                // If this entity needs to be checked for concurrency, track its version
                if (checkConcurrency || _store.Configuration.ConcurrentTypes.Contains(entity.GetType()))
                {
                    state.Concurrent.Add(id);
                }

                return;
            }

            // Does it have a valid identifier?
            var accessor = _store.GetIdAccessor(entity.GetType());
            if (accessor != null)
            {
                id = accessor.Get(entity);

                if (id > 0)
                {
                    state.IdentityMap.AddEntity(id, entity);
                    state.Updated.Add(entity);
                    
                    // If this entity needs to be checked for concurrency, track its version
                    if (checkConcurrency || _store.Configuration.ConcurrentTypes.Contains(entity.GetType()))
                    {
                        state.Concurrent.Add(id);
                    }

                    return;
                }
            }

            // It's a new entity
            id = _store.GetNextId(collection);
            state.IdentityMap.AddEntity(id, entity);

            // Then assign a new identifier if it has one
            if (accessor != null)
            {
                accessor.Set(entity, id);
            }

            state.Saved.Add(entity);
        }

        public bool Import(object entity, int id = 0, int version = 0, string collection = null)
        {
            CheckDisposed();

            var state = GetState(collection);

            // already known?
            if (state.IdentityMap.HasEntity(entity))
            {
                return false;
            }

            var doc = new Document
            {
                Type = Store.TypeNames[entity.GetType()],
                Content = Store.Configuration.ContentSerializer.Serialize(entity)
            };

            // Import version
            if (version != 0)
            {
                doc.Version = version;
            }
            else
            {
                var versionAccessor = _store.GetVersionAccessor(entity.GetType());
                if (versionAccessor != null)
                {
                    doc.Version = versionAccessor.Get(entity);
                }
            }

            if (id != 0)
            {
                state.IdentityMap.AddEntity(id, entity);
                state.Updated.Add(entity);

                doc.Id = id;
                state.IdentityMap.AddDocument(doc);

                return true;
            }
            else
            {
                // Does it have a valid identifier?
                var accessor = _store.GetIdAccessor(entity.GetType());
                if (accessor != null)
                {
                    id = accessor.Get(entity);

                    if (id > 0)
                    {
                        state.IdentityMap.AddEntity(id, entity);
                        state.Updated.Add(entity);

                        doc.Id = id;
                        state.IdentityMap.AddDocument(doc);

                        return true;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Invalid 'Id' value: {id}");
                    }
                }
                else
                {
                    throw new InvalidOperationException("Objects without an 'Id' property can't be imported if no 'id' argument is provided.");
                }
            }
        }

        public void Detach(object entity, string collection)
        {
            CheckDisposed();
            
            var state = GetState(collection);

            state.Saved.Remove(entity);
            state.Updated.Remove(entity);
            state.Tracked.Remove(entity);
            state.Deleted.Remove(entity);

            if (state.IdentityMap.TryGetDocumentId(entity, out var id))
            {
                state.IdentityMap.Remove(id, entity);
            }
        }

        private async Task SaveEntityAsync(object entity, string collection)
        {
            if (entity == null)
            {
                throw new ArgumentNullException("obj");
            }

            if (entity is Document document)
            {
                throw new ArgumentException("A document should not be saved explicitely");
            }

            if (entity is IIndex index)
            {
                throw new ArgumentException("An index should not be saved explicitely");
            }

            var state = GetState(collection);

            var doc = new Document
            {
                Type = Store.TypeNames[entity.GetType()]
            };

            if (!state.IdentityMap.TryGetDocumentId(entity, out var id))
            {
                throw new InvalidOperationException("The object to save was not found in identity map.");
            }

            doc.Id = id;

            await DemandAsync();

            var versionAccessor = _store.GetVersionAccessor(entity.GetType());
            if (versionAccessor != null)
            {
                doc.Version = versionAccessor.Get(entity);
            }

            if (doc.Version == 0)
            {
                doc.Version = 1;
            }

            if (versionAccessor != null)
            {
                versionAccessor.Set(entity, (int) doc.Version);
            }

            doc.Content = Store.Configuration.ContentSerializer.Serialize(entity);

            _commands.Add(new CreateDocumentCommand(doc, Store.Configuration.TableNameConvention, _tablePrefix, collection));

            state.IdentityMap.AddDocument(doc);

            await MapNew(doc, entity, collection);
        }

        private async Task UpdateEntityAsync(object entity, bool tracked, string collection)
        {
            if (entity == null)
            {
                throw new ArgumentNullException("obj");
            }

            var index = entity as IIndex;

            if (entity is Document)
            {
                throw new ArgumentException("A document should not be saved explicitely");
            }

            if (index != null)
            {
                throw new ArgumentException("An index should not be saved explicitely");
            }

            var state = GetState(collection);

            // Reload to get the old map
            if (!state.IdentityMap.TryGetDocumentId(entity, out var id))
            {
                throw new InvalidOperationException("The object to update was not found in identity map.");
            }

            if (!state.IdentityMap.TryGetDocument(id, out var oldDoc))
            {
                oldDoc = await GetDocumentByIdAsync(id, collection);

                if (oldDoc == null)
                {
                    throw new InvalidOperationException("Incorrect attempt to update an object that doesn't exist. Ensure a new object was not saved with an identifier value.");
                }
            }

            string newContent = Store.Configuration.ContentSerializer.Serialize(entity);

            // if the document has already been updated or saved with this session (auto or intentional flush), ensure it has 
            // been changed before doing another query
            if (tracked && String.Equals(newContent, oldDoc.Content))
            {
                return;
            }

            long version = -1;

            if (state.Concurrent.Contains(id))
            {
                version = oldDoc.Version;

                var versionAccessor = _store.GetVersionAccessor(entity.GetType());
                if (versionAccessor != null)
                {
                    var localVersion = versionAccessor.Get(entity);

                    // if the version has been set, use it
                    if (localVersion != 0)
                    {
                        version = localVersion;
                    }
                }

                oldDoc.Version += 1;

                // apply the new version to the object
                if (versionAccessor != null)
                {
                    versionAccessor.Set(entity, (int)oldDoc.Version);

                    newContent = Store.Configuration.ContentSerializer.Serialize(entity);
                }
            }

            var oldObj = Store.Configuration.ContentSerializer.Deserialize(oldDoc.Content, entity.GetType());

            // Update map index
            await MapDeleted(oldDoc, oldObj, collection);

            await MapNew(oldDoc, entity, collection);

            await DemandAsync();

            oldDoc.Content = newContent;

            _commands.Add(new UpdateDocumentCommand(oldDoc, Store, version, collection));
        }

        private async Task<Document> GetDocumentByIdAsync(int id, string collection)
        {
            await DemandAsync();

            var documentTable = Store.Configuration.TableNameConvention.GetDocumentTable(collection);

            var command = "select * from " + _dialect.QuoteForTableName(_tablePrefix + documentTable) + " where " + _dialect.QuoteForColumnName("Id") + " = @Id";
            var key = new WorkerQueryKey(nameof(GetDocumentByIdAsync), new[] { id });

            try
            {
                var result = await _store.ProduceAsync(key, (args) =>
                {
                    var localStore = (Store)args[0];
                    var localConnection = (DbConnection)args[1];
                    var localTransaction = (DbTransaction)args[2];
                    var localCommand = (string)args[3];
                    var localParameters = (object)args[4];

                    localStore.Configuration.Logger.LogTrace(localCommand);
                    return localConnection.QueryAsync<Document>(localCommand, localParameters, localTransaction);
                },
                _store,
                _connection,
                _transaction,
                command,
                new { Id = id });

                return result.FirstOrDefault();
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
                Cancel();

                throw;
            }            
        }

        public void Delete(object obj, string collection = null)
        {
            CheckDisposed();
            
            var state = GetState(collection);

            state.Deleted.Add(obj);
        }

        private async Task DeleteEntityAsync(object obj, string collection)
        {
            if (obj == null)
            {
                throw new ArgumentNullException("obj");
            }
            else if (obj is IIndex)
            {
                throw new ArgumentException("Can't call DeleteEntity on an Index");
            }
            else
            {
                var state = GetState(collection);

                if (!state.IdentityMap.TryGetDocumentId(obj, out var id))
                {
                    var accessor = _store.GetIdAccessor(obj.GetType());
                    if (accessor == null)
                    {
                        throw new InvalidOperationException("Could not delete object as it doesn't have an Id property");
                    }

                    id = accessor.Get(obj);
                }

                var doc = await GetDocumentByIdAsync(id, collection);

                if (doc != null)
                {
                    // Untrack the deleted object
                    state.IdentityMap.Remove(id, obj);

                    // Update impacted indexes
                    await MapDeleted(doc, obj, collection);

                    // The command needs to come after any index deletion because of the database constraints
                    _commands.Add(new DeleteDocumentCommand(doc, Store, collection));
                }
            }
        }

        public async Task<IEnumerable<T>> GetAsync<T>(int[] ids, string collection = null) where T : class
        {
            if (ids == null || !ids.Any())
            {
                return Enumerable.Empty<T>();
            }

            CheckDisposed();

            // Auto-flush
            await FlushAsync();

            await DemandAsync();

            var documentTable = Store.Configuration.TableNameConvention.GetDocumentTable(collection);

            var command = "select * from " + _dialect.QuoteForTableName(_tablePrefix + documentTable) + " where " + _dialect.QuoteForColumnName("Id") + " " + _dialect.InOperator("@Ids");

            var key = new WorkerQueryKey(nameof(GetAsync), ids);
            try
            {
                var documents = await _store.ProduceAsync(key, (args) =>
                {
                    var localConnection = (DbConnection)args[0];
                    var localTransaction = (DbTransaction)args[1];
                    var localCommand = (string)args[2];
                    var localParamters = args[3];

                    return localConnection.QueryAsync<Document>(localCommand, localParamters, localTransaction);
                },
                _connection,
                _transaction,
                command,
                new { Ids = ids });

                return Get<T>(documents.OrderBy(d => Array.IndexOf(ids, d.Id)).ToArray(), collection);
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
                Cancel();

                throw;
            }
        }

        public IEnumerable<T> Get<T>(IList<Document> documents, string collection) where T : class
        {
            if (documents == null || !documents.Any())
            {
                return Enumerable.Empty<T>();
            }

            var result = new List<T>();
            var defaultAccessor = _store.GetIdAccessor(typeof(T));
            var accessor = defaultAccessor;
            var typeName = Store.TypeNames[typeof(T)];

            var state = GetState(collection);

            // Are all the objects already in cache?
            foreach (var d in documents)
            {
                if (state.IdentityMap.TryGetEntityById(d.Id, out var entity))
                {
                    result.Add((T)entity);
                }
                else
                {
                    T item;

                    // If the document type doesn't match the requested one, check it's a base type
                    if (!String.Equals(typeName, d.Type, StringComparison.Ordinal))
                    {
                        var itemType = Store.TypeNames[d.Type];

                        // Ignore the document if it can't be casted to the requested type
                        if (!typeof(T).IsAssignableFrom(itemType))
                        {
                            continue;
                        }

                        accessor = _store.GetIdAccessor(itemType);

                        item = (T)Store.Configuration.ContentSerializer.Deserialize(d.Content, itemType);
                    }
                    else
                    {
                        item = (T)Store.Configuration.ContentSerializer.Deserialize(d.Content, typeof(T));

                        accessor = defaultAccessor;
                    }

                    if (accessor != null)
                    {
                        accessor.Set(item, d.Id);
                    }

                    // track the loaded object
                    state.IdentityMap.AddEntity(d.Id, item);
                    state.IdentityMap.AddDocument(d);

                    result.Add(item);
                }
            };

            return result;
        }

        public IQuery Query(string collection = null)
        {
            return new DefaultQuery(_connection, _transaction, this, _tablePrefix, collection);
        }

        public IQuery<T> ExecuteQuery<T>(ICompiledQuery<T> compiledQuery, string collection = null) where T : class
        {
            if (compiledQuery == null)
            {
                throw new ArgumentNullException(nameof(compiledQuery));
            }

            var compiledQueryType = compiledQuery.GetType();

            if (!_store.CompiledQueries.TryGetValue(compiledQueryType, out var queryState))
            {
                var localQuery = ((IQuery)new DefaultQuery(_connection, _transaction, this, _tablePrefix, collection)).For<T>(false);
                var defaultQuery = (DefaultQuery.Query<T>)compiledQuery.Query().Compile().Invoke(localQuery);
                queryState = defaultQuery._query._queryState;

                // Don't use Add as two thread could concurrently reach this point.
                // We don't mind losing some values as the next call will restore it if it's not cached.
                _store.CompiledQueries = _store.CompiledQueries.SetItem(compiledQueryType, queryState);
            }

            queryState = queryState.Clone();

            IQuery newQuery = new DefaultQuery(_connection, _transaction, this, _tablePrefix, queryState, compiledQuery);
            return newQuery.For<T>(false);
        }

        private void CheckDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(Session));
            }
        }

        ~Session()
        {
            // Ensure the session gets disposed if the user cannot wrap the session in a using block.
            // For instance in OrchardCore the session is disposed from a middleware, so if an exception
            // is thrown in a middleware, it might not get triggered.

            _cancel = true;

            Dispose();
        }

        public void Dispose()
        {
            // Do nothing if Dispose() was already called
            if (_disposed)
            {
                return; 
            }

            try
            {
                if (!_cancel && HasWork())
                {
                    // This should never go there, CommitAsync() should be called explicitely
                    // and asynchronously before Dispose() is invoked
                    FlushAsync().GetAwaiter().GetResult();
                }
            }catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                _disposed = true;

                CommitTransaction();

                ReleaseSession();
            }
        }

        /// <summary>
        /// Clears all the resources associated to the session.
        /// </summary>
        private void ReleaseSession()
        {
            foreach (var state in CollectionStates.Values)
            {
                state.IdentityMap.Clear();
            }

            _descriptors.Clear();
            _indexes?.Clear();

            _store.ReleaseSession(this);
        }

        /// <summary>
        /// Clears all the resources associated to the unit of work.
        /// </summary>
        private void ReleaseTransaction()
        {
            foreach (var state in CollectionStates.Values)
            {
                state.Concurrent.Clear();
                state.Saved.Clear();
                state.Updated.Clear();
                state.Tracked.Clear();
                state.Deleted.Clear();
                state.Maps.Clear();
            }

            _commands.Clear();

            if (_transaction != null)
            {
                _transaction.Dispose();
                _transaction = null;
            }
        }

        private void ReleaseConnection()
        {
            try
            {
                ReleaseTransaction();

                if (_connection != null)
                {
                    _connection.Close();
                    _connection.Dispose();
                    _connection = null;
                }
            }catch(Exception ex) //TODO PCCOM "Fix"
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// Called when the instance is reused from an object pool and doesn't go
        /// through the constructor.
        /// </summary>
        internal void StartLease(IsolationLevel isolationLevel)
        {
            _disposed = false;
            _cancel = false;
            _isolationLevel = isolationLevel;
        }

        public async Task FlushAsync()
        {
            if (!HasWork())
            {
                return;
            }

            // prevent recursive calls in FlushAsync,
            // when autoflush is triggered from an IndexProvider
            // for instance.

            if (_flushing)
            {
                return;
            }

            _flushing = true;

            // we only check if the session is disposed if 
            // there are no commands to commit.

            CheckDisposed();

            try
            {
                // saving all tracked entities
                foreach (var collectionState in CollectionStates)
                {
                    var state = collectionState.Value;
                    var collection = collectionState.Key;

                    foreach (var obj in state.Tracked)
                    {
                        if (!state.Deleted.Contains(obj))
                        {
                            await UpdateEntityAsync(obj, true, collection);
                        }
                    }

                    // saving all updated entities
                    foreach (var obj in state.Updated)
                    {
                        if (!state.Deleted.Contains(obj))
                        {
                            await UpdateEntityAsync(obj, false, collection);
                        }
                    }

                    // saving all pending entities
                    foreach (var obj in state.Saved)
                    {
                        await SaveEntityAsync(obj, collection);
                    }

                    // deleting all pending entities
                    foreach (var obj in state.Deleted)
                    {
                        await DeleteEntityAsync(obj, collection);
                    }
                }

                // compute all reduce indexes
                await ReduceAsync();

                await DemandAsync();

                BatchCommands();

                foreach (var command in _commands.OrderBy(x => x.ExecutionOrder))
                {
                    await command.ExecuteAsync(_connection, _transaction, _dialect, _logger);
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
                Cancel();

                throw;
            }
            finally
            {
                foreach (var state in CollectionStates.Values)
                {
                    // Track all saved and updated entities in case they are modified before
                    // CommitAsync is called
                    foreach (var saved in state.Saved)
                    {
                        state.Tracked.Add(saved);
                    }

                    foreach (var updated in state.Updated)
                    {
                        state.Tracked.Add(updated);
                    }

                    state.Saved.Clear();
                    state.Updated.Clear();
                    state.Deleted.Clear();
                    state.Maps.Clear();
                }

                _commands.Clear();
                _flushing = false;
            }
        }

        private void BatchCommands()
        {
            if (_commands.Count == 0)
            {
                return;
            }

            List<CreateDocumentCommand> createDocumentCommands = null;
            List<DeleteDocumentCommand> deleteDocumentCommands = null;
            Dictionary<Type, List<DeleteMapIndexCommand>> deleteMapIndexCommandsDictionary = null;

            for (var i = _commands.Count - 1; i >= 0; i--)
            {
                var command = _commands[i];

                switch (command)
                {

                    case CreateDocumentCommand createDocumentCommand:
                        createDocumentCommands ??= new List<CreateDocumentCommand>();
                        createDocumentCommands.Add(createDocumentCommand);
                        _commands.RemoveAt(i);
                        break;

                    case DeleteDocumentCommand deleteDocumentCommand:
                        deleteDocumentCommands ??= new List<DeleteDocumentCommand>();
                        deleteDocumentCommands.Add(deleteDocumentCommand);
                        _commands.RemoveAt(i);
                        break;

                    case DeleteMapIndexCommand deleteMapIndexCommand:
                        deleteMapIndexCommandsDictionary ??= new Dictionary<Type, List<DeleteMapIndexCommand>>();
                        if (!deleteMapIndexCommandsDictionary.TryGetValue(deleteMapIndexCommand.IndexType, out var deleteMapIndexCommands))
                        {
                            deleteMapIndexCommands = new List<DeleteMapIndexCommand>();
                            deleteMapIndexCommandsDictionary.Add(deleteMapIndexCommand.IndexType, deleteMapIndexCommands);
                        }

                        deleteMapIndexCommands.Add(deleteMapIndexCommand);
                        _commands.RemoveAt(i);
                        break;
                }
            }

            if (createDocumentCommands != null)
            {
                foreach (var page in createDocumentCommands.PagesOfByCollection(_store.Configuration.CommandsPageSize))
                {
                    _commands.Add(new CreateDocumentCommand(page.Value.SelectMany(x => x.Documents), Store.Configuration.TableNameConvention, _tablePrefix, page.Key));
                }
            }

            if (deleteDocumentCommands != null)
            {
                foreach (var page in deleteDocumentCommands.PagesOfByCollection(_store.Configuration.CommandsPageSize))
                {
                    _commands.Add(new DeleteDocumentCommand(page.Value.SelectMany(x => x.Documents), Store, page.Key));
                }
            }

            if (deleteMapIndexCommandsDictionary != null)
            {
                foreach (var entry in deleteMapIndexCommandsDictionary)
                {
                    foreach (var page in entry.Value.PagesOfByCollection(_store.Configuration.CommandsPageSize))
                    {
                        _commands.Add(new DeleteMapIndexCommand(entry.Key, page.Value.SelectMany(x => x.DocumentIds), Store, page.Key));
                    }
                }
            }
        }

        public async Task CommitAsync()
        {
            try
            {
                if (!_cancel)
                {
                    await FlushAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                CommitTransaction();
            }
        }

        private void CommitTransaction()
        {
            try
            {
                if (!_cancel)
                {
                    if (_transaction != null)
                    {
                        _transaction.Commit();
                    }
                }
                else
                {
                    if (_transaction != null)
                    {
                        _transaction.Rollback();
                    }
                }
            }catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                ReleaseConnection();
            }
        }

        /// <summary>
        /// Whether the current session has data to flush or not.
        /// </summary>
        internal bool HasWork()
        {
            foreach (var state in CollectionStates.Values)
            {
                if (
                    state.Saved.Count +
                    state.Updated.Count +
                    state.Tracked.Count +
                    state.Deleted.Count > 0
                    ) return true;
            }

            return false;
        }

        private async Task ReduceAsync()
        {
            foreach (var collectionState in CollectionStates)
            {
                var state = collectionState.Value;
                var collection = collectionState.Key;

                // loop over each Indexer used by new objects
                foreach (var descriptor in state.Maps.Keys)
                {
                    // if the descriptor has no reduce behavior, ignore it
                    if (descriptor.Reduce == null)
                    {
                        continue;
                    }

                    if (descriptor.GroupKey == null)
                    {
                        throw new InvalidOperationException(
                            "A map/reduce index must declare at least one property with a GroupKey attribute: " +
                            descriptor.Type.FullName);
                    }

                    // a groupping method for the current descriptor
                    var descriptorGroup = GetGroupingMetod(descriptor);

                    // list all available grouping keys in the current set
                    var allKeysForDescriptor =
                        state.Maps[descriptor].Select(x => x.Map).Select(descriptorGroup).Distinct().ToArray();

                    // reduce each group, will result in one Reduced index per group
                    foreach (var currentKey in allKeysForDescriptor)
                    {
                        // group all mapped indexes
                        var newMapsGroup =
                            state.Maps[descriptor].Where(x => x.State == MapStates.New).Select(x => x.Map).Where(
                                x => descriptorGroup(x).Equals(currentKey)).ToArray();

                        var deletedMapsGroup =
                            state.Maps[descriptor].Where(x => x.State == MapStates.Delete).Select(x => x.Map).Where(
                                x => descriptorGroup(x).Equals(currentKey)).ToArray();

                        var updatedMapsGroup =
                            state.Maps[descriptor].Where(x => x.State == MapStates.Update).Select(x => x.Map).Where(
                                x => descriptorGroup(x).Equals(currentKey)).ToArray();

                        // todo: if an updated object got his Key changed, then apply a New to the new value group
                        // and a Delete to the old value group. Otherwise apply Update to the current value group

                        IIndex index = null;

                        if (newMapsGroup.Any())
                        {
                            // reducing an already groupped set (technically the reduction should contain the grouping step, but by design ...)
                            index = descriptor.Reduce(newMapsGroup.GroupBy(descriptorGroup).First());

                            if (index == null)
                            {
                                throw new InvalidOperationException(
                                    "The reduction on a grouped set should have resulted in a unique result"
                                    );
                            }
                        }

                        var dbIndex = await ReduceForAsync(descriptor, currentKey);

                        // if index present in db and new objects, reduce them
                        if (dbIndex != null && index != null)
                        {
                            // reduce over the two objects
                            var reductions = new[] { dbIndex, index };

                            var grouppedReductions = reductions.GroupBy(descriptorGroup).SingleOrDefault();

                            if (grouppedReductions == null)
                            {
                                throw new InvalidOperationException(
                                    "The grouping on the db and in memory set should have resulted in a unique result");
                            }

                            index = descriptor.Reduce(grouppedReductions);

                            if (index == null)
                            {
                                throw new InvalidOperationException(
                                    "The reduction on a grouped set should have resulted in a unique result");
                            }
                        }
                        else if (dbIndex != null)
                        {
                            index = dbIndex;
                        }

                        if (index != null)
                        {
                            // are there any deleted object for this descriptor/group ?
                            if (deletedMapsGroup.Any())
                            {
                                index = descriptor.Delete(index, deletedMapsGroup.GroupBy(descriptorGroup).First());
                                // At this point, index can be null if the reduction returned a null index from Delete handler
                            }

                            // are there any updated object for this descriptor/group ?
                            if (updatedMapsGroup.Any())
                            {
                                index = descriptor.Update(index, updatedMapsGroup.GroupBy(descriptorGroup).First());
                            }
                        }

                        var deletedDocumentIds = deletedMapsGroup.SelectMany(x => x.GetRemovedDocuments().Select(d => d.Id)).ToArray();
                        var addedDocumentIds = newMapsGroup.SelectMany(x => x.GetAddedDocuments().Select(d => d.Id)).ToArray();

                        if (dbIndex != null)
                        {
                            if (index == null)
                            {
                                _commands.Add(new DeleteReduceIndexCommand(dbIndex, Store, collection));
                            }
                            else
                            {
                                index.Id = dbIndex.Id;

                                var common = addedDocumentIds.Intersect(deletedDocumentIds).ToArray();
                                addedDocumentIds = addedDocumentIds.Where(x => !common.Contains(x)).ToArray();
                                deletedDocumentIds = deletedDocumentIds.Where(x => !common.Contains(x)).ToArray();

                                // Update updated, new and deleted linked documents
                                _commands.Add(new UpdateIndexCommand(index, addedDocumentIds, deletedDocumentIds, Store, collection));
                            }
                        }
                        else
                        {
                            if (index != null)
                            {
                                // The index is new
                                _commands.Add(new CreateIndexCommand(index, addedDocumentIds, Store, collection));
                            }
                        }
                    }
                }
            }
        }

        private async Task<ReduceIndex> ReduceForAsync(IndexDescriptor descriptor, object currentKey)
        {
            await DemandAsync();

            var name = _tablePrefix + descriptor.IndexType.Name;
            var sql = "select * from " + _dialect.QuoteForTableName(name) + " where " + _dialect.QuoteForColumnName(descriptor.GroupKey.Name) + " = @currentKey";

            var index = await _connection.QueryAsync(descriptor.IndexType, sql, new { currentKey }, _transaction);
            return index.FirstOrDefault() as ReduceIndex;
        }

        /// <summary>
        /// Creates a Func{IIndex, object}; dynamically, based on GroupKey attributes
        /// this function will be used as the keySelector for Linq.Grouping
        /// </summary>
        private Func<IIndex, object> GetGroupingMetod(IndexDescriptor descriptor)
        {
            if (!_store.GroupMethods.TryGetValue(descriptor.Type, out var result))
            {
                // IIndex i => i
                var instance = Expression.Parameter(typeof(IIndex), "i");
                // i => ((TIndex)i)
                var convertInstance = Expression.Convert(instance, descriptor.GroupKey.DeclaringType);
                // i => ((TIndex)i).{Property}
                var property = Expression.Property(convertInstance, descriptor.GroupKey);
                // i => (object)(((TIndex)i).{Property})
                var convert = Expression.Convert(property, typeof(object));

                result = Expression.Lambda<Func<IIndex, object>>(convert, instance).Compile();

                // Don't use Add as two thread could concurrently reach this point.
                // We don't mind losing some values as the next call will restore it if it's not cached.
                _store.GroupMethods = _store.GroupMethods.SetItem(descriptor.Type, result);
            }

            return result;
        }

        /// <summary>
        /// Resolves all the descriptors registered on the Store and the Session
        /// </summary>
        private IEnumerable<IndexDescriptor> GetDescriptors(Type t, string collection)
        {
            var cacheKey = t.FullName + ":" + collection;

            if (!_descriptors.TryGetValue(cacheKey, out var typedDescriptors))
            {
                typedDescriptors = _store.Describe(t, collection);

                if (_indexes != null)
                {
                    typedDescriptors = typedDescriptors.Union(_store.CreateDescriptors(t, collection, _indexes)).ToArray();
                }

                _descriptors.Add(cacheKey, typedDescriptors);
            }

            return typedDescriptors;
        }

        private async Task MapNew(Document document, object obj, string collection)
        {
            var descriptors = GetDescriptors(obj.GetType(), collection);

            var state = GetState(collection);

            foreach (var descriptor in descriptors)
            {
                var mapped = await descriptor.Map(obj);

                if (mapped != null)
                {
                    foreach (var index in mapped)
                    {
                        if (index == null)
                        {
                            continue;
                        }

                        index.AddDocument(document);

                        // if the mapped elements are not meant to be reduced,
                        // then save them in db, as index
                        if (descriptor.Reduce == null)
                        {
                            if (index.Id == 0)
                            {
                                _commands.Add(new CreateIndexCommand(index, Enumerable.Empty<int>(), Store, collection));
                            }
                            else
                            {
                                _commands.Add(new UpdateIndexCommand(index, Enumerable.Empty<int>(), Enumerable.Empty<int>(), Store, collection));
                            }
                        }
                        else
                        {
                            // save for later reducing
                            if (!state.Maps.TryGetValue(descriptor, out var listmap))
                            {
                                state.Maps.Add(descriptor, listmap = new List<MapState>());
                            }

                            listmap.Add(new MapState(index, MapStates.New));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Update map and reduce indexes when an entity is deleted.
        /// </summary>
        private async Task MapDeleted(Document document, object obj, string collection)
        {
            var descriptors = GetDescriptors(obj.GetType(), collection);

            var state = GetState(collection);

            foreach (var descriptor in descriptors)
            {
                // If the mapped elements are not meant to be reduced, delete
                if (descriptor.Reduce == null || descriptor.Delete == null)
                {
                    _commands.Add(new DeleteMapIndexCommand(descriptor.IndexType, new[] { document.Id }, Store, collection));
                }
                else
                {
                    var mapped = await descriptor.Map(obj);

                    if (mapped != null)
                    {
                        foreach (var index in mapped)
                        {
                            // save for later reducing
                            if (!state.Maps.TryGetValue(descriptor, out var listmap))
                            {
                                state.Maps.Add(descriptor, listmap = new List<MapState>());
                            }

                            listmap.Add(new MapState(index, MapStates.Delete));
                            index.RemoveDocument(document);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Initializes a new transaction if none has been yet
        /// </summary>
        public async Task<DbTransaction> DemandAsync()
        {
            CheckDisposed();

            if (_transaction == null)
            {
                if (_connection == null)
                {
                    _connection = _store.Configuration.ConnectionFactory.CreateConnection() as DbConnection;

                    if (_connection == null)
                    {
                        throw new InvalidOperationException("The connection couldn't be covnerted to DbConnection");
                    }
                }

                if (_connection.State == ConnectionState.Closed)
                {
                    await _connection.OpenAsync();
                }

                // In the case of shared connections (InMemory) this can throw as the transation
                // might already be set by a concurrent thread on the same shared connection.
                _transaction = _connection.BeginTransaction(_isolationLevel);
            }

            return _transaction;
        }

        public void Cancel()
        {
            CheckDisposed();

            _cancel = true;

            ReleaseTransaction();
        }

        public IStore Store => _store;

        #region Storage implementation

        private struct IdString
        {
#pragma warning disable 0649
            public int Id;
            public string Content;
#pragma warning restore 0649
        }
        #endregion
    }
}
