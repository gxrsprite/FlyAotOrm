using FlyAotOrm;
using Microsoft.Data.SqlClient;

namespace FlyAotOrm.Generator.Tests;

/// <summary>
/// SQL Server real-database tests. Requires FLYAOTORM_TEST_SQLSERVER pointing to a
/// dedicated disposable server/database; skipped otherwise. Uses Microsoft.Data.SqlClient.
/// </summary>
[CollectionDefinition("SqlServerLive", DisableParallelization = true)]
public class SqlServerLiveCollection { }

[Collection("SqlServerLive")]
public sealed class SqlServerLiveTests
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

    private static async Task<SqlConnection> OpenConnectionAsync()
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FLYAOTORM_TEST_SQLSERVER")!)
        {
            InitialCatalog = TestDatabase
        };
        var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    [ExternalDatabaseFact("FLYAOTORM_TEST_SQLSERVER")]
    public async Task GeneratedCrudRoundTrips()
    {
        await using var connection = await OpenConnectionAsync();

        await ExecuteAsync(connection, """
if object_id('dbo.generated_users', 'U') is not null
    drop table dbo.generated_users;

create table dbo.generated_users (
    id bigint not null identity(1,1) primary key,
    name nvarchar(100) not null,
    created_at datetime2 not null
);
""");

        try
        {
            await GeneratedUserCrud.InsertAsync(
                connection,
                new GeneratedUser { Name = "alice", CreatedAt = new DateTime(2026, 1, 1) },
                DbDialectKind.SqlServer);

            var user = await GeneratedUserCrud.GetByIdAsync(connection, 1, DbDialectKind.SqlServer);
            Assert.NotNull(user);
            Assert.Equal("alice", user.Name);

            user.Name = "alice-updated";
            Assert.Equal(1, await GeneratedUserCrud.UpdateAsync(connection, user, DbDialectKind.SqlServer));

            var all = await GeneratedUserCrud.ListAllAsync(connection, DbDialectKind.SqlServer);
            Assert.Single(all);
            Assert.Equal("alice-updated", all[0].Name);

            Assert.True(await GeneratedUserCrud.ExistsAsync(connection, 1, DbDialectKind.SqlServer));
            Assert.Equal(1, await GeneratedUserCrud.CountAsync(connection, DbDialectKind.SqlServer));

            Assert.Equal(1, await GeneratedUserCrud.DeleteByIdAsync(connection, 1, DbDialectKind.SqlServer));
            Assert.False(await GeneratedUserCrud.ExistsAsync(connection, 1, DbDialectKind.SqlServer));
        }
        finally
        {
            await ExecuteAsync(connection, "if object_id('dbo.generated_users', 'U') is not null drop table dbo.generated_users;");
        }
    }

    [ExternalDatabaseFact("FLYAOTORM_TEST_SQLSERVER")]
    public async Task InsertReturning_IdentityAndAssignedKeys()
    {
        await using var connection = await OpenConnectionAsync();

        await ExecuteAsync(connection, """
if object_id('dbo.generated_users', 'U') is not null
    drop table dbo.generated_users;
if object_id('dbo.assigned_key_items', 'U') is not null
    drop table dbo.assigned_key_items;

create table dbo.generated_users (
    id bigint not null identity(1,1) primary key,
    name nvarchar(100) not null,
    created_at datetime2 not null
);
create table dbo.assigned_key_items (
    id nvarchar(50) not null primary key,
    name nvarchar(100) not null
);
""");

        try
        {
            // OUTPUT INSERTED with identity key
            var identity = await GeneratedUserCrud.InsertReturningAsync(
                connection,
                new GeneratedUser { Name = "bob", CreatedAt = new DateTime(2026, 2, 2) },
                DbDialectKind.SqlServer);
            Assert.Equal(1, identity.Id);
            Assert.Equal("bob", identity.Name);
            Assert.Equal(new DateTime(2026, 2, 2), identity.CreatedAt);

            // OUTPUT INSERTED with assigned key
            var assigned = await AssignedKeyItemCrud.InsertReturningAsync(
                connection,
                new AssignedKeyItem { Id = "manual-1", Name = "carol" },
                DbDialectKind.SqlServer);
            Assert.Equal("manual-1", assigned.Id);
            Assert.Equal("carol", assigned.Name);
        }
        finally
        {
            await ExecuteAsync(connection, """
if object_id('dbo.generated_users', 'U') is not null drop table dbo.generated_users;
if object_id('dbo.assigned_key_items', 'U') is not null drop table dbo.assigned_key_items;
""");
        }
    }

    [ExternalDatabaseFact("FLYAOTORM_TEST_SQLSERVER")]
    public async Task QuotedKeywordIdentifiersRoundTrip()
    {
        await using var connection = await OpenConnectionAsync();

        await ExecuteAsync(connection, """
if object_id('dbo.[select]', 'U') is not null
    drop table dbo.[select];

create table dbo.[select] (
    [id] bigint not null identity(1,1) primary key,
    [from] nvarchar(100) not null
);
""");

        try
        {
            var inserted = await GeneratedKeywordRowCrud.InsertReturningAsync(
                connection,
                new GeneratedKeywordRow { From = "quoted-mssql" },
                DbDialectKind.SqlServer);

            Assert.Equal(1, inserted.Id);
            Assert.Equal("quoted-mssql", inserted.From);

            inserted.From = "quoted-updated";
            Assert.Equal(1, await GeneratedKeywordRowCrud.UpdateAsync(connection, inserted, DbDialectKind.SqlServer));

            var loaded = await GeneratedKeywordRowCrud.GetByIdAsync(connection, 1, DbDialectKind.SqlServer);
            Assert.NotNull(loaded);
            Assert.Equal("quoted-updated", loaded.From);
        }
        finally
        {
            await ExecuteAsync(connection, "if object_id('dbo.[select]', 'U') is not null drop table dbo.[select];");
        }
    }

    [ExternalDatabaseFact("FLYAOTORM_TEST_SQLSERVER")]
    public async Task QueryDslPagingCountAnyAndJoinProjection()
    {
        await using var connection = await OpenConnectionAsync();

        await ExecuteAsync(connection, """
if object_id('dbo.generated_users', 'U') is not null
    drop table dbo.generated_users;
if object_id('dbo.sqlserver_orders', 'U') is not null
    drop table dbo.sqlserver_orders;

create table dbo.generated_users (
    id bigint not null primary key,
    name nvarchar(100) not null,
    created_at datetime2 not null
);
create table dbo.sqlserver_orders (
    id bigint not null primary key,
    user_id bigint not null,
    amount decimal(10, 2) not null,
    status int not null
);

insert into dbo.generated_users(id, name, created_at) values
(1, N'alice', convert(datetime2, '2026-01-01T00:00:00')),
(2, N'bob', convert(datetime2, '2026-01-02T00:00:00')),
(3, N'anna', convert(datetime2, '2026-01-03T00:00:00')),
(4, N'amy', convert(datetime2, '2026-01-04T00:00:00'));

insert into dbo.sqlserver_orders(id, user_id, amount, status) values
(10, 1, 12.50, 1),
(11, 1, 8.25, 0),
(12, 2, 20.00, 1);
""");

        try
        {
            var db = new FlyDb(connection, DbDialectKind.SqlServer);
            var users = Tables.GeneratedUser;
            var orders = new SqlServerOrderTable();

            // LIKE + OrderBy + Skip + Take (OFFSET/FETCH)
            var paged = await db
                .Select(users)
                .Where(users.Name.StartsWith("a"))
                .OrderBy(users.Id.Desc())
                .Skip(1)
                .Take(2)
                .ToListAsync();

            Assert.Equal(new long[] { 3, 1 }, paged.Select(static r => r.Id).ToArray());

            // FirstOrDefaultAsync (offset 0 rows fetch next @limit rows only)
            var first = await db
                .Select(users)
                .Where(users.Name.StartsWith("a"))
                .OrderBy(users.Id.Desc())
                .FirstOrDefaultAsync();
            Assert.NotNull(first);
            Assert.Equal(4, first.Id);

            // CountAsync & AnyAsync (case when exists ... then 1 else 0 end)
            Assert.Equal(3, await db.SelectCount(users).Where(users.Name.StartsWith("a")).CountAsync());
            Assert.True(await db.SelectAny(users).Where(users.Name.Eq("anna")).AnyAsync());
            Assert.False(await db.SelectAny(users).Where(users.Name.Eq("missing")).AnyAsync());

            // InlineParameters
            var inline = await db
                .Select(users)
                .Where(users.Name.Eq("alice"))
                .InlineParameters()
                .ToListAsync();
            Assert.Single(inline);
            Assert.Equal(1, inline[0].Id);

            // Join DTO projection
            var joined = await db
                .Select<QueryDslTests.UserOrderRow>()
                .From(users)
                .Join(orders)
                .On(users.Id.Eq(orders.UserId))
                .Columns(
                    users.Id.As(nameof(QueryDslTests.UserOrderRow.UserId)),
                    users.Name.As(nameof(QueryDslTests.UserOrderRow.UserName)),
                    orders.Id.As(nameof(QueryDslTests.UserOrderRow.OrderId)),
                    orders.Amount.As(nameof(QueryDslTests.UserOrderRow.Amount)))
                .Where(orders.Status.Eq(1))
                .OrderBy(orders.Id.Asc())
                .ToListAsync();

            Assert.Equal(2, joined.Count);
            Assert.Equal("alice", joined[0].UserName);
            Assert.Equal(12.50m, joined[0].Amount);
            Assert.Equal("bob", joined[1].UserName);
            Assert.Equal(20.00m, joined[1].Amount);
        }
        finally
        {
            await ExecuteAsync(connection, """
if object_id('dbo.generated_users', 'U') is not null drop table dbo.generated_users;
if object_id('dbo.sqlserver_orders', 'U') is not null drop table dbo.sqlserver_orders;
""");
        }
    }

    [ExternalDatabaseFact("FLYAOTORM_TEST_SQLSERVER")]
    public async Task TypedValuesAndEnumStorageRoundTrip()
    {
        await using var connection = await OpenConnectionAsync();

        await ExecuteAsync(connection, """
if object_id('dbo.typed_rows', 'U') is not null
    drop table dbo.typed_rows;
if object_id('dbo.enum_rows', 'U') is not null
    drop table dbo.enum_rows;

create table dbo.typed_rows (
    id uniqueidentifier not null primary key,
    business_date date not null,
    start_time time not null,
    changed_at datetimeoffset not null
);
create table dbo.enum_rows (
    id nvarchar(50) not null primary key,
    number int null,
    [text] nvarchar(50) null
);
""");

        try
        {
            var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
            var businessDate = new DateOnly(2026, 6, 30);
            var startTime = new TimeOnly(9, 15, 30);
            var changedAt = new DateTimeOffset(2026, 6, 30, 9, 15, 30, TimeSpan.FromHours(8));

            var insertedTyped = await TypedRowCrud.InsertReturningAsync(
                connection,
                new TypedRow
                {
                    Id = id,
                    BusinessDate = businessDate,
                    StartTime = startTime,
                    ChangedAt = changedAt
                },
                DbDialectKind.SqlServer);

            Assert.Equal(id, insertedTyped.Id);
            Assert.Equal(businessDate, insertedTyped.BusinessDate);
            Assert.Equal(startTime, insertedTyped.StartTime);
            Assert.Equal(changedAt, insertedTyped.ChangedAt);

            var loadedTyped = await TypedRowCrud.GetByIdAsync(connection, id, DbDialectKind.SqlServer);
            Assert.NotNull(loadedTyped);
            Assert.Equal(id, loadedTyped.Id);
            Assert.Equal(businessDate, loadedTyped.BusinessDate);
            Assert.Equal(startTime, loadedTyped.StartTime);
            Assert.Equal(changedAt, loadedTyped.ChangedAt);

            var insertedEnum = await EnumStorageRowCrud.InsertReturningAsync(
                connection,
                new EnumStorageRow
                {
                    Id = EnumRowState.Pending,
                    Number = EnumRowState.Active,
                    Text = EnumRowState.Active
                },
                DbDialectKind.SqlServer);

            Assert.Equal(EnumRowState.Pending, insertedEnum.Id);
            Assert.Equal(EnumRowState.Active, insertedEnum.Number);
            Assert.Equal(EnumRowState.Active, insertedEnum.Text);

            var db = new FlyDb(connection, DbDialectKind.SqlServer);
            var enumTable = Tables.EnumStorageRow;
            var matched = await db
                .Select(enumTable)
                .Where(enumTable.Text.Eq(EnumRowState.Active))
                .And(enumTable.Number.Eq(EnumRowState.Active))
                .ToListAsync();
            Assert.Single(matched);
            Assert.Equal(EnumRowState.Pending, matched[0].Id);
        }
        finally
        {
            await ExecuteAsync(connection, """
if object_id('dbo.typed_rows', 'U') is not null drop table dbo.typed_rows;
if object_id('dbo.enum_rows', 'U') is not null drop table dbo.enum_rows;
""");
        }
    }

    private sealed class SqlServerOrderTable : DbTable<SqlServerOrder>
    {
        public SqlServerOrderTable() : base("sqlserver_orders", "Order", Materialize)
        {
            Id = new DbColumn<long>(this, "id");
            UserId = new DbColumn<long>(this, "user_id");
            Amount = new DbColumn<decimal>(this, "amount");
            Status = new DbColumn<int>(this, "status");
        }

        public DbColumn<long> Id { get; }
        public DbColumn<long> UserId { get; }
        public DbColumn<decimal> Amount { get; }
        public DbColumn<int> Status { get; }

        private static SqlServerOrder Materialize(System.Data.Common.DbDataReader reader)
            => new()
            {
                Id = reader.GetInt64(0),
                UserId = reader.GetInt64(1),
                Amount = reader.GetDecimal(2),
                Status = reader.GetInt32(3)
            };
    }

    private sealed class SqlServerOrder
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public decimal Amount { get; set; }
        public int Status { get; set; }
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
