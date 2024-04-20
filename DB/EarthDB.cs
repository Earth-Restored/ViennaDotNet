using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.Diagnostics;
using System.Transactions;
using System.Xml.Linq;
using ViennaDotNet.Excceptions;
using ViennaDotNet.Utils;

namespace ViennaDotNet.DB
{
    public sealed class EarthDB : IDisposable
    {
        public static EarthDB Open(string connectionString)
        {
            return new EarthDB(connectionString);
        }

        private string connectionString;
        private HashSet<Transaction> transactions = new HashSet<Transaction>();

        private EarthDB(string _connectionString)
        {
            connectionString = _connectionString;

            try
            {
                using var connection = new SQLiteConnection("Data Source=" + connectionString);
                connection.Open();
                using (var command = new SQLiteCommand("CREATE TABLE IF NOT EXISTS objects (type STRING NOT NULL, id STRING NOT NULL, value STRING NOT NULL, version INTEGER NOT NULL, PRIMARY KEY (type, id))", connection))
                    command.ExecuteNonQuery();

            }
            catch (SQLiteException ex)
            {
                throw new DatabaseException(ex);
            }
        }

        private Transaction transaction(bool write)
        {
            lock (this)
            {
                try
                {
                    using var connection = new SQLiteConnection("Data Source=" + connectionString);
                    connection.Open();
                    var transaction = new Transaction(this, connection, write);
                    transactions.Add(transaction);
                    return transaction;
                }
                catch (SQLiteException ex)
                {
                    throw new DatabaseException(ex);
                }
            }
        }

        public void Dispose()
        {
            lock (this)
            {
                foreach (var transaction in transactions)
                    try
                    {
                        transaction.Dispose();
                    }
                    catch { }
            }
        }

        private sealed class Transaction : IDisposable
        {
            private readonly EarthDB db;
            public readonly SQLiteConnection Connection;
            private bool committed;

            public Transaction(EarthDB _db, SQLiteConnection _connection, bool write)
            {
                db = _db;
                Connection = _connection;

                try
                {
                    using (var command = new SQLiteCommand(write ? "BEGIN IMMEDIATE TRANSACTION" : "BEGIN DEFERRED TRANSACTION", Connection))
                        command.ExecuteNonQuery();
                }
                catch (SQLiteException ex)
                {
                    try
                    {
                        Connection.Close();
                        Connection.Dispose();
                    }
                    catch
                    {
                        // empty
                    }
                    throw new DatabaseException(ex);
                }
            }

            public void Commit()
            {
                try
                {
                    using (var command = new SQLiteCommand("COMMIT TRANSACTION", Connection))
                        command.ExecuteNonQuery();
                }
                catch (SQLiteException ex)
                {
                    throw new DatabaseException(ex);
                }

                committed = true;
            }

            public void Dispose()
            {
                if (!committed)
                    try
                    {
                        using (var command = new SQLiteCommand("ROLLBACK TRANSACTION", Connection))
                            command.ExecuteNonQuery();
                    }
                    catch (SQLiteException ex)
                    {
                        throw new DatabaseException(ex);
                    }

                lock (db)
                {
                    db.transactions.Remove(this);
                    try
                    {
                        Connection.Close();
                    }
                    catch (SQLiteException)
                    {
                        // Empty
                    }
                }
            }
        }

        public class Query
        {
            private bool write;
            private List<WriteObjectsEntry> writeObjects = new();
            private List<ReadObjectsEntry> readObjects = new();
            private List<ExtrasEntry> extras = new();
            private List<Func<Results, Query>> thenFunctions = new();

            private record WriteObjectsEntry(string type, string id, object value)
            {
            }

            private record ReadObjectsEntry(string type, string id, Type valueType)
            {
            }
            private record ExtrasEntry(string name, object value)
            {
            }

            public Query(bool _write)
            {
                write = _write;
            }

