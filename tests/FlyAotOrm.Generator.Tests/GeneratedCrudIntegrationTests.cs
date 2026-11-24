using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace FlyAotOrm.Generator.Tests;

public sealed class GeneratedCrudIntegrationTests
{
    [Fact]
    public async Task GeneratedCrudExecutesAgainstSqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
create table generated_users (
    id integer not null primary key,
    name text not null,
    created_at text not null
);
""");

        await GeneratedUserCrud.InsertAsync(connection, new GeneratedUser
        {
            Name = "alice",
            CreatedAt = new DateTime(2026, 1, 1)
        });

        var user = await GeneratedUserCrud.GetByIdAsync(connection, 1);

        Assert.NotNull(user);
        Assert.Equal("alice", user.Name);

        var exists = await GeneratedUserCrud.ExistsAsync(connection, 1);
        Assert.True(exists);

        var count = await GeneratedUserCrud.CountAsync(connection);
        Assert.Equal(1, count);

        user.Name = "alice-updated";
        var updated = await GeneratedUserCrud.UpdateAsync(connection, user);
        Assert.Equal(1, updated);

        var updatedUser = await GeneratedUserCrud.GetByIdAsync(connection, 1);
        Assert.NotNull(updatedUser);
        Assert.Equal("alice-updated", updatedUser.Name);

        await GeneratedUserCrud.InsertAsync(connection, new GeneratedUser
        {
            Name = "bob",
            CreatedAt = new DateTime(2026, 1, 2)
        });

        var returned = await GeneratedUserCrud.InsertReturningAsync(connection, new GeneratedUser
        {
            Name = "carol",
            CreatedAt = new DateTime(2026, 1, 3)
        });

        Assert.Equal(3, returned.Id);
        Assert.Equal("carol", returned.Name);
        Assert.Equal(new DateTime(2026, 1, 3), returned.CreatedAt);

        var all = await GeneratedUserCrud.ListAllAsync(connection);
        Assert.Equal(new[] { "alice-updated", "bob", "carol" }, all.Select(static item => item.Name).ToArray());

        var deleted = await GeneratedUserCrud.DeleteByIdAsync(connection, 1);
        Assert.Equal(1, deleted);

        var missing = await GeneratedUserCrud.GetByIdAsync(connection, 1);
        Assert.Null(missing);

        var missingExists = await GeneratedUserCrud.ExistsAsync(connection, 1);
        Assert.False(missingExists);
    }

    [Fact]
    public async Task GeneratedCrudQuotesIdentifiersForSqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
create table "select" (
    "id" integer not null primary key,
    "from" text not null
);
""");

        await GeneratedKeywordRowCrud.InsertAsync(
            connection,
            new GeneratedKeywordRow { From = "quoted-default" });

        var defaultDialectRow = await GeneratedKeywordRowCrud.GetByIdAsync(connection, 1);
        Assert.NotNull(defaultDialectRow);
        Assert.Equal("quoted-default", defaultDialectRow.From);

        defaultDialectRow.From = "quoted-explicit";
        await GeneratedKeywordRowCrud.UpdateAsync(
            connection,
            defaultDialectRow,
            DbDialectKind.Sqlite);

        var explicitDialectRow = await GeneratedKeywordRowCrud.GetByIdAsync(
            connection,
            1,
            DbDialectKind.Sqlite);

        Assert.NotNull(explicitDialectRow);
        Assert.Equal("quoted-explicit", explicitDialectRow.From);
    }

    [Fact]
    public async Task GeneratedCrudSupportsAssignedKeys()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
