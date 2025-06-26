using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Text.Json;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Excceptions;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB.Models.Player;

namespace ViennaDotNet.DB;

public sealed class EarthDB : IDisposable
{
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const int TRANSACTION_TIMEOUT = 60;

    public static EarthDB Open(string connectionString)
        => new EarthDB(connectionString);

    private readonly string connectionString;
    private readonly HashSet<SqliteTransaction> transactions = [];

#if NET9_0_OR_GREATER
    private readonly Lock _lock = new();
#else
    private readonly object _lock = new();
#endif

    static EarthDB()
    {
        jsonOptions.Converters.Add(new Tokens.TokenConverter());
        jsonOptions.Converters.Add(new ActivityLog.Entry.EntryConverter());
    }

    private EarthDB(string _connectionString)
    {
        connectionString = _connectionString;

        try
        {
            using var connection = new SqliteConnection("Data Source=" + connectionString);
            connection.Open();
            using (var command = new SqliteCommand("CREATE TABLE IF NOT EXISTS objects (type STRING NOT NULL, id STRING NOT NULL, value STRING NOT NULL, version INTEGER NOT NULL, PRIMARY KEY (type, id))", connection) { CommandTimeout = TRANSACTION_TIMEOUT })
            {
                command.ExecuteNonQuery();
            }

            using (var command = new SqliteCommand("""
                CREATE TABLE IF NOT EXISTS tiles (
                    pos INTEGER PRIMARY KEY,
                    objectId TEXT NOT NULL
                );
                """, connection) { CommandTimeout = TRANSACTION_TIMEOUT })
            {
                command.ExecuteNonQuery();
            }

        }
        catch (SqliteException ex)
        {
            throw new DatabaseException(ex);
        }
    }

    private SqliteTransaction CreateTransaction(bool write)
    {
        lock (_lock)
        {
            try
            {
                var connection = new SqliteConnection("Data Source=" + connectionString);
                connection.Open();
                var transaction = connection.BeginTransaction(/*!write*/false);// new Transaction(this, connection, write);
                transactions.Add(transaction);
                return transaction;
            }
            catch (SqliteException ex)
            {
                throw new DatabaseException(ex);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var transaction in transactions)
            {
                try
                {
                    transaction.Dispose();
                }
                catch { }
            }
        }
    }

    public class Query
    {
        private readonly bool _write;
        private readonly List<WriteObjectsEntry> writeObjects = [];
        private readonly LinkedList<BumpEntry> bumps = [];
        private readonly List<ReadObjectsEntry> readObjects = [];
        private readonly List<ExtrasEntry> extras = [];
        private readonly List<ThenFunctionEntry> thenFunctions = [];

        private sealed record WriteObjectsEntry(string type, string id, object value);

        private sealed record BumpEntry(string type, string id, Type valueType);

        private sealed record ReadObjectsEntry(string type, string id, Type valueType);

        private sealed record ExtrasEntry(string name, object value);

        private sealed record ThenFunctionEntry(Func<Results, Query> function, bool replaceResults);

        public Query(bool write)
        {
            _write = write;
        }

        #region methods
        public Query Update(string type, string id, object value)
        {
            if (!_write)
            {
                throw new UnsupportedOperationException();
            }

            writeObjects.Add(new WriteObjectsEntry(type, id, value));
            return this;
        }

        public Query bump(string type, string id, Type valueType)
        {
            if (!_write)
            {
                throw new UnsupportedOperationException();
            }

            bumps.AddLast(new BumpEntry(type, id, valueType));
            return this;
        }

        public Query Get(string type, string id, Type valueType)
        {
            readObjects.Add(new ReadObjectsEntry(type, id, valueType));
            return this;
        }

        public Query Extra(string name, object value)
        {
            extras.Add(new ExtrasEntry(name, value));
            return this;
        }

        public Query Then(Func<Results, Query> function, bool replaceResults)
        {
            thenFunctions.Add(new ThenFunctionEntry(function, replaceResults));
            return this;
        }

        public Query Then(Func<Results, Query> function)
            => Then(function, true);

        public Query Then(Query query, bool replaceResults)
            => Then(results => query, replaceResults);

        public Query Then(Query query)
            => Then(query, true);
        #endregion

