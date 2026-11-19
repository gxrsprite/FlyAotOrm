using System.Data.Common;
using FlyAotOrm;
using NewLife.NovaDb.Client;

namespace FlyAotOrm.Generator.Tests;

// NovaDb live tests: SQL-shape tests only assert strings; these run the real
// engine (NewLife.NovaDb 1.0.2026.901, embedded) to lock the Nova divergences:
// bare *, 1-IN-subquery Any, BETWEEN expansion, inline LIMIT/OFFSET,
// TRUE/FALSE inline booleans, @-prefixed parameter binding.
// Serial collection: embedded engines pin the temp dir (MMF) until released.
[CollectionDefinition("NovaDbLive", DisableParallelization = true)]
public class NovaDbLiveCollection { }

[Collection("NovaDbLive")]
public sealed class NovaDbLiveTests : IDisposable
{
    private readonly string _dir;
    private readonly NovaConnection _connection;
    private readonly FlyDb _db;
    private readonly NovaUserTable _users = new();
    private readonly NovaOrderTable _orders = new();

    public NovaDbLiveTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "flyaotorm-novadb-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        var connectionString = $"Data Source={_dir}";
        using (var setup = new NovaConnection(connectionString))
        {
            setup.Open();
            Execute(setup, "CREATE TABLE users (id INT PRIMARY KEY, name STRING(50), flag BOOL, age INT)");
            Execute(setup, "INSERT INTO users (id, name, flag, age) VALUES (1, 'alice', TRUE, 30), (2, 'bob', FALSE, 25), (3, 'carol', TRUE, 22)");
            Execute(setup, "CREATE TABLE orders (id INT PRIMARY KEY, user_id INT, amount INT)");
            Execute(setup, "INSERT INTO orders (id, user_id, amount) VALUES (1, 1, 100), (2, 1, 5)");
        }

        _connection = new NovaConnection(connectionString);
        _connection.Open();
        _db = new FlyDb(_connection, DbDialectKind.NovaDb);
    }

    public void Dispose()
    {
        try { _connection.Dispose(); } catch { }
        try { NovaConnection.DisposeSharedEngines(); } catch { }
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task SelectAll_ReturnsAllRows()
    {
        var rows = await _db.Select(_users).ToListAsync();
        Assert.True(rows.Count == 3, $"Expected 3 rows, got {rows.Count}.");
    }

    [Fact]
    public async Task WhereBool_ParameterizedAndInline()
    {
        Assert.Equal(2, (await _db.Select(_users).Where(_users.Flag.Eq(true)).ToListAsync()).Count);
        Assert.Equal(2, (await _db.Select(_users).Where(_users.Flag.Eq(true)).InlineParameters().ToListAsync()).Count);
        Assert.Single(await _db.Select(_users).Where(_users.Flag.Eq(false)).InlineParameters().ToListAsync());
    }

    [Fact]
    public async Task Paging_ReturnsExpectedSlices()
    {
        var page1 = await _db.Select(_users).OrderBy(_users.Id.Asc()).Page(1, 2).ToListAsync();
        var page2 = await _db.Select(_users).OrderBy(_users.Id.Asc()).Page(2, 2).ToListAsync();
        Assert.Equal([1, 2], page1.Select(static u => u.Id));
        Assert.Equal([3], page2.Select(static u => u.Id));
    }

    [Fact]
    public async Task JoinWithBetween_FiltersCorrectly()
    {
        var rows = await _db.Select(_users)
            .Join(_orders).On(_users.Id.Eq(_orders.UserId))
            .Where(_orders.Amount.Between(50, 200))
            .ToListAsync();
        Assert.Single(rows);
        Assert.Equal("alice", rows[0].Name);
    }

    [Fact]
    public async Task CountAsync_CountsFilteredRows()
    {
        Assert.Equal(3, await _db.Select(_users).Where(_users.Age.GreaterThan(20)).CountAsync());
    }

    [Fact]
    public async Task AnyAsync_ReturnsExistence()
    {
        Assert.True(await _db.Select(_users).Where(_users.Id.Eq(1)).AnyAsync());
        Assert.False(await _db.Select(_users).Where(_users.Id.Eq(999)).AnyAsync());
    }

    [Fact]
    public async Task Like_StartsWith_MatchesPrefix()
    {
        var rows = await _db.Select(_users).Where(_users.Name.StartsWith("a")).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("alice", rows[0].Name);
    }

    private static void Execute(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed record NovaUser(int Id, string Name, bool Flag, int Age);

    private sealed record NovaOrder(int Id, int UserId, int Amount);

    private sealed class NovaUserTable : DbTable<NovaUser>
    {
        public NovaUserTable()
            : base("users", "User", static reader => new NovaUser(reader.GetInt32(0), reader.GetString(1), reader.GetBoolean(2), reader.GetInt32(3)))
        {
            Id = new DbColumn<int>(this, "id");
            Name = new DbColumn<string>(this, "name");
            Flag = new DbColumn<bool>(this, "flag");
            Age = new DbColumn<int>(this, "age");
        }

        public DbColumn<int> Id { get; }

        public DbColumn<string> Name { get; }

        public DbColumn<bool> Flag { get; }

        public DbColumn<int> Age { get; }
    }

    private sealed class NovaOrderTable : DbTable<NovaOrder>
    {
        public NovaOrderTable()
            : base("orders", "Order", static reader => new NovaOrder(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)))
        {
            Id = new DbColumn<int>(this, "id");
            UserId = new DbColumn<int>(this, "user_id");
            Amount = new DbColumn<int>(this, "amount");
        }

        public DbColumn<int> Id { get; }

        public DbColumn<int> UserId { get; }

        public DbColumn<int> Amount { get; }
    }
}
