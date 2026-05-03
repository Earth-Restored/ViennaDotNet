using Microsoft.Data.Sqlite;
using Serilog;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Solace.Common;
using Solace.Common.Excceptions;
using Solace.Common.Utils;
using System.Globalization;

[assembly: InternalsVisibleTo("Launcher")]
[assembly: InternalsVisibleTo("Solace.LauncherUI")]
[assembly: InternalsVisibleTo("Solace.BuildplateImporter")]

namespace Solace.DB;

public sealed class EarthDB : IDisposable
{
    internal const string ObjectsTable = "objects";
    internal const string TilesTable = "tiles";
    internal const string BuildplatesTable = "buildplates";

    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal const int TRANSACTION_TIMEOUT = 60;

    public static EarthDB Open(string connectionString)
        => new EarthDB(connectionString);

    private readonly string connectionString;
    // TODO: remove when executed, why a hashset?
    private readonly HashSet<SqliteTransaction> transactions = [];

    private EarthDB(string _connectionString)
    {
        connectionString = _connectionString;

        try
        {
            using var connection = new SqliteConnection("Data Source=" + connectionString);
            connection.Open();
            using (var command = new SqliteCommand($"CREATE TABLE IF NOT EXISTS {ObjectsTable} (type STRING NOT NULL, id STRING NOT NULL, value STRING NOT NULL, version INTEGER NOT NULL, PRIMARY KEY (type, id))", connection) { CommandTimeout = TRANSACTION_TIMEOUT })
            {
                command.ExecuteNonQuery();
            }

            using (var command = new SqliteCommand($"""
                CREATE TABLE IF NOT EXISTS {TilesTable} (
                    id INTEGER PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """, connection) { CommandTimeout = TRANSACTION_TIMEOUT })
            {
                command.ExecuteNonQuery();
            }

            using (var command = new SqliteCommand($"""
                CREATE TABLE IF NOT EXISTS {BuildplatesTable} (
                    id TEXT PRIMARY KEY,
                    value TEXT NOT NULL
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

    internal SqliteConnection OpenConnection(bool write)
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = connectionString,
            Mode = write ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly
        };
        var connection = new SqliteConnection(csb.ConnectionString);
        connection.Open();
        return connection;
    }

    internal SqliteTransaction CreateTransaction(bool write)
    {
        try
        {
            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = connectionString,
                Mode = write ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadOnly
            };
            var connection = new SqliteConnection(csb.ConnectionString);
            connection.Open();
            var transaction = connection.BeginTransaction(/*!write*//*false*/);
            transactions.Add(transaction);
            return transaction;
        }
        catch (SqliteException ex)
        {
            throw new DatabaseException(ex);
        }
    }

    internal void ExecuteCommand(bool write, Action<SqliteCommand> action)
    {
        using SqliteTransaction transaction = CreateTransaction(write);

        using (var command = transaction.Connection!.CreateCommand())
        {
            action(command);
        }

        transaction.Commit();
        transaction.Connection?.Close();
    }

    internal async Task ExecuteCommandAsync(bool write, Action<SqliteCommand> action, CancellationToken cancellationToken = default)
    {
        using SqliteTransaction transaction = CreateTransaction(write);

        try
        {
            using (var command = transaction.Connection!.CreateCommand())
            {
                action(command);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            if (transaction.Connection is not null)
            {
                await transaction.Connection.CloseAsync();
            }
        }
    }

    public void Dispose()
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

    public sealed class Query
    {
        public static Query Empty => new Query(false);

        private readonly bool _write;
        private readonly List<WriteObjectsEntry> writeObjects = [];
        private readonly LinkedList<BumpEntry> bumps = [];
        private readonly List<ReadObjectsEntry> readObjects = [];
        private readonly List<ExtrasEntry> extras = [];
        private readonly List<ThenFunctionEntry> thenFunctions = [];

        private sealed record WriteObjectsEntry(string Type, string Id, object Value);

        private sealed record BumpEntry(string Type, string Id, Type ValueType);

        private sealed record ReadObjectsEntry(string Type, string Id, Type ValueType);

        private sealed record ExtrasEntry(string Name, object Value);

        private sealed record ThenFunctionEntry(Func<Results, Task<Query?>> Function, bool ReplaceResults);

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

        public Query Bump(string type, string id, Type valueType)
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

        public Query Then(Func<Results, Task<Query?>> function)
            => Then(function, true);

        public Query Then(Func<Results, Task<Query?>> function, bool replaceResults)
        {
            thenFunctions.Add(new ThenFunctionEntry(function, replaceResults));
            return this;
        }

        public Query Then(Func<Results, Query?> function)
            => Then(function, true);

        public Query Then(Func<Results, Query?> function, bool replaceResults)
        {
            thenFunctions.Add(new ThenFunctionEntry(results => Task.FromResult(function(results)), replaceResults));
            return this;
        }

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
                Results results = await ExecuteInternalAsync(transaction, _write, updates, cancellationToken);
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

        private async Task<Results> ExecuteInternalAsync(SqliteTransaction transaction, bool write, Dictionary<string, int?> updates, CancellationToken cancellationToken)
        {
            if (_write && !write)
            {
                throw new UnsupportedOperationException();
            }

            var results = new Results();

            foreach (WriteObjectsEntry entry in writeObjects)
            {
                string json = ToJson(entry.Value);

                using (var command = transaction.Connection!.CreateCommand())
                {
                    command.CommandTimeout = TRANSACTION_TIMEOUT;
                    command.CommandText = $"INSERT OR REPLACE INTO {ObjectsTable}(type, id, value, version) VALUES ($type, $id, $value, COALESCE((SELECT version FROM {ObjectsTable} WHERE type == $type AND id == $id), 1) + 1)";

                    command.Parameters.AddWithValue("$type", entry.Type);
                    command.Parameters.AddWithValue("$id", entry.Id);
                    command.Parameters.AddWithValue("$value", json);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                using (var command = transaction.Connection.CreateCommand())
                {
                    command.CommandTimeout = TRANSACTION_TIMEOUT;
                    command.CommandText = $"SELECT version FROM {ObjectsTable} WHERE type == $type AND id == $id";

                    command.Parameters.AddWithValue("$type", entry.Type);
                    command.Parameters.AddWithValue("$id", entry.Id);
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        if (await reader.ReadAsync(cancellationToken))
                        {
                            int version = reader.GetInt32(0);
                            updates[entry.Type] = version;
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
                    command.CommandText = $"SELECT version FROM {ObjectsTable} WHERE type == $type AND id == $id";

                    command.Parameters.AddWithValue("$type", entry.Type);
                    command.Parameters.AddWithValue("$id", entry.Id);
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
                        command.CommandText = $"UPDATE {ObjectsTable} SET version = $version WHERE type == $type AND id == $id";

                        command.Parameters.AddWithValue("$version", version + 1);
                        command.Parameters.AddWithValue("$type", entry.Type);
                        command.Parameters.AddWithValue("$id", entry.Id);
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }

                    resultVersion = version.Value + 1;
                }
                else
                {
                    object value = CreateNewInstance(entry.ValueType);
                    string json = ToJson(value);

                    using (var command = transaction.Connection!.CreateCommand())
                    {
                        command.CommandTimeout = TRANSACTION_TIMEOUT;
                        command.CommandText = $"INSERT INTO {ObjectsTable}(type, id, value, version) VALUES ($type, $id, $json, 2)";

                        command.Parameters.AddWithValue("$type", entry.Type);
                        command.Parameters.AddWithValue("$id", entry.Id);
                        command.Parameters.AddWithValue("$json", json);
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }

                    resultVersion = 2;
                }

                updates[entry.Type] = resultVersion;
            }

            foreach (ReadObjectsEntry entry in readObjects)
            {
                using (var command = transaction.Connection!.CreateCommand())
                {
                    command.CommandTimeout = TRANSACTION_TIMEOUT;
                    command.CommandText = $"SELECT value, version FROM {ObjectsTable} WHERE type == $type AND id == $id";

                    command.Parameters.AddWithValue("$type", entry.Type);
                    command.Parameters.AddWithValue("$id", entry.Id);
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        if (await reader.ReadAsync(cancellationToken))
                        {
                            string json = reader.GetString(0);
                            int version = reader.GetInt32(1);
                            object? value = FromJson(json, entry.ValueType);
                            Debug.Assert(value is not null);
                            results.getValues[entry.Type] = new Results.Result(value, version);
                        }
                        else
                        {
                            results.getValues[entry.Type] = new Results.Result(CreateNewInstance(entry.ValueType), 1);
                        }
                    }
                }
            }

            foreach (ExtrasEntry entry in extras)
            {
                results.extras[entry.Name] = entry.Value;
            }

            foreach (var entry in thenFunctions)
            {
                Query? query = await entry.Function(results);
                if (query is null)
                {
                    continue;
                }

                Results innerResults = await query.ExecuteInternalAsync(transaction, write, updates, cancellationToken);
                if (entry.ReplaceResults)
                {
                    results = innerResults;
                }
            }

            return results;
        }
    }

    public sealed class ObjectQuery
    {
        private readonly bool _write;
        private readonly List<WriteObjectsEntry> writeObjects = [];
        private readonly List<ReadObjectsEntry> readObjects = [];
        private readonly List<SearchEntry> searchEntries = [];
        private readonly List<ExtrasEntry> extras = [];
        private readonly List<ThenFunctionEntry> thenFunctions = [];

        private sealed record WriteObjectsEntry(string Table, object Id, string? Value);

        private sealed record ReadObjectsEntry(string Table, object Id);

        private sealed record SearchEntry(string Table, SearchArguments Arguments);

        private sealed record ExtrasEntry(string Name, object Value);

        private sealed record ThenFunctionEntry(Func<ObjectResults, ObjectQuery> Function, bool ReplaceResults);

        public readonly record struct SearchArguments(bool IncludeValues, bool GetTotalCount, int? Skip = null, int? Take = null, Dictionary<string, MatchValue>? MatchJson = null);

        public readonly record struct MatchValue(string Value, MatchType Type);

        public enum MatchType
        {
            Exact,
            Like, // sql LIKE
        }

        public ObjectQuery(bool write)
        {
            _write = write;
        }

        #region methods
        // null to remove
        public ObjectQuery UpdateTile(ulong pos, string? objectId)
        {
            if (!_write)
            {
                throw new UnsupportedOperationException();
            }

            writeObjects.Add(new WriteObjectsEntry(TilesTable, pos, objectId));
            return this;
        }

        // null to remove
        public ObjectQuery UpdateBuildplate(string id, Models.Global.TemplateBuildplate? buildplate)
        {
            if (!_write)
            {
                throw new UnsupportedOperationException();
            }

            writeObjects.Add(new WriteObjectsEntry(BuildplatesTable, id, buildplate is null ? null : ToJson(buildplate)));
            return this;
        }

        public ObjectQuery GetTile(ulong pos)
        {
            readObjects.Add(new ReadObjectsEntry(TilesTable, pos));
            return this;
        }

        public ObjectQuery GetTiles(IEnumerable<ulong> positions)
        {
            foreach (ulong pos in positions)
            {
                GetTile(pos);
            }

            return this;
        }

        public ObjectQuery GetBuildplate(string id)
        {
            readObjects.Add(new ReadObjectsEntry(BuildplatesTable, id));
            return this;
        }

        public ObjectQuery GetBuildplates(IEnumerable<string> ids)
        {
            foreach (string name in ids)
            {
                GetBuildplate(name);
            }

            return this;
        }

        public ObjectQuery SearchBuildplates(out SearchArguments arguments, bool includeValues, bool getTotalCount, int? skip = null, int? take = null, string? name = null)
        {
            Dictionary<string, MatchValue>? matchJson = null;
            if (!string.IsNullOrWhiteSpace(name))
            {
                matchJson ??= [];
                matchJson.Add("$.name", new MatchValue($"%{name}%", MatchType.Like));
            }

            arguments = new SearchArguments(includeValues, getTotalCount, skip, take, matchJson);

            return SearchBuildplatesInternal(arguments);
        }

        public ObjectQuery SearchBuildplatesInternal(SearchArguments arguments)
        {
            searchEntries.Add(new SearchEntry(BuildplatesTable, arguments));
            return this;
        }

        public ObjectQuery Extra(string name, object value)
        {
            extras.Add(new ExtrasEntry(name, value));
            return this;
        }

        public ObjectQuery Then(Func<ObjectResults, ObjectQuery> function, bool replaceResults)
        {
            thenFunctions.Add(new ThenFunctionEntry(function, replaceResults));
            return this;
        }

        public ObjectQuery Then(Func<ObjectResults, ObjectQuery> function)
            => Then(function, true);

        public ObjectQuery Then(ObjectQuery query, bool replaceResults)
            => Then(results => query, replaceResults);

        public ObjectQuery Then(ObjectQuery query)
            => Then(query, true);
        #endregion

        public async Task<ObjectResults> ExecuteAsync(EarthDB earthDB, CancellationToken cancellationToken = default)
        {
            try
            {
                using SqliteTransaction transaction = earthDB.CreateTransaction(_write);
                ObjectResults results = await ExecuteInternalAsync(transaction, _write, cancellationToken);
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

        private async Task<ObjectResults> ExecuteInternalAsync(SqliteTransaction transaction, bool write, CancellationToken cancellationToken)
        {
            if (_write && !write)
            {
                throw new UnsupportedOperationException();
            }

            var results = new ObjectResults();

            foreach (WriteObjectsEntry entry in writeObjects)
            {
                using (var command = transaction.Connection!.CreateCommand())
                {
                    command.CommandTimeout = TRANSACTION_TIMEOUT;

                    if (entry.Value is null)
                    {
                        command.CommandText = $"DELETE FROM {entry.Table} WHERE id = $id";
                        command.Parameters.AddWithValue("$id", entry.Id);
                    }
                    else
                    {
                        command.CommandText = $"INSERT OR REPLACE INTO {entry.Table}(id, value) VALUES ($id, $value)";

                        command.Parameters.AddWithValue("$id", entry.Id);
                        command.Parameters.AddWithValue("$value", entry.Value);
                    }

                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            foreach (ReadObjectsEntry entry in readObjects)
            {
                using (var command = transaction.Connection!.CreateCommand())
                {
                    command.CommandTimeout = TRANSACTION_TIMEOUT;
                    command.CommandText = $"SELECT value FROM {entry.Table} WHERE id == $id";

                    command.Parameters.AddWithValue("$id", entry.Id);
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        if (await reader.ReadAsync(cancellationToken))
                        {
                            string value = reader.GetString(0);
                            results.getValues[(entry.Table, entry.Id)] = value;
                        }
                        else
                        {
                            results.getValues[(entry.Table, entry.Id)] = null;
                        }
                    }
                }
            }

            foreach (SearchEntry entry in searchEntries)
            {
                var list = new List<(string Id, string? Value)>();
                var args = entry.Arguments;

                int? totalCount = null;

                using (var command = transaction.Connection!.CreateCommand())
                {
                    command.CommandTimeout = TRANSACTION_TIMEOUT;

                    var whereClauses = new List<string>();

                    if (args.MatchJson is not null && args.MatchJson.Count > 0)
                    {
                        int pIndex = 0;
                        foreach (var kvp in args.MatchJson)
                        {
                            string op = kvp.Value.Type == MatchType.Like ? "LIKE" : "=";
                            string pathParamName = $"@path_{pIndex}";
                            string valParamName = $"@val_{pIndex}";

                            whereClauses.Add($"json_extract(value, {pathParamName}) {op} {valParamName}");

                            var pathParam = command.CreateParameter();
                            pathParam.ParameterName = pathParamName;
                            pathParam.Value = kvp.Key;
                            command.Parameters.Add(pathParam);

                            var valParam = command.CreateParameter();
                            valParam.ParameterName = valParamName;
                            valParam.Value = kvp.Value.Value ?? (object)DBNull.Value;
                            command.Parameters.Add(valParam);

                            pIndex++;
                        }
                    }

                    string whereSql = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

                    string limitSql = "";
                    if (args.Take.HasValue)
                    {
                        limitSql = $"LIMIT {args.Take.Value}";
                        if (args.Skip.HasValue)
                        {
                            limitSql += $" OFFSET {args.Skip.Value}";
                        }
                    }
                    else if (args.Skip.HasValue)
                    {
                        // SQLite requires LIMIT to be present if OFFSET is used
                        limitSql = $"LIMIT -1 OFFSET {args.Skip.Value}";
                    }

                    string columns = args.IncludeValues ? "id, value" : "id";

                    var sqlBuilder = new StringBuilder();

                    if (args.GetTotalCount)
                    {
                        sqlBuilder.AppendLine(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM {entry.Table} {whereSql};");
                    }

                    sqlBuilder.AppendLine(CultureInfo.InvariantCulture, $"SELECT {columns} FROM {entry.Table} {whereSql}");
                    sqlBuilder.AppendLine("ORDER BY id");
                    sqlBuilder.AppendLine(limitSql + ";");

#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
                    command.CommandText = sqlBuilder.ToString();
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities

                    using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        if (args.GetTotalCount)
                        {
                            if (await reader.ReadAsync(cancellationToken))
                            {
                                totalCount = reader.GetInt32(0);
                            }

                            await reader.NextResultAsync(cancellationToken);
                        }

                        while (await reader.ReadAsync(cancellationToken))
                        {
                            string id = reader.GetString(0);
                            string? value = args.IncludeValues ? reader.GetString(1) : null;

                            list.Add((id, value));
                        }
                    }
                }

                results.searchResults[(entry.Table, args)] = (list, totalCount);
            }

            foreach (ExtrasEntry entry in extras)
            {
                results.extras[entry.Name] = entry.Value;
            }

            foreach (var entry in thenFunctions)
            {
                ObjectQuery query = entry.Function(results);
                ObjectResults innerResults = await query.ExecuteInternalAsync(transaction, write, cancellationToken);
                if (entry.ReplaceResults)
                {
                    results = innerResults;
                }
            }

            return results;
        }
    }

    private static object? FromJson(string json, Type valueType)
        => Json.Deserialize(json, valueType, jsonOptions);

    internal static T? FromJson<T>(string json)
        => Json.Deserialize<T>(json, jsonOptions);

    private static string ToJson(object value)
        => Json.Serialize(value, jsonOptions);

    internal static string ToJson<T>(T value)
        => Json.Serialize(value, jsonOptions);

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

        public Result GetResult(string name)
            => !getValues.TryGetValue(name, out Result? value) || value is null
            ? throw new KeyNotFoundException($"Key '{name}' was not found.")
            : value;

        public Result<T> GetResult<T>(string name)
            => !getValues.TryGetValue(name, out Result? value) || value is null
                ? throw new KeyNotFoundException()
                : new Result<T>((T)value.Value, value.Version);

        public object Get(string name)
            => GetResult(name).Value;

        public T Get<T>(string name)
            => GetResult<T>(name).Value;

        public Dictionary<string, int?> GetUpdates()
            => new Dictionary<string, int?>(updates);

        public object GetExtra(string name)
            => !extras.TryGetValue(name, out object? value) || value is null
            ? throw new KeyNotFoundException()
            : value;

        public record Result(object Value, int Version);

        public record struct Result<T>(T Value, int Version);
    }

    public sealed class ObjectResults
    {
        public Dictionary<(string TableName, ObjectQuery.SearchArguments SearchArguments), (List<(string Id, string? Value)> Results, int? TotalCount)> searchResults = [];

        public Dictionary<(string TableName, object Id), string?> getValues = [];
        public Dictionary<string, object> extras = [];

        public ObjectResults()
        {
            // empty
        }

        public string? GetTile(ulong pos)
            => !getValues.TryGetValue((TilesTable, pos), out string? objectId)
            ? null
            : objectId;

        public Models.Global.TemplateBuildplate? GetBuildplate(string id)
            => !getValues.TryGetValue((BuildplatesTable, id), out string? buildplateJson)
            ? null
            : buildplateJson is null ? null : FromJson<Models.Global.TemplateBuildplate>(buildplateJson);

        public (IEnumerable<(string Id, Models.Global.TemplateBuildplate? Buildplate)> Buildplates, int Count, int? TotalCount) GetBuildplates(ObjectQuery.SearchArguments arguments)
        {
            if (!searchResults.TryGetValue((BuildplatesTable, arguments), out var item))
            {
                return ([], 0, 0);
            }

            var (results, totalCount) = item;

            return (results.Select(result => (result.Id, result.Value is null ? null : FromJson<Models.Global.TemplateBuildplate>(result.Value))), results.Count, totalCount);
        }

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
