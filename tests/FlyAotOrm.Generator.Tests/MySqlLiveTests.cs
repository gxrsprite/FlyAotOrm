using FlyAotOrm;
using MySqlConnector;

namespace FlyAotOrm.Generator.Tests;

/// <summary>
/// MySQL real-database tests. Requires FLYAOTORM_TEST_MYSQL pointing to a
/// dedicated disposable database; skipped otherwise. Uses MySqlConnector.
/// </summary>
[CollectionDefinition("MySqlLive", DisableParallelization = true)]
public class MySqlLiveCollection { }

[Collection("MySqlLive")]
public sealed class MySqlLiveTests
{
    // The solution runs all three TFMs in parallel processes. Give each TFM its
    // own database so concurrent drop/create of the same tables cannot collide.
    private static string TestDatabase
    {
        get
        {
#if NET11_0
            return "flyaotorm_test_net11";
#elif NET10_0
            return "flyaotorm_test_net10";
#else
            return "flyaotorm_test_net8";
#endif
        }
    }

    private static async Task<MySqlConnection> OpenConnectionAsync()
    {
        var builder = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FLYAOTORM_TEST_MYSQL")!);
        builder.Database = TestDatabase;
        var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    [ExternalDatabaseFact("FLYAOTORM_TEST_MYSQL")]
    public async Task ConnectivityAndMultiStatementLastInsertId()
    {
        await using var connection = await OpenConnectionAsync();

        await ExecuteAsync(connection, """
drop table if exists flyaotorm_probe;
create table flyaotorm_probe (
    id bigint not null auto_increment primary key,
    name varchar(100) not null
);
""");

        try
        {
            // MySqlConnector allows multiple statements by default: verify that
            // ExecuteReader exposes the result set of the final SELECT, which is
            // what a generated InsertReturningAsync will rely on.
            await using var command = connection.CreateCommand();
            command.CommandText = """
insert into flyaotorm_probe (name) values ('alice');
select id, name from flyaotorm_probe where id = last_insert_id();
""";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal("alice", reader.GetString(1));
            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            await ExecuteAsync(connection, "drop table if exists flyaotorm_probe;");
        }
    }

    [ExternalDatabaseFact("FLYAOTORM_TEST_MYSQL")]
    public async Task GeneratedCrudRoundTrips()
    {
        await using var connection = await OpenConnectionAsync();

        await ExecuteAsync(connection, """
drop table if exists generated_users;
create table generated_users (
    id bigint not null auto_increment primary key,
    name varchar(100) not null,
    created_at datetime(6) not null
);
""");

        try
        {
            await GeneratedUserCrud.InsertAsync(
                connection,
                new GeneratedUser { Name = "alice", CreatedAt = new DateTime(2026, 1, 1) },
                DbDialectKind.MySql);

            var user = await GeneratedUserCrud.GetByIdAsync(connection, 1, DbDialectKind.MySql);
            Assert.NotNull(user);
            Assert.Equal("alice", user.Name);

            user.Name = "alice-updated";
            Assert.Equal(1, await GeneratedUserCrud.UpdateAsync(connection, user, DbDialectKind.MySql));

            var all = await GeneratedUserCrud.ListAllAsync(connection, DbDialectKind.MySql);
            Assert.Single(all);
            Assert.Equal("alice-updated", all[0].Name);

            Assert.True(await GeneratedUserCrud.ExistsAsync(connection, 1, DbDialectKind.MySql));
            Assert.Equal(1, await GeneratedUserCrud.CountAsync(connection, DbDialectKind.MySql));

            Assert.Equal(1, await GeneratedUserCrud.DeleteByIdAsync(connection, 1, DbDialectKind.MySql));
            Assert.False(await GeneratedUserCrud.ExistsAsync(connection, 1, DbDialectKind.MySql));
        }
        finally
        {
            await ExecuteAsync(connection, "drop table if exists generated_users;");
        }
    }

    [ExternalDatabaseFact("FLYAOTORM_TEST_MYSQL")]
    public async Task InsertReturning_IdentityAndAssignedKeys()
    {
        await using var connection = await OpenConnectionAsync();

        await ExecuteAsync(connection, """
drop table if exists generated_users;
drop table if exists assigned_key_items;
create table generated_users (
    id bigint not null auto_increment primary key,
    name varchar(100) not null,
    created_at datetime(6) not null
);
create table assigned_key_items (
    id varchar(50) not null primary key,
    name varchar(100) not null
);
""");

        try
        {
            // identity key -> LAST_INSERT_ID()
            var identity = await GeneratedUserCrud.InsertReturningAsync(
                connection,
                new GeneratedUser { Name = "bob", CreatedAt = new DateTime(2026, 2, 2) },
                DbDialectKind.MySql);
            Assert.Equal(1, identity.Id);
            Assert.Equal("bob", identity.Name);

            // assigned key -> bound @Id parameter reused in the SELECT
            var assigned = await AssignedKeyItemCrud.InsertReturningAsync(
                connection,
                new AssignedKeyItem { Id = "manual-1", Name = "carol" },
                DbDialectKind.MySql);
            Assert.Equal("manual-1", assigned.Id);
            Assert.Equal("carol", assigned.Name);
        }
        finally
        {
            await ExecuteAsync(connection, "drop table if exists generated_users; drop table if exists assigned_key_items;");
        }
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