            public Query Update(string type, string id, object value)
            {
                if (!write)
                    throw new UnsupportedOperationException();

                writeObjects.Add(new WriteObjectsEntry(type, id, value));
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

            public Query Then(Func<Results, Query> function)
            {
                thenFunctions.Add(function);
                return this;
            }

            //public Query then(Query query)
            //{
            //    thenFunctions.Add(results.query);
            //    return this;
            //}

            public Results Execute(EarthDB earthDB)
            {
                try
                {
                    using Transaction transaction = earthDB.transaction(write);
                    Results results = executeInternal(transaction, write, null);
                    transaction.Commit();
                    return results;
                }
                catch (SqlException exception)
                {
                    throw new DatabaseException(exception);
                }
            }

            private Results executeInternal(Transaction transaction, bool write, Dictionary<string, int?> parentUpdates)
            {
                if (this.write && !write)
                    throw new UnsupportedOperationException();

                Results results = new Results();
                if (parentUpdates != null)
                    results.updates.AddRange(parentUpdates);

                foreach (WriteObjectsEntry entry in writeObjects)
                {
                    string json = JsonConvert.SerializeObject(entry.value);
                    SQLiteCommand statement = new SQLiteCommand($"INSERT OR REPLACE INTO objects(type, id, value, version) VALUES ({entry.type}, {entry.id}, {json}, COALESCE((SELECT version FROM objects WHERE type == {entry.type} AND id == {entry.id}), 1) + 1)", transaction.Connection);
                    /*statement.setString(1, entry.type);
                    statement.setString(2, entry.id);
                    statement.setString(3, json);
                    statement.setString(4, entry.type);
                    statement.setString(5, entry.id);
                    statement.execute();*/
                    statement.ExecuteNonQuery();
                    using (var command = new SQLiteCommand("INSERT OR REPLACE INTO objects(type, id, value, version) VALUES (@type, @id, @value, COALESCE((SELECT version FROM objects WHERE type == @type AND id == @id), 1) + 1)", transaction.Connection))
                    {
                        command.Parameters.AddWithValue("@type", entry.type);
                        command.Parameters.AddWithValue("@id", entry.id);
                        command.Parameters.AddWithValue("@value", json);
                        command.ExecuteNonQuery();
                    }

                    using (var command = new SQLiteCommand("SELECT version FROM objects WHERE type == @type AND id == @id", transaction.Connection))
                    {
                        command.Parameters.AddWithValue("@type", entry.type);
                        command.Parameters.AddWithValue("@id", entry.id);
                        using (var reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                var version = reader.GetInt32(0);
                                results.updates.Add(entry.type, version);
                            }
                            else
                            {
                                throw new DatabaseException("Could not query updated object");
                            }
                        }
                    }
                }

                foreach (ReadObjectsEntry entry in readObjects)
                {
                    using (var command = new SQLiteCommand("SELECT value, version FROM objects WHERE type == @type AND id == @id", transaction.Connection))
                    {
                        command.Parameters.AddWithValue("@type", entry.type);
                        command.Parameters.AddWithValue("@id", entry.id);
                        using (var reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                var json = reader.GetString(0);
                                var version = reader.GetInt32(1);
                                var value = JsonConvert.DeserializeObject(json, entry.valueType)!;
                                results.getValues.Add(entry.type, new Results.Result(value, version));
                            }
                            else
                            {
                                try
                                {
                                    var value = Activator.CreateInstance(entry.valueType)!;
                                    results.getValues.Add(entry.type, new Results.Result(value, 1));
                                }
                                catch (Exception exception)
                                {
                                    throw new DatabaseException(exception);
                                }
                            }
                        }
                    }
                    //                try (PreparedStatement statement = transaction.connection.prepareStatement("SELECT value, version FROM objects WHERE type == ? AND id == ?"))
                    //{
                    //                statement.setString(1, entry.type);
                    //                statement.setString(2, entry.id);
                    //                statement.execute();
                    //                ResultSet resultSet = statement.getResultSet();
                    //                if (resultSet.next())
                    //                {
                    //                    String json = resultSet.getString("value");
                    //                    int version = resultSet.getInt("version");
                    //                    Object value = new Gson().fromJson(json, entry.valueClass);
                    //                    results.getValues.put(entry.type, new Results.Result<>(value, version));
                    //                }
                    //                else
                    //                {
                    //                    try
                    //                    {
                    //                        Constructor constructor = entry.valueClass.getDeclaredConstructor();
                    //                        Object value = constructor.newInstance();
                    //                        results.getValues.put(entry.type, new Results.Result<>(value, 1));
                    //                    }
                    //                    catch (ReflectiveOperationException exception)
                    //                    {
                    //                        throw new DatabaseException(exception);
                    //                    }
                    //                }
                    //            }
                }

                foreach (ExtrasEntry entry in extras)
                {
                    results.extras.Add(entry.name, entry.value);
                }

                foreach (Func<Results, Query> function in thenFunctions)
                {
                    Query query = function.Invoke(results);
                    results = query.executeInternal(transaction, write, results.updates);
                }

                return results;
            }
        }

        public class Results
        {
            public Dictionary<string, Result> getValues = new();
            public Dictionary<string, object> extras = new();
            public Dictionary<string, int?> updates = new();

            public Results()
            {
                // empty
            }

            public Result Get(string name)
            {
                if (!getValues.TryGetValue(name, out Result? value) || value is null)
                    throw new KeyNotFoundException();
                else
                    return value;
            }

            public Dictionary<string, int?> getUpdates()
            {
                return new Dictionary<string, int?>(updates);
            }

            public object getExtra(string name)
            {
                if (!extras.TryGetValue(name, out object? value) || value is null)
                    throw new KeyNotFoundException();
                else
                    return value;
            }

            public record Result(object Value, int version)
            {
            }
        }

        public class DatabaseException : Exception
        {
            public DatabaseException() { }
            public DatabaseException(string message) : base(message) { }
            public DatabaseException(string message, Exception innerException) : base(message, innerException) { }
            public DatabaseException(Exception innerException) : base("Database operation failed.", innerException) { }
        }
    }
}
