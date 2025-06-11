using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Threading;
using ViennaDotNet.Common.Excceptions;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB.Models.Player;

namespace ViennaDotNet.DB;

public sealed class EarthDB : IDisposable
{
    private const int TRANSACTION_TIMEOUT = 60;

    public static EarthDB Open(string connectionString)
    {
        return new EarthDB(connectionString);
    }

    private string connectionString;
    private HashSet<SqliteTransaction> transactions = [];

    private readonly Lock _lock = new();

    private EarthDB(string _connectionString)
    {
        connectionString = _connectionString;

        try
        {
            using var connection = new SqliteConnection("Data Source=" + connectionString);
            connection.Open();
            using (var command = new SqliteCommand("CREATE TABLE IF NOT EXISTS objects (type STRING NOT NULL, id STRING NOT NULL, value STRING NOT NULL, version INTEGER NOT NULL, PRIMARY KEY (type, id))", connection) { CommandTimeout = TRANSACTION_TIMEOUT })
                command.ExecuteNonQuery();

        }
        catch (SqliteException ex)
        {
            throw new DatabaseException(ex);
        }
    }

    private SqliteTransaction transaction(bool write)
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
                throw new UnsupportedOperationException();

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
        {
            return Then(function, true);
        }

        public Query Then(Query query, bool replaceResults)
        {
            return Then(results => query, replaceResults);
        }

        public Query Then(Query query)
        {
            return Then(query, true);
        }
        #endregion

        public async Task<Results> ExecuteAsync(EarthDB earthDB, CancellationToken cancellationToken = default)
        {
            try
            {
                using SqliteTransaction transaction = earthDB.transaction(_write);
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
                using SqliteTransaction transaction = earthDB.transaction(_write);
                Dictionary<string, int?> updates = [];
                Results results = executeInternal(transaction, _write, updates);
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
                string json = toJson(entry.value);

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
                if (version != null)
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
                    object value = createNewInstance(entry.valueType);
                    string json = toJson(value);

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
                var command = transaction.Connection!.CreateCommand();
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
                        object value = fromJson(json, entry.valueType);
                        results.getValues[entry.type] = new Results.Result(value, version);
                    }
                    else
                    {
                        results.getValues[entry.type] = new Results.Result(createNewInstance(entry.valueType), 1);
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

        private Results executeInternal(SqliteTransaction transaction, bool write, Dictionary<string, int?> updates)
        {
            if (_write && !write)
                throw new UnsupportedOperationException();

            Results results = new Results();

            foreach (WriteObjectsEntry entry in writeObjects)
            {
                string json = toJson(entry.value);

                var command = transaction.Connection!.CreateCommand();
                command.CommandTimeout = TRANSACTION_TIMEOUT;
                command.CommandText = "INSERT OR REPLACE INTO objects(type, id, value, version) VALUES ($type, $id, $value, COALESCE((SELECT version FROM objects WHERE type == $type AND id == $id), 1) + 1)";

                command.Parameters.AddWithValue("$type", entry.type);
                command.Parameters.AddWithValue("$id", entry.id);
                command.Parameters.AddWithValue("$value", json);
                command.ExecuteNonQuery();

                /*****************************/
                command = transaction.Connection.CreateCommand();
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
                if (version != null)
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
                    object value = createNewInstance(entry.valueType);
                    string json = toJson(value);

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
                var command = transaction.Connection!.CreateCommand();
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
                        object value = fromJson(json, entry.valueType);
                        results.getValues[entry.type] = new Results.Result(value, version);
                    }
                    else
                    {
                        results.getValues[entry.type] = new Results.Result(createNewInstance(entry.valueType), 1);
                    }
                }
            }

            foreach (ExtrasEntry entry in extras)
                results.extras[entry.name] = entry.value;

            foreach (var entry in thenFunctions)
            {
                Query query = entry.function(results);
                Results innerResults = query.executeInternal(transaction, write, updates);
                if (entry.replaceResults)
                {
                    results = innerResults;
                }
            }

            return results;
        }
    }

    private static object fromJson(string json, Type valueType)
    {
        return JsonConvert.DeserializeObject(json, valueType, new Tokens.TokenConverter(), new ActivityLog.Entry.EntryConverter());
    }

    private static string toJson(object value)
    {
        return JsonConvert.SerializeObject(value);
    }

    private static object createNewInstance(Type valueType)
    {
        try
        {
            object? value = Activator.CreateInstance(valueType);
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
            ? throw new KeyNotFoundException()
            : value;

        public GenericResult<T> GetGeneric<T>(string name)
            => !getValues.TryGetValue(name, out Result? value) || value is null
                ? throw new KeyNotFoundException()
                : new GenericResult<T>((T)value.Value, value.version);

        public Dictionary<string, int?> getUpdates()
            => new Dictionary<string, int?>(updates);

        public object getExtra(string name)
            => !extras.TryGetValue(name, out object? value) || value is null
            ? throw new KeyNotFoundException()
            : value;

        public record Result(object Value, int version);

        public record GenericResult<T>(T GValue, int version)
            : Result(GValue!, version);
    }

    public class DatabaseException : Exception
    {
        public DatabaseException() { }
        public DatabaseException(string message) : base(message) { }
        public DatabaseException(string message, Exception innerException) : base(message, innerException) { }
        public DatabaseException(Exception innerException) : base("Database operation failed.", innerException) { }
    }
}
