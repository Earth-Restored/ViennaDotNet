using Npgsql;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ViennaDotNet.TileRenderer;

// only for testing, TODO: convert to lib type

var log = new LoggerConfiguration()
   .WriteTo.Console()
   .MinimumLevel.Debug()
   .CreateLogger();

Log.Logger = log;

string connectionString = args[0];
await using var dataSource = NpgsqlDataSource.Create(connectionString);

Renderer renderer = Renderer.Create(File.ReadAllText("tagMap.json"), log);

await renderer.RenderAsync(dataSource);