create table assigned_key_items (
    id text not null primary key,
    name text not null
);
""");

        await AssignedKeyItemCrud.InsertAsync(connection, new AssignedKeyItem
        {
            Id = "item-1",
            Name = "assigned"
        });

        var item = await AssignedKeyItemCrud.GetByIdAsync(connection, "item-1");
        Assert.NotNull(item);
        Assert.Equal("assigned", item.Name);

        var returned = await AssignedKeyItemCrud.InsertReturningAsync(connection, new AssignedKeyItem
        {
            Id = "item-2",
            Name = "returned"
        });

        Assert.Equal("item-2", returned.Id);
        Assert.Equal("returned", returned.Name);
    }

    [Fact]
    public async Task GeneratedFlyDbContextExecutesEntityQueries()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
create table generated_users (
    id integer not null primary key,
    name text not null,
    created_at text not null
);

insert into generated_users(id, name, created_at) values
(1, 'alice', '2026-01-01'),
(2, 'bob', '2026-01-02'),
(3, 'anna', '2026-01-03');
""");

        var db = new FlyDb(connection, DbDialectKind.Sqlite);
        var context = new FlyDbContext(db);

        var rendered = context.GeneratedUsers
            .Where(Tables.GeneratedUser.Name.StartsWith("a"))
            .OrderBy(Tables.GeneratedUser.Id.Desc())
            .ToSql();

        Assert.Equal(
            "select \"GeneratedUser\".* from \"generated_users\" as \"GeneratedUser\" where \"GeneratedUser\".\"name\" like @p0 order by \"GeneratedUser\".\"id\" desc",
            rendered.Sql);

        var rows = await context.GeneratedUsers
            .Where(Tables.GeneratedUser.Name.StartsWith("a"))
            .OrderBy(Tables.GeneratedUser.Id.Desc())
            .ToListAsync();

        Assert.Equal(new[] { "anna", "alice" }, rows.Select(static row => row.Name).ToArray());
    }

    [Fact]
    public async Task GeneratedQueryEntrypointsApplySoftDeleteFilter()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
create table soft_delete_users (
    id integer not null primary key,
    name text not null,
    is_deleted integer not null
);

insert into soft_delete_users(id, name, is_deleted) values
(1, 'alice', 0),
(2, 'bob', 1),
(3, 'anna', 0);
""");

        var db = new FlyDb(connection, DbDialectKind.Sqlite);
        var context = new FlyDbContext(db);

        var rendered = context.SoftDeleteUsers
            .OrderBy(Tables.SoftDeleteUser.Id.Asc())
            .ToSql();

        Assert.Equal(
            "select \"SoftDeleteUser\".* from \"soft_delete_users\" as \"SoftDeleteUser\" where \"SoftDeleteUser\".\"is_deleted\" = @p0 order by \"SoftDeleteUser\".\"id\" asc",
            rendered.Sql);
        Assert.Equal(false, rendered.Parameters[0].Value);

        var contextRows = await context.SoftDeleteUsers
            .OrderBy(Tables.SoftDeleteUser.Id.Asc())
            .ToListAsync();

        Assert.Equal(new[] { "alice", "anna" }, contextRows.Select(static row => row.Name).ToArray());

        var repository = new SoftDeleteUserRepository(connection);
        var repositoryRows = await repository.Query()
            .OrderBy(Tables.SoftDeleteUser.Id.Asc())
            .ToListAsync();

        Assert.Equal(new[] { "alice", "anna" }, repositoryRows.Select(static row => row.Name).ToArray());
        Assert.Equal(2, await repository.CountQuery().CountAsync());
        Assert.True(await repository.AnyQuery().Where(Tables.SoftDeleteUser.Name.Eq("anna")).AnyAsync());
        Assert.False(await repository.AnyQuery().Where(Tables.SoftDeleteUser.Name.Eq("bob")).AnyAsync());

        var rawRows = await repository.ListRawAsync();
        Assert.Equal(new[] { "alice", "bob", "anna" }, rawRows.Select(static row => row.Name).ToArray());

        Assert.Equal(1, await repository.CountRawAsync(isDeleted: true));
        Assert.Equal(2, await repository.CountRawAsync(isDeleted: false));
    }

    [Fact]
    public async Task GeneratedQueryEntrypointsApplyCustomDefaultFilter()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
create table filtered_users (
    id integer not null primary key,
    name text not null,
    is_active integer not null
);

insert into filtered_users(id, name, is_active) values
(1, 'alice', 1),
(2, 'bob', 0),
(3, 'anna', 1);
""");

        var db = new FlyDb(connection, DbDialectKind.Sqlite);
        var context = new FlyDbContext(db);

        var rendered = context.FilteredUsers
            .OrderBy(Tables.FilteredUser.Id.Asc())
            .ToSql();

        Assert.Equal(
            "select \"FilteredUser\".* from \"filtered_users\" as \"FilteredUser\" where \"FilteredUser\".\"is_active\" = @p0 order by \"FilteredUser\".\"id\" asc",
            rendered.Sql);
        Assert.Equal(true, rendered.Parameters[0].Value);

        var contextRows = await context.FilteredUsers
            .OrderBy(Tables.FilteredUser.Id.Asc())
            .ToListAsync();

        Assert.Equal(new[] { "alice", "anna" }, contextRows.Select(static row => row.Name).ToArray());

        var repository = new FilteredUserRepository(connection);
        var repositoryRows = await repository.Query()
            .OrderBy(Tables.FilteredUser.Id.Asc())
            .ToListAsync();

        Assert.Equal(new[] { "alice", "anna" }, repositoryRows.Select(static row => row.Name).ToArray());
    }

    [Fact]
    public async Task GeneratedQueryEntrypointsApplyTenantFilter()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
