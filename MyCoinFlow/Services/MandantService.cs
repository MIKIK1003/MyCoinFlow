using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace MyCoinFlow.Services
{
    public sealed class MandantService
    {
        private const string MasterConn = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Initial Catalog=master;";

        /// <summary>Listet alle LocalDB-Datenbanken, die eine dbo.Users-Tabelle haben (unsere „Mandanten“).</summary>
        public async Task<IList<string>> GetMandantenAsync()
        {
            var result = new List<string>();
            var names = new List<string>();

            await using (var c = new SqlConnection(MasterConn))
            {
                await c.OpenAsync();
                var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT name FROM sys.databases WHERE database_id > 4 ORDER BY name";
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    names.Add(r.GetString(0));
            }

            foreach (var db in names)
            {
                try
                {
                    var cs = $@"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Initial Catalog={db};";
                    await using var c2 = new SqlConnection(cs);
                    await c2.OpenAsync();
                    var cmd2 = c2.CreateCommand();
                    cmd2.CommandText = "SELECT OBJECT_ID(N'dbo.Users','U')";
                    var obj = await cmd2.ExecuteScalarAsync();
                    if (obj != null && obj != DBNull.Value)
                        result.Add(db);
                }
                catch { /* defekte DB ignorieren */ }
            }

            return result;
        }

        public void SetActive(string dbName) => ConnectionStrings.SetActiveDatabase(dbName);
    }
}
