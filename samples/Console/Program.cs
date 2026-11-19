using FlyAotOrm;
using Microsoft.Data.Sqlite;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();

await ExecuteAsync(connection, """
create table users (
    id integer not null primary key,
    name text not null,
    status integer not null,
    created_at text not null
);

create table orders (
    id integer not null primary key,
    user_id integer not null,
    amount real not null,
    status integer not null,
    created_at text not null
);

insert into orders(id, user_id, amount, status, created_at) values
(10, 1, 12.5, 1, '2026-01-03'),
(11, 1, 8.25, 0, '2026-01-04');
""");

await UserCrud.InsertAsync(connection, new User
{
    Id = 1,
    Name = "alice",
    Status = UserStatus.Active,
    CreatedAt = new DateTime(2026, 1, 1)
});

await UserCrud.InsertAsync(connection, new User
{
    Id = 2,
    Name = "bob",
    Status = UserStatus.Disabled,
    CreatedAt = new DateTime(2026, 1, 2)
});

var db = new FlyDb(connection, DbDialectKind.Sqlite);
var appDb = new FlyDbContext(db);
var u = Tables.User;
var o = Tables.Order;

var activeUsersQuery = appDb
    .Users
    .Where(u.Status.Eq(UserStatus.Active))
    .OrderBy(u.Id.Asc());

var activeUsersSql = activeUsersQuery.ToSql();

var userOrdersSql = db
    .Select<UserOrderRow>()
    .From(u)
    .LeftJoin(o)
    .On(u.Id.Eq(o.UserId))
    .Columns(
        u.Id.As(nameof(UserOrderRow.UserId)),
        u.Name.As(nameof(UserOrderRow.UserName)),
        o.Id.As(nameof(UserOrderRow.OrderId)),
        o.Amount.As(nameof(UserOrderRow.Amount)))
    .Where(u.Id.Eq(1))
    .OrderBy(o.Id.Asc())
    .ToSql();

var activeUsers = await activeUsersQuery.ToListAsync(CancellationToken.None);
var activeUserCount = await activeUsersQuery.CountAsync(CancellationToken.None);
var firstUserOrder = await db
    .Select<UserOrderRow>()
    .From(u)
    .LeftJoin(o)
    .On(u.Id.Eq(o.UserId))
    .Columns(
        u.Id.As(nameof(UserOrderRow.UserId)),
        u.Name.As(nameof(UserOrderRow.UserName)),
        o.Id.As(nameof(UserOrderRow.OrderId)),
        o.Amount.As(nameof(UserOrderRow.Amount)))
    .Where(u.Id.Eq(1))
    .OrderBy(o.Id.Asc())
    .FirstOrDefaultAsync(CancellationToken.None);
var generatedCrudCount = await UserCrud.CountAsync(connection, cancellationToken: CancellationToken.None);
var repository = new UserRepository(connection);
var repositoryUser = await repository.GetByIdAsync(1, CancellationToken.None);
var repositoryActiveUsers = await repository
    .Query()
    .Where(u.Status.Eq(UserStatus.Active))
    .ToListAsync(CancellationToken.None);
var repositoryActiveUserCount = await repository
    .CountQuery()
    .Where(u.Status.Eq(UserStatus.Active))
    .CountAsync(CancellationToken.None);
var repositoryHasDisabledUsers = await repository
    .AnyQuery()
    .Where(u.Status.Eq(UserStatus.Disabled))
    .AnyAsync(CancellationToken.None);
var repositoryRawActiveUserCount = await repository.CountByStatusAsync(UserStatus.Active);
var userOrders = await db
    .SelectCount(u)
    .LeftJoin(o)
    .On(u.Id.Eq(o.UserId))
    .Where(u.Id.Eq(1))
    .CountAsync(CancellationToken.None);
var hasActiveOrders = await db
    .SelectAny(o)
    .Where(o.UserId.Eq(1))
    .And(o.Status.Eq(1))
    .AnyAsync(CancellationToken.None);

Console.WriteLine($"Entity: {UserMetadata.TableName}");
Console.WriteLine($"Active users SQL: {activeUsersSql.Sql}");
Console.WriteLine($"Join SQL: {userOrdersSql.Sql}");
Console.WriteLine($"Active users: {activeUserCount}");
Console.WriteLine($"Generated CRUD users: {generatedCrudCount}");
Console.WriteLine($"Repository user: {repositoryUser?.Name}");
Console.WriteLine($"Repository active users: {repositoryActiveUsers.Count}");
Console.WriteLine($"Repository active user count: {repositoryActiveUserCount}");
Console.WriteLine($"Repository has disabled users: {repositoryHasDisabledUsers}");
Console.WriteLine($"Repository raw active user count: {repositoryRawActiveUserCount}");
Console.WriteLine($"User orders: {userOrders}");
Console.WriteLine($"Has active orders: {hasActiveOrders}");
Console.WriteLine($"First user order: {firstUserOrder?.OrderId}");
Console.WriteLine($"First active user: {activeUsers[0].Name}");

static async Task ExecuteAsync(System.Data.Common.DbConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await command.ExecuteNonQueryAsync();
}

[DbTable("users")]
public sealed partial class User
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("name")]
    public string Name { get; set; } = "";

    [DbColumn("status")]
    public UserStatus Status { get; set; }

    [DbColumn("created_at")]
    public DateTime CreatedAt { get; set; }
}

[DbTable("orders")]
public sealed partial class Order
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("user_id")]
    public long UserId { get; set; }

    [DbColumn("amount")]
    public decimal Amount { get; set; }

    [DbColumn("status")]
    public int Status { get; set; }

    [DbColumn("created_at")]
    public DateTime CreatedAt { get; set; }
}

public enum UserStatus
{
    Disabled = 0,
    Active = 1
}

public sealed class UserOrderRow
{
    public long UserId { get; set; }

    public string UserName { get; set; } = "";

    public long OrderId { get; set; }

    public decimal Amount { get; set; }
}

[DbRepository]
public partial interface IUserRepository
{
    Query<User> Query();

    CountQuery<User> CountQuery();

    AnyQuery<User> AnyQuery();

    [SqlQuery("""
select *
from users
where id = @id
""")]
    Task<User?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    [SqlExecute("""
update users
set name = @name
where id = @id
""")]
    Task<int> UpdateNameAsync(long id, string name);

    [SqlScalar("select count(1) from users where status = @status")]
    Task<long> CountByStatusAsync(UserStatus status);
}