create table tenant_users (
    id integer not null primary key,
    tenant_id integer not null,
    name text not null
);

insert into tenant_users(id, tenant_id, name) values
(1, 10, 'alice'),
(2, 20, 'bob'),
(3, 10, 'anna');
""");

        var db = new FlyDb(connection, DbDialectKind.Sqlite);
        var context = new FlyDbContext(db);

        using (FlyTenant.Use(10L))
        {
            var rendered = context.TenantUsers
                .OrderBy(Tables.TenantUser.Id.Asc())
                .ToSql();

            Assert.Equal(
                "select \"TenantUser\".* from \"tenant_users\" as \"TenantUser\" where \"TenantUser\".\"tenant_id\" = @p0 order by \"TenantUser\".\"id\" asc",
                rendered.Sql);
            Assert.Equal(10L, rendered.Parameters[0].Value);

            var contextRows = await context.TenantUsers
                .OrderBy(Tables.TenantUser.Id.Asc())
                .ToListAsync();

            Assert.Equal(new[] { "alice", "anna" }, contextRows.Select(static row => row.Name).ToArray());
        }

        using (FlyTenant.Use(20L))
        {
            var repository = new TenantUserRepository(connection);
            var repositoryRows = await repository.Query()
                .OrderBy(Tables.TenantUser.Id.Asc())
                .ToListAsync();

            Assert.Equal(new[] { "bob" }, repositoryRows.Select(static row => row.Name).ToArray());
        }

        var error = Assert.Throws<InvalidOperationException>(() => context.TenantUsers.ToSql());
        Assert.Contains("No FlyAotOrm tenant is active", error.Message);
    }

    [Fact]
    public async Task GeneratedCrudConvertsGuidDateOnlyTimeOnlyAndDateTimeOffset()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
create table typed_rows (
    id text not null primary key,
    business_date text not null,
    start_time text not null,
    changed_at text not null
);
""");

        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var changedAt = new DateTimeOffset(2026, 6, 30, 9, 15, 30, TimeSpan.FromHours(8));
        await TypedRowCrud.InsertAsync(connection, new TypedRow
        {
            Id = id,
            BusinessDate = new DateOnly(2026, 6, 30),
            StartTime = new TimeOnly(9, 15, 30),
            ChangedAt = changedAt
        });

        var row = await TypedRowCrud.GetByIdAsync(connection, id);

        Assert.NotNull(row);
        Assert.Equal(id, row.Id);
        Assert.Equal(new DateOnly(2026, 6, 30), row.BusinessDate);
        Assert.Equal(new TimeOnly(9, 15, 30), row.StartTime);
        Assert.Equal(changedAt, row.ChangedAt);
    }

    [ExternalDatabaseFact("FLYAOTORM_TEST_POSTGRES")]
    public async Task GeneratedCrudExecutesAgainstPostgreSqlWhenLocalServerIsAvailable()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLYAOTORM_TEST_POSTGRES")!;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        try
        {
            await ExecuteAsync(connection, """
drop table if exists generated_users;
create table generated_users (
    id bigint generated by default as identity primary key,
    name text not null,
    created_at timestamp not null
);
""");

            await GeneratedUserCrud.InsertAsync(
                connection,
                new GeneratedUser
                {
                    Name = "alice",
                    CreatedAt = new DateTime(2026, 1, 1)
                },
                DbDialectKind.PostgreSql);

            var user = await GeneratedUserCrud.GetByIdAsync(connection, 1, DbDialectKind.PostgreSql);

            Assert.NotNull(user);
            Assert.Equal("alice", user.Name);

            user.Name = "alice-updated";
            var updated = await GeneratedUserCrud.UpdateAsync(connection, user, DbDialectKind.PostgreSql);
            Assert.Equal(1, updated);

            var returned = await GeneratedUserCrud.InsertReturningAsync(
                connection,
                new GeneratedUser
                {
                    Name = "bob",
                    CreatedAt = new DateTime(2026, 1, 2)
                },
                DbDialectKind.PostgreSql);

            Assert.Equal(2, returned.Id);
            Assert.Equal("bob", returned.Name);

            var all = await GeneratedUserCrud.ListAllAsync(connection, DbDialectKind.PostgreSql);
            Assert.Equal(new[] { "alice-updated", "bob" }, all.Select(static item => item.Name).ToArray());

            Assert.True(await GeneratedUserCrud.ExistsAsync(connection, 1, DbDialectKind.PostgreSql));
            Assert.Equal(2, await GeneratedUserCrud.CountAsync(connection, DbDialectKind.PostgreSql));

            var deleted = await GeneratedUserCrud.DeleteByIdAsync(connection, 1, DbDialectKind.PostgreSql);
            Assert.Equal(1, deleted);
            Assert.False(await GeneratedUserCrud.ExistsAsync(connection, 1, DbDialectKind.PostgreSql));
        }
        finally
        {
            await ExecuteAsync(connection, "drop table if exists generated_users;");
        }
    }

    [ExternalDatabaseFact("FLYAOTORM_TEST_SQLSERVER")]
    public async Task GeneratedCrudExecutesAgainstSqlServerWhenLocalServerIsAvailable()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLYAOTORM_TEST_SQLSERVER")!;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        try
        {
            await ExecuteAsync(connection, """
if object_id('dbo.generated_users', 'U') is not null
    drop table dbo.generated_users;

create table dbo.generated_users (
    id bigint not null identity(1,1) primary key,
    name nvarchar(100) not null,
    created_at datetime2 not null
);
""");

            await GeneratedUserCrud.InsertAsync(
                connection,
                new GeneratedUser { Name = "alice", CreatedAt = new DateTime(2026, 1, 1) },
                DbDialectKind.SqlServer);

            var user = await GeneratedUserCrud.GetByIdAsync(connection, 1, DbDialectKind.SqlServer);
            Assert.NotNull(user);
            Assert.Equal("alice", user.Name);

            user.Name = "alice-updated";
            Assert.Equal(1, await GeneratedUserCrud.UpdateAsync(connection, user, DbDialectKind.SqlServer));

            // OUTPUT INSERTED returns identity key + all columns back in one round-trip.
            var returned = await GeneratedUserCrud.InsertReturningAsync(
                connection,
                new GeneratedUser { Name = "bob", CreatedAt = new DateTime(2026, 1, 2) },
                DbDialectKind.SqlServer);
            Assert.Equal(2, returned.Id);
            Assert.Equal("bob", returned.Name);

            var all = await GeneratedUserCrud.ListAllAsync(connection, DbDialectKind.SqlServer);
            Assert.Equal(new[] { "alice-updated", "bob" }, all.Select(static item => item.Name).ToArray());

            Assert.True(await GeneratedUserCrud.ExistsAsync(connection, 1, DbDialectKind.SqlServer));
            Assert.Equal(2, await GeneratedUserCrud.CountAsync(connection, DbDialectKind.SqlServer));

            Assert.Equal(1, await GeneratedUserCrud.DeleteByIdAsync(connection, 1, DbDialectKind.SqlServer));
            Assert.False(await GeneratedUserCrud.ExistsAsync(connection, 1, DbDialectKind.SqlServer));
        }
        finally
        {
            await ExecuteAsync(connection, "if object_id('dbo.generated_users', 'U') is not null drop table dbo.generated_users;");
        }
    }

    private static async Task ExecuteAsync(System.Data.Common.DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

[DbTable("generated_users")]
public sealed partial class GeneratedUser
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("name")]
    public string Name { get; set; } = "";

    [DbColumn("created_at")]
    public DateTime CreatedAt { get; set; }
}