        public async Task<Results> ExecuteAsync(EarthDB earthDB, CancellationToken cancellationToken = default)
        {
            try
            {
                using SqliteTransaction transaction = earthDB.CreateTransaction(_write);
                Dictionary<string, int?> updates = [];
                Results results = await executeInternalAsync(transaction, _write, updates, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                if (transaction.Connection is not null)
                {
                    await transaction.Connection.CloseAsync();
                }

                results.updates.AddRange(updates);
                return results;
            }
            catch (SqliteException ex)
            {
                throw new DatabaseException(ex);
            }
        }

        public Results Execute(EarthDB earthDB)
        {
            try
            {
                using SqliteTransaction transaction = earthDB.CreateTransaction(_write);
                Dictionary<string, int?> updates = [];
                Results results = ExecuteInternal(transaction, _write, updates);
                transaction.Commit();
                transaction.Connection?.Close();

                results.updates.AddRange(updates);
                return results;
            }
            catch (SqliteException ex)
            {
                throw new DatabaseException(ex);
            }
        }

        private async Task<Results> executeInternalAsync(SqliteTransaction transaction, bool write, Dictionary<string, int?> updates, CancellationToken cancellationToken)
        {
            if (_write && !write)
            {
                throw new UnsupportedOperationException();
            }

            Results results = new Results();

            foreach (WriteObjectsEntry entry in writeObjects)
            {
                string json = ToJson(entry.value);

                using (var command = transaction.Connection!.CreateCommand())
                {
                    command.CommandTimeout = TRANSACTION_TIMEOUT;
                    command.CommandText = "INSERT OR REPLACE INTO objects(type, id, value, version) VALUES ($type, $id, $value, COALESCE((SELECT version FROM objects WHERE type == $type AND id == $id), 1) + 1)";

                    command.Parameters.AddWithValue("$type", entry.type);
                    command.Parameters.AddWithValue("$id", entry.id);
                    command.Parameters.AddWithValue("$value", json);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                using (var command = transaction.Connection.CreateCommand())
                {
                    command.CommandTimeout = TRANSACTION_TIMEOUT;
                    command.CommandText = "SELECT version FROM objects WHERE type == $type AND id == $id";

                    command.Parameters.AddWithValue("$type", entry.type);
                    command.Parameters.AddWithValue("$id", entry.id);
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        if (await reader.ReadAsync(cancellationToken))
                        {
                            int version = reader.GetInt32(0);
                            updates[entry.type] = version;
                        }
                        else
                        {
                            throw new DatabaseException("Could not query updated object");
                        }
                    }
                }
            }

            foreach (BumpEntry entry in bumps)
            {
                int? version;
                using (var command = transaction.Connection!.CreateCommand())
                {
                    command.CommandTimeout = TRANSACTION_TIMEOUT;
                    command.CommandText = "SELECT version FROM objects WHERE type == $type AND id == $id";

                    command.Parameters.AddWithValue("$type", entry.type);
                    command.Parameters.AddWithValue("$id", entry.id);
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        if (await reader.ReadAsync(cancellationToken))
                        {
                            version = reader.GetInt32(0);
                        }
                        else
                        {
                            version = null;
                        }
                    }
                }

                int resultVersion;
                if (version is not null)
                {
                    using (var command = transaction.Connection!.CreateCommand())
                    {
                        command.CommandTimeout = TRANSACTION_TIMEOUT;
                        command.CommandText = "UPDATE objects SET version = $version WHERE type == $type AND id == $id";

                        command.Parameters.AddWithValue("$version", version + 1);
                        command.Parameters.AddWithValue("$type", entry.type);
                        command.Parameters.AddWithValue("$id", entry.id);
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }

                    resultVersion = version.Value + 1;
                }
                else
                {
                    object value = CreateNewInstance(entry.valueType);
                    string json = ToJson(value);

                    using (var command = transaction.Connection!.CreateCommand())
                    {
                        command.CommandTimeout = TRANSACTION_TIMEOUT;
                        command.CommandText = "INSERT INTO objects(type, id, value, version) VALUES ($type, $id, $json, 2)";

                        command.Parameters.AddWithValue("$type", entry.type);
                        command.Parameters.AddWithValue("$id", entry.id);
                        command.Parameters.AddWithValue("$json", json);
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }

                    resultVersion = 2;
                }

                updates[entry.type] = resultVersion;
            }

            foreach (ReadObjectsEntry entry in readObjects)
            {
                using (var command = transaction.Connection!.CreateCommand())
                {
                    command.CommandTimeout = TRANSACTION_TIMEOUT;
                    command.CommandText = "SELECT value, version FROM objects WHERE type == $type AND id == $id";

                    command.Parameters.AddWithValue("$type", entry.type);
                    command.Parameters.AddWithValue("$id", entry.id);
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        if (await reader.ReadAsync(cancellationToken))
                        {
                            string json = reader.GetString(0);
                            int version = reader.GetInt32(1);
                            object? value = FromJson(json, entry.valueType);
                            Debug.Assert(value is not null);
                            results.getValues[entry.type] = new Results.Result(value, version);
                        }
                        else
                        {
                            results.getValues[entry.type] = new Results.Result(CreateNewInstance(entry.valueType), 1);
                        }
                    }
                }
            }