[DbTable("select")]
public sealed partial class GeneratedKeywordRow
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("from")]
    public string From { get; set; } = "";
}

[DbTable("assigned_key_items")]
public sealed partial class AssignedKeyItem
{
    [DbKey(IsIdentity = false)]
    [DbColumn("id")]
    public string Id { get; set; } = "";

    [DbColumn("name")]
    public string Name { get; set; } = "";
}

[DbTable("typed_rows")]
public sealed partial class TypedRow
{
    [DbKey(IsIdentity = false)]
    [DbColumn("id")]
    public Guid Id { get; set; }

    [DbColumn("business_date")]
    public DateOnly BusinessDate { get; set; }

    [DbColumn("start_time")]
    public TimeOnly StartTime { get; set; }

    [DbColumn("changed_at")]
    public DateTimeOffset ChangedAt { get; set; }
}

[DbTable("soft_delete_users")]
public sealed partial class SoftDeleteUser
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("name")]
    public string Name { get; set; } = "";

    [DbColumn("is_deleted")]
    [DbSoftDelete]
    public bool IsDeleted { get; set; }
}

[DbRepository]
public partial interface ISoftDeleteUserRepository
{
    Query<SoftDeleteUser> Query();

    CountQuery<SoftDeleteUser> CountQuery();

    AnyQuery<SoftDeleteUser> AnyQuery();

    [SqlQuery("select id, name, is_deleted from soft_delete_users order by id")]
    Task<IReadOnlyList<SoftDeleteUser>> ListRawAsync();

    [SqlScalar("select count(1) from soft_delete_users where is_deleted = @isDeleted")]
    Task<long> CountRawAsync(bool isDeleted);
}

[DbDefaultFilter(typeof(FilteredUserDefaults), nameof(FilteredUserDefaults.Active))]
[DbTable("filtered_users")]
public sealed partial class FilteredUser
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("name")]
    public string Name { get; set; } = "";

    [DbColumn("is_active")]
    public bool IsActive { get; set; }
}

public static class FilteredUserDefaults
{
    public static SqlCondition Active() => Tables.FilteredUser.IsActive.Eq(true);
}

[DbRepository]
public partial interface IFilteredUserRepository
{
    Query<FilteredUser> Query();
}

[DbTable("tenant_users")]
public sealed partial class TenantUser
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("tenant_id")]
    [DbTenant]
    public long TenantId { get; set; }

    [DbColumn("name")]
    public string Name { get; set; } = "";
}

[DbRepository]
public partial interface ITenantUserRepository
{
    Query<TenantUser> Query();
}