            foreach (ExtrasEntry entry in extras)
                results.extras[entry.name] = entry.value;

            foreach (var entry in thenFunctions)
            {
                Query query = entry.function(results);
                Results innerResults = await query.executeInternalAsync(transaction, write, updates, cancellationToken);
                if (entry.replaceResults)
                {
                    results = innerResults;
                }
            }

            return results;
        }

        private Results ExecuteInternal(SqliteTransaction transaction, bool write, Dictionary<string, int?> updates)
        {
            if (_write && !write)
                throw new UnsupportedOperationException();

            Results results = new Results();

            foreach (WriteObjectsEntry entry in writeObjects)
            {
                string json = ToJson(entry.value);

                using (var command = transaction.Connection!.CreateCommand())
                {
                    command.CommandTimeout = TRANSACTION_TIMEOUT;
                    command.CommandText = "INSERT OR REPLACE INTO objects(type, id, value, version) VALUES ($type, $id, $value, COALESCE((SELECT version FROM objects WHERE type == $type AND id == $id), 1) + 1)";

                    command.Parameters.AddWithValue("$type", entry.type);
                    command.Parameters.AddWithValue("$id", entry.id);
                    command.Parameters.AddWithValue("$value", json);
                    command.ExecuteNonQuery();
                }

                using (var command = transaction.Connection.CreateCommand())
                {
                    command.CommandTimeout = TRANSACTION_TIMEOUT;
                    command.CommandText = "SELECT version FROM objects WHERE type == $type AND id == $id";

                    command.Parameters.AddWithValue("$type", entry.type);
                    command.Parameters.AddWithValue("$id", entry.id);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            int version = reader.GetInt32(0);
                            updates[entry.type] = version;
                        }
                        else
                            throw new DatabaseException("Could not query updated object");
                    }
                }
            }

            foreach (BumpEntry entry in bumps)
            {
                int? version;
                using (var command = transaction.Connection!.CreateCommand())
                {
                    command.CommandTimeout = TRANSACTION_TIMEOUT;
                    command.CommandText = "SELECT version FROM objects WHERE type == $type AND id == $id";

                    command.Parameters.AddWithValue("$type", entry.type);
                    command.Parameters.AddWithValue("$id", entry.id);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            version = reader.GetInt32(0);
                        }
                        else
                        {
                            version = null;
                        }
                    }
                }

                int resultVersion;
                if (version is not null)
                {
                    using (var command = transaction.Connection!.CreateCommand())
                    {
                        command.CommandTimeout = TRANSACTION_TIMEOUT;
                        command.CommandText = "UPDATE objects SET version = $version WHERE type == $type AND id == $id";

                        command.Parameters.AddWithValue("$version", version + 1);
                        command.Parameters.AddWithValue("$type", entry.type);
                        command.Parameters.AddWithValue("$id", entry.id);
                        command.ExecuteNonQuery();
                    }

                    resultVersion = version.Value + 1;
                }
                else
                {
                    object value = CreateNewInstance(entry.valueType);
                    string json = ToJson(value);

                    using (var command = transaction.Connection!.CreateCommand())
                    {
                        command.CommandTimeout = TRANSACTION_TIMEOUT;
                        command.CommandText = "INSERT INTO objects(type, id, value, version) VALUES ($type, $id, $json, 2)";

                        command.Parameters.AddWithValue("$type", entry.type);
                        command.Parameters.AddWithValue("$id", entry.id);
                        command.Parameters.AddWithValue("$json", json);
                        command.ExecuteNonQuery();
                    }

                    resultVersion = 2;
                }

                updates[entry.type] = resultVersion;
            }

            foreach (ReadObjectsEntry entry in readObjects)
            {
                using (var command = transaction.Connection!.CreateCommand())
                {
                    command.CommandTimeout = TRANSACTION_TIMEOUT;
                    command.CommandText = "SELECT value, version FROM objects WHERE type == $type AND id == $id";

                    command.Parameters.AddWithValue("$type", entry.type);
                    command.Parameters.AddWithValue("$id", entry.id);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string json = reader.GetString(0);
                            int version = reader.GetInt32(1);
                            object? value = FromJson(json, entry.valueType);
                            Debug.Assert(value is not null);
                            results.getValues[entry.type] = new Results.Result(value, version);
                        }
                        else
                        {
                            results.getValues[entry.type] = new Results.Result(CreateNewInstance(entry.valueType), 1);
                        }
                    }
                }
            }

            foreach (ExtrasEntry entry in extras)
                results.extras[entry.name] = entry.value;

            foreach (var entry in thenFunctions)
            {
                Query query = entry.function(results);
                Results innerResults = query.ExecuteInternal(transaction, write, updates);
                if (entry.replaceResults)
                {
                    results = innerResults;
                }
            }

            return results;
        }
    }

    public class TileQuery
    {
        private readonly bool _write;
        private readonly List<WriteObjectsEntry> writeObjects = [];
        private readonly List<ReadObjectsEntry> readObjects = [];
        private readonly List<ExtrasEntry> extras = [];
        private readonly List<ThenFunctionEntry> thenFunctions = [];

        private sealed record WriteObjectsEntry(ulong Pos, string ObjectId);

        private sealed record ReadObjectsEntry(ulong Pos);

        private sealed record ExtrasEntry(string name, object value);

        private sealed record ThenFunctionEntry(Func<TileResults, TileQuery> function, bool replaceResults);

        public TileQuery(bool write)
        {
            _write = write;
        }

        #region methods
        public TileQuery Update(ulong pos, string objectId)
        {
            if (!_write)
            {
                throw new UnsupportedOperationException();
            }

            writeObjects.Add(new WriteObjectsEntry(pos, objectId));
            return this;
        }

        public TileQuery Get(ulong pos)
        {
            readObjects.Add(new ReadObjectsEntry(pos));
            return this;
        }

        public TileQuery Extra(string name, object value)
        {
            extras.Add(new ExtrasEntry(name, value));
            return this;
        }

        public TileQuery Then(Func<TileResults, TileQuery> function, bool replaceResults)
        {
            thenFunctions.Add(new ThenFunctionEntry(function, replaceResults));
            return this;
        }

        public TileQuery Then(Func<TileResults, TileQuery> function)
            => Then(function, true);

        public TileQuery Then(TileQuery query, bool replaceResults)
            => Then(results => query, replaceResults);

        public TileQuery Then(TileQuery query)
            => Then(query, true);
        #endregion

        public async Task<TileResults> ExecuteAsync(EarthDB earthDB, CancellationToken cancellationToken = default)
        {
            try
            {
                using SqliteTransaction transaction = earthDB.CreateTransaction(_write);
                TileResults results = await ExecuteInternalAsync(transaction, _write, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                if (transaction.Connection is not null)
                {
                    await transaction.Connection.CloseAsync();
                }

                return results;
            }
            catch (SqliteException ex)
            {
                throw new DatabaseException(ex);
            }
        }

        public TileResults Execute(EarthDB earthDB)
        {
            try
            {
                using SqliteTransaction transaction = earthDB.CreateTransaction(_write);
                TileResults results = ExecuteInternal(transaction, _write);
                transaction.Commit();
                transaction.Connection?.Close();

                return results;
            }
            catch (SqliteException ex)
            {
                throw new DatabaseException(ex);
            }
        }

        private async Task<TileResults> ExecuteInternalAsync(SqliteTransaction transaction, bool write, CancellationToken cancellationToken)
        {
            if (_write && !write)
            {
                throw new UnsupportedOperationException();
            }

            TileResults results = new TileResults();

            foreach (WriteObjectsEntry entry in writeObjects)
            {
                using (var command = transaction.Connection!.CreateCommand())
                {
                    command.CommandTimeout = TRANSACTION_TIMEOUT;
                    command.CommandText = "INSERT OR REPLACE INTO tiles(pos, objectId) VALUES ($pos, $objectId)";

                    command.Parameters.AddWithValue("$pos", entry.Pos);
                    command.Parameters.AddWithValue("$objectId", entry.ObjectId);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            foreach (ReadObjectsEntry entry in readObjects)
            {
                using (var command = transaction.Connection!.CreateCommand())
                {
                    command.CommandTimeout = TRANSACTION_TIMEOUT;
                    command.CommandText = "SELECT objectId FROM tiles WHERE pos == $pos";

                    command.Parameters.AddWithValue("$pos", entry.Pos);
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        if (await reader.ReadAsync(cancellationToken))
                        {
                            string objectId = reader.GetString(0);
                            results.getValues[entry.Pos] = objectId;
                        }
                        else
                        {
                            results.getValues[entry.Pos] = null;
                        }
                    }
                }
            }

            foreach (ExtrasEntry entry in extras)
            {
                results.extras[entry.name] = entry.value;
            }

            foreach (var entry in thenFunctions)
            {
                TileQuery query = entry.function(results);
                TileResults innerResults = await query.ExecuteInternalAsync(transaction, write, cancellationToken);
                if (entry.replaceResults)
                {
                    results = innerResults;
                }
            }

            return results;
        }

        private TileResults ExecuteInternal(SqliteTransaction transaction, bool write)
        {
            if (_write && !write)
            {
                throw new UnsupportedOperationException();
            }

            TileResults results = new TileResults();

            foreach (WriteObjectsEntry entry in writeObjects)
            {
                using (var command = transaction.Connection!.CreateCommand())
                {
                    command.CommandTimeout = TRANSACTION_TIMEOUT;
                    command.CommandText = "INSERT OR REPLACE INTO tiles(pos, objectId) VALUES ($pos, $objectId)";

                    command.Parameters.AddWithValue("$pos", entry.Pos);
                    command.Parameters.AddWithValue("$objectId", entry.ObjectId);
                    command.ExecuteNonQuery();
                }
            }

            foreach (ReadObjectsEntry entry in readObjects)
            {
                using (var command = transaction.Connection!.CreateCommand())
                {
                    command.CommandTimeout = TRANSACTION_TIMEOUT;
                    command.CommandText = "SELECT objectId FROM tiles WHERE pos == $pos";

                    command.Parameters.AddWithValue("$pos", entry.Pos);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string objectId = reader.GetString(0);
                            results.getValues[entry.Pos] = objectId;
                        }
                        else
                        {
                            results.getValues[entry.Pos] = null;
                        }
                    }
                }
            }

            foreach (ExtrasEntry entry in extras)
            {
                results.extras[entry.name] = entry.value;
            }

            foreach (var entry in thenFunctions)
            {
                TileQuery query = entry.function(results);
                TileResults innerResults = query.ExecuteInternal(transaction, write);
                if (entry.replaceResults)
                {
                    results = innerResults;
                }
            }

            return results;
        }
    }

    private static object? FromJson(string json, Type valueType)
        => Json.Deserialize(json, valueType, jsonOptions);

    private static string ToJson(object value)
        => Json.Serialize(value);

    private static object CreateNewInstance(Type valueType)
    {
        try
        {
            object? value = Activator.CreateInstance(valueType);
            Debug.Assert(value is not null);
            return value;
        }
        catch (/*ReflectiveOperationException*/Exception exception)

        {
            throw new DatabaseException(exception);
        }
    }

    public class Results
    {
        public Dictionary<string, Result> getValues = [];
        public Dictionary<string, object> extras = [];
        public Dictionary<string, int?> updates = [];

        public Results()
        {
            // empty
        }

        public Result Get(string name)
            => !getValues.TryGetValue(name, out Result? value) || value is null
            ? throw new KeyNotFoundException($"Key '{name}' was not found.")
            : value;

        public GenericResult<T> GetGeneric<T>(string name)
            => !getValues.TryGetValue(name, out Result? value) || value is null
                ? throw new KeyNotFoundException()
                : new GenericResult<T>((T)value.Value, value.version);

        public Dictionary<string, int?> GetUpdates()
            => new Dictionary<string, int?>(updates);

        public object GetExtra(string name)
            => !extras.TryGetValue(name, out object? value) || value is null
            ? throw new KeyNotFoundException()
            : value;

        public record Result(object Value, int version);

        public record GenericResult<T>(T GValue, int version)
            : Result(GValue!, version);
    }

    public class TileResults
    {
        public Dictionary<ulong, string?> getValues = [];
        public Dictionary<string, object> extras = [];

        public TileResults()
        {
            // empty
        }

        public string? Get(ulong pos)
            => !getValues.TryGetValue(pos, out string? objectId)
            ? throw new KeyNotFoundException($"Pos '{pos}' was not found.")
            : objectId;

        public object GetExtra(string name)
            => !extras.TryGetValue(name, out object? value) || value is null
            ? throw new KeyNotFoundException()
            : value;
    }

    public sealed class DatabaseException : Exception
    {
        public DatabaseException()
        { 
        }

        public DatabaseException(string message) 
            : base(message) 
        { 
        }

        public DatabaseException(string message, Exception innerException) 
            : base(message, innerException) 
        { 
        }

        public DatabaseException(Exception innerException) 
            : base("Database operation failed.", innerException)
        { 
        }
    }
}
