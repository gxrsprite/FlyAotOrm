using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace FlyAotOrm.Generator.Tests;

public sealed class QueryDslTests
{
    [Fact]
    public void RendersSqliteSelectWithWhereOrderAndPage()
    {
        var users = new TestUserTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.Sqlite);

        var rendered = db
            .Select(users)
            .Where(users.Id.Eq(1))
            .And(users.Name.Contains("ann"))
            .OrderBy(users.CreatedAt.Desc())
            .Page(2, 10)
            .ToSql();

        Assert.Equal(
            "select \"User\".* from \"users\" as \"User\" where (\"User\".\"id\" = @p0 and \"User\".\"name\" like @p1) order by \"User\".\"created_at\" desc limit @limit2 offset @offset3",
            rendered.Sql);
        Assert.Equal(4, rendered.Parameters.Count);
        Assert.Equal(1L, rendered.Parameters[0].Value);
        Assert.Equal("%ann%", rendered.Parameters[1].Value);
        Assert.Equal(10, rendered.Parameters[2].Value);
        Assert.Equal(10, rendered.Parameters[3].Value);
        Assert.True(rendered.TryGetParameter("p0", out var idParameter));
        Assert.Equal(1L, idParameter.Value);
        Assert.Equal("%ann%", rendered.GetParameterValue("@p1"));
        Assert.Equal(10, rendered.GetParameterValue("limit2"));
        Assert.Equal(10, rendered.GetParameterValue("@offset3"));

        var parameters = rendered.ToParameterDictionary();
        Assert.Equal(4, parameters.Count);
        Assert.Equal(1L, parameters["p0"]);
        Assert.Throws<KeyNotFoundException>(() => rendered.GetParameterValue("missing"));
    }

    [Fact]
    public void CustomRendererCanFallbackToCountForAnySql()
    {
        var users = new TestUserTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), new CountOnlyRenderer());

        var rendered = db
            .Select(users)
            .Where(users.Id.Eq(1))
            .ToAnySql();

        Assert.Equal("count fallback", rendered.Sql);
        Assert.Single(rendered.Parameters);
        Assert.Equal(1L, rendered.Parameters[0].Value);
    }

    [Fact]
    public void FlyTenantUseScopesCurrentTenant()
    {
        FlyTenant.Clear();

        Assert.Throws<InvalidOperationException>(() => FlyTenant.Get<long>());

        using (FlyTenant.Use(10L))
        {
            Assert.Equal(10L, FlyTenant.Get<long>());

            using (FlyTenant.Use(20L))
            {
                Assert.Equal(20L, FlyTenant.Get<long>());
            }

            Assert.Equal(10L, FlyTenant.Get<long>());
        }

        Assert.Throws<InvalidOperationException>(() => FlyTenant.Get<long>());
    }

    [Fact]
    public void FlyTenantSetClearAndConvertCurrentTenant()
    {
        FlyTenant.Clear();

        FlyTenant.Set("42");
        Assert.Equal(42, FlyTenant.Get<int>());

        FlyTenant.Clear();
        Assert.Throws<InvalidOperationException>(() => FlyTenant.Get<int>());
    }

    [Fact]
    public void RendersSqliteSelectWithTakeAndSkip()
    {
        var users = new TestUserTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.Sqlite);

        var rendered = db
            .Select(users)
            .OrderBy(users.Id.Asc())
            .Skip(5)
            .Take(10)
            .ToSql();

        Assert.Equal(
            "select \"User\".* from \"users\" as \"User\" order by \"User\".\"id\" asc limit @limit0 offset @offset1",
            rendered.Sql);
        Assert.Equal(10, rendered.Parameters[0].Value);
        Assert.Equal(5, rendered.Parameters[1].Value);
    }

    [Fact]
    public void RendersSqliteSelectWithCustomParameterNaming()
    {
        var users = new TestUserTable();
        var options = new SqlRendererOptions(
            valueParameterNamePrefix: "arg",
            limitParameterNamePrefix: "take",
            offsetParameterNamePrefix: "skip");
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.Sqlite, options);

        var rendered = db
            .Select(users)
            .Where(users.Name.StartsWith("a"))
            .OrderBy(users.Id.Asc())
            .Page(2, 10)
            .ToSql();

        Assert.Equal(
            "select \"User\".* from \"users\" as \"User\" where \"User\".\"name\" like @arg0 order by \"User\".\"id\" asc limit @take1 offset @skip2",
            rendered.Sql);
        Assert.Equal("a%", rendered.GetParameterValue("arg0"));
        Assert.Equal(10, rendered.GetParameterValue("take1"));
        Assert.Equal(10, rendered.GetParameterValue("skip2"));
    }

    [Fact]
    public void RepeatedWhereCombinesWithAnd()
    {
        var users = new TestUserTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.Sqlite);

        var rendered = db
            .Select(users)
            .Where(users.Id.Eq(1))
            .Where(users.Name.Eq("alice"))
            .ToSql();

        Assert.Equal(
            "select \"User\".* from \"users\" as \"User\" where (\"User\".\"id\" = @p0 and \"User\".\"name\" = @p1)",
            rendered.Sql);
        Assert.Equal(1L, rendered.Parameters[0].Value);
        Assert.Equal("alice", rendered.Parameters[1].Value);
    }

    [Fact]
    public void RepeatedProjectionWhereCombinesWithAnd()
    {
        var users = new TestUserTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.Sqlite);

        var rendered = db
            .Select<UserNameRow>()
            .From(users)
            .Columns(users.Name.As(nameof(UserNameRow.Name)))
            .Where(users.Id.Eq(1))
            .Where(users.Name.Eq("alice"))
            .ToSql();

        Assert.Equal(
            "select \"User\".\"name\" as \"Name\" from \"users\" as \"User\" where (\"User\".\"id\" = @p0 and \"User\".\"name\" = @p1)",
            rendered.Sql);
        Assert.Equal(1L, rendered.Parameters[0].Value);
        Assert.Equal("alice", rendered.Parameters[1].Value);
    }

    [Fact]
    public void RepeatedScalarWhereCombinesWithAnd()
    {
        var users = new TestUserTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.Sqlite);

        var countSql = db
            .SelectCount(users)
            .Where(users.Id.Eq(1))
            .Where(users.Name.Eq("alice"))
            .ToSql();

        var anySql = db
            .SelectAny(users)
            .Where(users.Id.Eq(1))
            .Where(users.Name.Eq("alice"))
            .ToSql();

        Assert.Equal(
            "select count(1) from \"users\" as \"User\" where (\"User\".\"id\" = @p0 and \"User\".\"name\" = @p1)",
            countSql.Sql);
        Assert.Equal(
            "select case when exists (select 1 from \"users\" as \"User\" where (\"User\".\"id\" = @p0 and \"User\".\"name\" = @p1)) then 1 else 0 end",
            anySql.Sql);
    }

    [Fact]
    public void RendersSelectCountWithWhereJoinAndInlineParameters()
    {
        var users = new TestUserTable();
        var orders = new TestOrderTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.Sqlite);

        var rendered = db
            .SelectCount(users)
            .Join(orders)
            .On(users.Id.Eq(orders.UserId))
            .Where(users.Name.StartsWith("a"))
            .And(orders.Status.Eq(1))
            .InlineParameters()
            .ToSql();

        Assert.Equal(
            "select count(1) from \"users\" as \"User\" join \"orders\" as \"Order\" on \"User\".\"id\" = \"Order\".\"user_id\" where (\"User\".\"name\" like 'a%' and \"Order\".\"status\" = 1)",
            rendered.Sql);
        Assert.Empty(rendered.Parameters);
    }

    [Fact]
    public void RendersSelectAnyWithWhereJoinAndInlineParameters()
    {
        var users = new TestUserTable();
        var orders = new TestOrderTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.Sqlite);

        var rendered = db
            .SelectAny(users)
            .LeftJoin(orders)
            .On(users.Id.Eq(orders.UserId))
            .Where(users.Name.StartsWith("a"))
            .And(orders.Status.Eq(1))
            .InlineParameters()
            .ToSql();

        Assert.Equal(
            "select case when exists (select 1 from \"users\" as \"User\" left join \"orders\" as \"Order\" on \"User\".\"id\" = \"Order\".\"user_id\" where (\"User\".\"name\" like 'a%' and \"Order\".\"status\" = 1)) then 1 else 0 end",
            rendered.Sql);
        Assert.Empty(rendered.Parameters);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1p")]
    [InlineData("has-dash")]
    public void RejectsInvalidParameterNamePrefixes(string prefix)
    {
        Assert.Throws<ArgumentException>(() => new SqlRendererOptions(valueParameterNamePrefix: prefix));
    }

    [Fact]
    public void RendersSqliteSelectWithInlineParameters()
    {
        var users = new TestUserTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.Sqlite);

        var rendered = db
            .Select(users)
            .Where(users.Name.Eq("a'lice"))
            .And(users.Id.In(1, 2, 3))
            .OrderBy(users.Id.Asc())
            .Skip(5)
            .Take(10)
            .InlineParameters()
            .ToSql();

        Assert.Equal(
            "select \"User\".* from \"users\" as \"User\" where (\"User\".\"name\" = 'a''lice' and \"User\".\"id\" in (1, 2, 3)) order by \"User\".\"id\" asc limit 10 offset 5",
            rendered.Sql);
        Assert.Empty(rendered.Parameters);
    }

    [Fact]
    public void RendersNullEqualityAsIsNull()
    {
        var users = new TestUserTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.Sqlite);

        var rendered = db
            .Select(users)
            .Where(users.Email.EqNull())
            .And(users.Email.NotEqNull())
            .InlineParameters()
            .ToSql();

        Assert.Equal(
            "select \"User\".* from \"users\" as \"User\" where (\"User\".\"email\" is null and \"User\".\"email\" is not null)",
            rendered.Sql);
        Assert.Empty(rendered.Parameters);
    }

    [Fact]
    public void RendersEntityQueryWithConditionalPredicates()
    {
        var users = new TestUserTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.Sqlite);

        var rendered = db
            .Select(users)
            .WhereIf(false, users.Id.Eq(0))
            .AndIf(true, users.Name.StartsWith("a"))
            .OrIf(false, users.Email.Eq("ignored"))
            .AndIf(true, users.Id.GreaterThan(10))
            .ToSql();

        Assert.Equal(
            "select \"User\".* from \"users\" as \"User\" where (\"User\".\"name\" like @p0 and \"User\".\"id\" > @p1)",
            rendered.Sql);
        Assert.Equal("a%", rendered.Parameters[0].Value);
        Assert.Equal(10L, rendered.Parameters[1].Value);
    }

    [Fact]
    public void RendersProjectionQueryWithConditionalPredicates()
    {
        var users = new TestUserTable();
        var orders = new TestOrderTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.Sqlite);

        var rendered = db
            .Select<UserOrderRow>()
            .From(users)
            .Join(orders)
            .On(users.Id.Eq(orders.UserId))
            .Columns(
                users.Name.As(nameof(UserOrderRow.UserName)),
                orders.Id.As(nameof(UserOrderRow.OrderId)))
            .WhereIf(true, users.Name.Eq("alice"))
            .OrIf(false, orders.Status.Eq(9))
            .AndIf(true, orders.Status.Eq(1))
            .ToSql();

        Assert.Equal(
            "select \"User\".\"name\" as \"UserName\", \"Order\".\"id\" as \"OrderId\" from \"users\" as \"User\" join \"orders\" as \"Order\" on \"User\".\"id\" = \"Order\".\"user_id\" where (\"User\".\"name\" = @p0 and \"Order\".\"status\" = @p1)",
            rendered.Sql);
        Assert.Equal("alice", rendered.Parameters[0].Value);
        Assert.Equal(1, rendered.Parameters[1].Value);
    }

    [Fact]
    public void RendersSqlServerSelectWithOrderedTakeAndSkip()
    {
        var users = new TestUserTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.SqlServer);

        var rendered = db
            .Select(users)
            .OrderBy(users.Id.Asc())
            .Skip(5)
            .Take(10)
            .ToSql();

        Assert.Equal(
            "select [User].* from [users] as [User] order by [User].[id] asc offset @offset0 rows fetch next @limit1 rows only",
            rendered.Sql);
        Assert.Equal(5, rendered.Parameters[0].Value);
        Assert.Equal(10, rendered.Parameters[1].Value);
    }

    [Fact]
    public void FlyDbCanCreateRendererFromDialectKind()
    {
        var users = new TestUserTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.MySql);

        var rendered = db
            .Select(users)
            .Where(users.Id.Eq(7))
            .ToSql();

        Assert.Equal("select `User`.* from `users` as `User` where `User`.`id` = @p0", rendered.Sql);
        Assert.Equal(7L, rendered.Parameters[0].Value);
    }

    [Fact]
    public void NovaDbRendererUsesBacktickAndLimitOffset()
    {
        var users = new TestUserTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.NovaDb);

        Assert.IsType<NovaDbRenderer>(db.Renderer);

        var rendered = db
            .Select(users)
            .Where(users.Id.Eq(7))
            .OrderBy(users.Id.Asc())
            .Page(2, 10)
            .ToSql();

        // NovaDb: bare * (no `a`.*), LIMIT/OFFSET always inline literals (parameters rejected).
        Assert.Equal(
            "select * from `users` as `User` where `User`.`id` = @p0 order by `User`.`id` asc limit 10 offset 10",
            rendered.Sql);
        Assert.Single(rendered.Parameters);
        Assert.Equal(7L, rendered.Parameters[0].Value);
    }

    [Fact]
    public void NovaDbRendererInlinesBooleansAsKeywords()
    {
        var users = new TestFlagTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.NovaDb);

        var rendered = db
            .Select(users)
            .Where(users.Flag.Eq(true))
            .InlineParameters()
            .ToSql();

        // NovaDb never coerces bool = 1 (always false): inline booleans must be TRUE/FALSE.
        Assert.Equal("select * from `users` as `User` where `User`.`flag` = TRUE", rendered.Sql);
        Assert.Empty(rendered.Parameters);
    }

    [Fact]
    public void RendersColumnToColumnComparisonOperators()
    {
        var orders = new TestOrderTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.Sqlite);

        var rendered = db
            .Select(orders)
            .Where(orders.Id.GreaterThan(orders.UserId))
            .And(orders.Id.GreaterOrEqual(orders.UserId))
            .And(orders.Id.LessThan(orders.UserId))
            .And(orders.Id.LessOrEqual(orders.UserId))
            .ToSql();

        Assert.Equal(
            "select \"Order\".* from \"orders\" as \"Order\" where (((\"Order\".\"id\" > \"Order\".\"user_id\" and \"Order\".\"id\" >= \"Order\".\"user_id\") and \"Order\".\"id\" < \"Order\".\"user_id\") and \"Order\".\"id\" <= \"Order\".\"user_id\")",
            rendered.Sql);
        Assert.Empty(rendered.Parameters);
    }

    [Fact]
    public void RendersProjectionQueryWithNoneParamterAlias()
    {
        var users = new TestUserTable();
        var orders = new TestOrderTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.Sqlite);

        var rendered = db
            .Select<UserOrderRow>()
            .From(users)
            .Join(orders)
            .On(users.Id.Eq(orders.UserId))
            .Columns(
                users.Name.As(nameof(UserOrderRow.UserName)),
                orders.Id.As(nameof(UserOrderRow.OrderId)))
            .Where(orders.Amount.GreaterOrEqual(12.5m))
            .NoneParamter()
            .ToSql();

        Assert.Equal(
            "select \"User\".\"name\" as \"UserName\", \"Order\".\"id\" as \"OrderId\" from \"users\" as \"User\" join \"orders\" as \"Order\" on \"User\".\"id\" = \"Order\".\"user_id\" where \"Order\".\"amount\" >= 12.5",
            rendered.Sql);
        Assert.Empty(rendered.Parameters);
    }

    public static TheoryData<DbDialectKind, string> DialectSelectCases => new()
    {
        {
            DbDialectKind.PostgreSql,
            "select \"User\".* from \"users\" as \"User\" join \"orders\" as \"Order\" on \"User\".\"id\" = \"Order\".\"user_id\" where (\"User\".\"name\" like @p0 and \"Order\".\"amount\" between @p1 and @p2) order by \"Order\".\"id\" desc limit @limit3 offset @offset4"
        },
        {
            DbDialectKind.Sqlite,
            "select \"User\".* from \"users\" as \"User\" join \"orders\" as \"Order\" on \"User\".\"id\" = \"Order\".\"user_id\" where (\"User\".\"name\" like @p0 and \"Order\".\"amount\" between @p1 and @p2) order by \"Order\".\"id\" desc limit @limit3 offset @offset4"
        },
        {
            DbDialectKind.MySql,
            "select `User`.* from `users` as `User` join `orders` as `Order` on `User`.`id` = `Order`.`user_id` where (`User`.`name` like @p0 and `Order`.`amount` between @p1 and @p2) order by `Order`.`id` desc limit @limit3 offset @offset4"
        },
        {
            DbDialectKind.NovaDb,
            "select * from `users` as `User` join `orders` as `Order` on `User`.`id` = `Order`.`user_id` where (`User`.`name` like @p0 and (`Order`.`amount` >= @p1 and `Order`.`amount` <= @p2)) order by `Order`.`id` desc limit 25 offset 25"
        },
        {
            DbDialectKind.SqlServer,
            "select [User].* from [users] as [User] join [orders] as [Order] on [User].[id] = [Order].[user_id] where ([User].[name] like @p0 and [Order].[amount] between @p1 and @p2) order by [Order].[id] desc offset @offset3 rows fetch next @limit4 rows only"
        }
    };

    [Theory]
    [MemberData(nameof(DialectSelectCases))]
    public void RendersSameSelectAstAcrossDialects(DbDialectKind dialect, string expectedSql)
    {
        var users = new TestUserTable();
        var orders = new TestOrderTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), dialect);

        var rendered = db
            .Select(users)
            .Join(orders)
            .On(users.Id.Eq(orders.UserId))
            .Where(users.Name.StartsWith("a"))
            .And(orders.Amount.Between(10m, 20m))
            .OrderBy(orders.Id.Desc())
            .Page(2, 25)
            .ToSql();

        Assert.Equal(expectedSql, rendered.Sql);
        Assert.Equal("a%", rendered.Parameters[0].Value);
        Assert.Equal(10m, rendered.Parameters[1].Value);
        Assert.Equal(20m, rendered.Parameters[2].Value);
        if (dialect == DbDialectKind.NovaDb)
        {
            // NovaDb 内联 LIMIT/OFFSET（参数写法会被解析器拒绝），只有 3 个参数。
            Assert.Equal(3, rendered.Parameters.Count);
        }
        else if (dialect == DbDialectKind.SqlServer)
        {
            Assert.Equal(25, rendered.Parameters[3].Value);
            Assert.Equal(25, rendered.Parameters[4].Value);
        }
        else
        {
            Assert.Equal(25, rendered.Parameters[3].Value);
            Assert.Equal(25, rendered.Parameters[4].Value);
        }
    }

    public static TheoryData<DbDialectKind, string> DialectFirstCases => new()
    {
        {
            DbDialectKind.PostgreSql,
            "select \"User\".* from \"users\" as \"User\" where \"User\".\"name\" like @p0 order by \"User\".\"id\" desc limit @limit1"
        },
        {
            DbDialectKind.Sqlite,
            "select \"User\".* from \"users\" as \"User\" where \"User\".\"name\" like @p0 order by \"User\".\"id\" desc limit @limit1"
        },
        {
            DbDialectKind.MySql,
            "select `User`.* from `users` as `User` where `User`.`name` like @p0 order by `User`.`id` desc limit @limit1"
        },
        {
            DbDialectKind.NovaDb,
            "select * from `users` as `User` where `User`.`name` like @p0 order by `User`.`id` desc limit 1"
        },
        {
            DbDialectKind.SqlServer,
            "select [User].* from [users] as [User] where [User].[name] like @p0 order by [User].[id] desc offset 0 rows fetch next @limit1 rows only"
        }
    };

    [Theory]
    [MemberData(nameof(DialectFirstCases))]
    public void RendersSameFirstAstAcrossDialects(DbDialectKind dialect, string expectedSql)
    {
        var users = new TestUserTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), dialect);

        var rendered = db
            .Select(users)
            .Where(users.Name.StartsWith("a"))
            .OrderBy(users.Id.Desc())
            .ToFirstSql();

        Assert.Equal(expectedSql, rendered.Sql);
        Assert.Equal("a%", rendered.Parameters[0].Value);
        if (dialect == DbDialectKind.NovaDb)
        {
            // NovaDb 内联 LIMIT（参数写法会被解析器拒绝）。
            Assert.Single(rendered.Parameters);
        }
        else
        {
            Assert.Equal(1, rendered.Parameters[1].Value);
        }
    }

    public static TheoryData<DbDialectKind, string> DialectCountCases => new()
    {
        {
            DbDialectKind.PostgreSql,
            "select count(1) from \"users\" as \"User\" left join \"orders\" as \"Order\" on \"User\".\"id\" = \"Order\".\"user_id\" where \"Order\".\"status\" = @p0"
        },
        {
            DbDialectKind.Sqlite,
            "select count(1) from \"users\" as \"User\" left join \"orders\" as \"Order\" on \"User\".\"id\" = \"Order\".\"user_id\" where \"Order\".\"status\" = @p0"
        },
        {
            DbDialectKind.MySql,
            "select count(1) from `users` as `User` left join `orders` as `Order` on `User`.`id` = `Order`.`user_id` where `Order`.`status` = @p0"
        },
        {
            DbDialectKind.NovaDb,
            "select count(1) from `users` as `User` left join `orders` as `Order` on `User`.`id` = `Order`.`user_id` where `Order`.`status` = @p0"
        },
        {
            DbDialectKind.SqlServer,
            "select count(1) from [users] as [User] left join [orders] as [Order] on [User].[id] = [Order].[user_id] where [Order].[status] = @p0"
        }
    };

    [Theory]
    [MemberData(nameof(DialectCountCases))]
    public void RendersSameCountAstAcrossDialects(DbDialectKind dialect, string expectedSql)
    {
        var users = new TestUserTable();
        var orders = new TestOrderTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), dialect);

        var countRendered = db
            .Select(users)
            .LeftJoin(orders)
            .On(users.Id.Eq(orders.UserId))
            .Where(orders.Status.Eq(1))
            .OrderBy(orders.Id.Desc())
            .Page(2, 25)
            .ToCountSql();

        Assert.Equal(expectedSql, countRendered.Sql);
        Assert.Single(countRendered.Parameters);
        Assert.Equal(1, countRendered.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(DialectCountCases))]
    public void RendersSameSelectCountAstAcrossDialects(DbDialectKind dialect, string expectedSql)
    {
        var users = new TestUserTable();
        var orders = new TestOrderTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), dialect);

        var countRendered = db
            .SelectCount(users)
            .LeftJoin(orders)
            .On(users.Id.Eq(orders.UserId))
            .Where(orders.Status.Eq(1))
            .ToSql();

        Assert.Equal(expectedSql, countRendered.Sql);
        Assert.Single(countRendered.Parameters);
        Assert.Equal(1, countRendered.Parameters[0].Value);
    }

    public static TheoryData<DbDialectKind, string> DialectAnyCases => new()
    {
        {
            DbDialectKind.PostgreSql,
            "select case when exists (select 1 from \"users\" as \"User\" left join \"orders\" as \"Order\" on \"User\".\"id\" = \"Order\".\"user_id\" where \"Order\".\"status\" = @p0) then 1 else 0 end"
        },
        {
            DbDialectKind.Sqlite,
            "select case when exists (select 1 from \"users\" as \"User\" left join \"orders\" as \"Order\" on \"User\".\"id\" = \"Order\".\"user_id\" where \"Order\".\"status\" = @p0) then 1 else 0 end"
        },
        {
            DbDialectKind.MySql,
            "select case when exists (select 1 from `users` as `User` left join `orders` as `Order` on `User`.`id` = `Order`.`user_id` where `Order`.`status` = @p0) then 1 else 0 end"
        },
        {
            DbDialectKind.NovaDb,
            "select case when 1 in (select 1 from `users` as `User` left join `orders` as `Order` on `User`.`id` = `Order`.`user_id` where `Order`.`status` = @p0) then 1 else 0 end"
        },
        {
            DbDialectKind.SqlServer,
            "select case when exists (select 1 from [users] as [User] left join [orders] as [Order] on [User].[id] = [Order].[user_id] where [Order].[status] = @p0) then 1 else 0 end"
        }
    };

    [Theory]
    [MemberData(nameof(DialectAnyCases))]
    public void RendersSameAnyAstAcrossDialects(DbDialectKind dialect, string expectedSql)
    {
        var users = new TestUserTable();
        var orders = new TestOrderTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), dialect);

        var anyRendered = db
            .Select(users)
            .LeftJoin(orders)
            .On(users.Id.Eq(orders.UserId))
            .Where(orders.Status.Eq(1))
            .OrderBy(orders.Id.Desc())
            .Page(2, 25)
            .ToAnySql();

        Assert.Equal(expectedSql, anyRendered.Sql);
        Assert.Single(anyRendered.Parameters);
        Assert.Equal(1, anyRendered.Parameters[0].Value);
    }

    [Theory]
    [MemberData(nameof(DialectAnyCases))]
    public void RendersSameSelectAnyAstAcrossDialects(DbDialectKind dialect, string expectedSql)
    {
        var users = new TestUserTable();
        var orders = new TestOrderTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), dialect);

        var anyRendered = db
            .SelectAny(users)
            .LeftJoin(orders)
            .On(users.Id.Eq(orders.UserId))
            .Where(orders.Status.Eq(1))
            .ToSql();

        Assert.Equal(expectedSql, anyRendered.Sql);
        Assert.Single(anyRendered.Parameters);
        Assert.Equal(1, anyRendered.Parameters[0].Value);
    }

    [Fact]
    public void SqlServerTakeRequiresOrderBy()
    {
        var users = new TestUserTable();
        var db = new FlyDb(new SqliteConnection("Data Source=:memory:"), DbDialectKind.SqlServer);

        var exception = Assert.Throws<InvalidOperationException>(() => db
            .Select(users)
            .Take(10)
            .ToSql());

        Assert.Equal("SQL Server paging requires at least one OrderBy column.", exception.Message);
    }

    [Fact]
    public async Task ExecutesSqliteQueryDsl()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
create table users (
    id integer not null primary key,
    name text not null,
    created_at text not null
);

insert into users(id, name, created_at) values
(1, 'alice', '2026-01-01'),
(2, 'bob', '2026-01-02'),
(3, 'anna', '2026-01-03');
""");

        var users = new TestUserTable();
        var db = new FlyDb(connection, DbDialectKind.Sqlite);

        var rows = await db
            .Select(users)
            .Where(users.Name.StartsWith("a"))
            .OrderBy(users.Id.Desc())
            .ToListAsync();

        Assert.Equal(new long[] { 3, 1 }, rows.Select(static row => row.Id).ToArray());

        var first = await db
            .Select(users)
            .OrderBy(users.Id.Desc())
            .FirstOrDefaultAsync();

        Assert.NotNull(first);
        Assert.Equal(3, first.Id);

        var count = await db
            .Select(users)
            .Where(users.Name.StartsWith("a"))
            .CountAsync();

        Assert.Equal(2, count);

        var explicitCount = await db
            .SelectCount(users)
            .Where(users.Name.StartsWith("a"))
            .CountAsync();

        Assert.Equal(2, explicitCount);

        var any = await db
            .Select(users)
            .Where(users.Name.Eq("missing"))
            .AnyAsync();

        Assert.False(any);

        var explicitAny = await db
            .SelectAny(users)
            .Where(users.Name.Eq("alice"))
            .AnyAsync();

        Assert.True(explicitAny);

        var inlineRows = await db
            .Select(users)
            .Where(users.Name.Eq("alice"))
            .InlineParameters()
            .ToListAsync();

        Assert.Single(inlineRows);
        Assert.Equal(1, inlineRows[0].Id);

        var inlineFirst = await db
            .Select(users)
            .Where(users.Name.Eq("a'lice"))
            .InlineParameters()
            .FirstOrDefaultAsync();

        Assert.Null(inlineFirst);

        var inlineCount = await db
            .Select(users)
            .Where(users.Name.StartsWith("a"))
            .InlineParameters()
            .CountAsync();

        Assert.Equal(2, inlineCount);

        var inlineAny = await db
            .Select(users)
            .Where(users.Name.Eq("alice"))
            .InlineParameters()
            .AnyAsync();

        Assert.True(inlineAny);
    }

    [Fact]
    public async Task ExecutesSqliteQueryDslWithConvertedScalarParameters()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var businessDate = new DateOnly(2026, 6, 30);
        var startTime = new TimeOnly(9, 15, 30);
        var changedAt = new DateTimeOffset(2026, 6, 30, 9, 15, 30, TimeSpan.FromHours(8));
        var idText = FlyDbValueConverter.ToDatabase(id);
        var businessDateText = FlyDbValueConverter.ToDatabase(businessDate);
        var startTimeText = FlyDbValueConverter.ToDatabase(startTime);
        var changedAtText = FlyDbValueConverter.ToDatabase(changedAt);

        await ExecuteAsync(connection, $$"""
create table typed_query_rows (
    id text not null primary key,
    business_date text not null,
    start_time text not null,
    changed_at text not null
);

insert into typed_query_rows(id, business_date, start_time, changed_at) values
('{{idText}}', '{{businessDateText}}', '{{startTimeText}}', '{{changedAtText}}');
""");

        var rows = new TestTypedQueryRowTable();
        var db = new FlyDb(connection, DbDialectKind.Sqlite);

        var result = await db
            .Select(rows)
            .Where(rows.Id.Eq(id))
            .And(rows.BusinessDate.Eq(businessDate))
            .And(rows.StartTime.Eq(startTime))
            .And(rows.ChangedAt.Eq(changedAt))
            .ToListAsync();

        Assert.Single(result);
        Assert.Equal(id, result[0].Id);
        Assert.Equal(businessDate, result[0].BusinessDate);
        Assert.Equal(startTime, result[0].StartTime);
        Assert.Equal(changedAt, result[0].ChangedAt);
    }

    [Fact]
    public async Task ExecutesSqliteJoinProjectionQueryDsl()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
create table users (
    id integer not null primary key,
    name text not null,
    created_at text not null
);

create table orders (
    id integer not null primary key,
    user_id integer not null,
    amount real not null,
    status integer not null
);

insert into users(id, name, created_at) values
(1, 'alice', '2026-01-01'),
(2, 'bob', '2026-01-02');

insert into orders(id, user_id, amount, status) values
(10, 1, 12.5, 1),
(11, 1, 8.25, 0),
(12, 2, 20.0, 1);
""");

        var users = new TestUserTable();
        var orders = new TestOrderTable();
        var db = new FlyDb(connection, DbDialectKind.Sqlite);

        var rows = await db
            .Select<UserOrderRow>()
            .From(users)
            .Join(orders)
            .On(users.Id.Eq(orders.UserId))
            .Columns(
                users.Id.As(nameof(UserOrderRow.UserId)),
                users.Name.As(nameof(UserOrderRow.UserName)),
                orders.Id.As(nameof(UserOrderRow.OrderId)),
                orders.Amount.As(nameof(UserOrderRow.Amount)))
            .Where(orders.Status.Eq(1))
            .OrderBy(orders.Id.Asc())
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal("alice", rows[0].UserName);
        Assert.Equal(10, rows[0].OrderId);
        Assert.Equal("bob", rows[1].UserName);
        Assert.Equal(12, rows[1].OrderId);

        var first = await db
            .Select<UserOrderRow>()
            .From(users)
            .Join(orders)
            .On(users.Id.Eq(orders.UserId))
            .Columns(
                users.Id.As(nameof(UserOrderRow.UserId)),
                users.Name.As(nameof(UserOrderRow.UserName)),
                orders.Id.As(nameof(UserOrderRow.OrderId)),
                orders.Amount.As(nameof(UserOrderRow.Amount)))
            .Where(orders.Status.Eq(1))
            .OrderBy(orders.Id.Asc())
            .FirstOrDefaultAsync(CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(10, first.OrderId);

        var count = await db
            .Select<UserOrderRow>()
            .From(users)
            .Join(orders)
            .On(users.Id.Eq(orders.UserId))
            .Where(orders.Status.Eq(1))
            .CountAsync();

        Assert.Equal(2, count);

        var any = await db
            .Select<UserOrderRow>()
            .From(users)
            .Join(orders)
            .On(users.Id.Eq(orders.UserId))
            .Where(orders.Status.Eq(2))
            .AnyAsync();

        Assert.False(any);
    }

    [Fact]
    public async Task ExecutesSqliteLeftJoinProjectionQueryDsl()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
create table users (
    id integer not null primary key,
    name text not null,
    created_at text not null
);

create table orders (
    id integer not null primary key,
    user_id integer not null,
    amount real not null,
    status integer not null
);

insert into users(id, name, created_at) values
(1, 'alice', '2026-01-01'),
(2, 'bob', '2026-01-02'),
(3, 'carol', '2026-01-03');

insert into orders(id, user_id, amount, status) values
(10, 1, 12.5, 1),
(12, 2, 20.0, 1);
""");

        var users = new TestUserTable();
        var orders = new TestOrderTable();
        var db = new FlyDb(connection, DbDialectKind.Sqlite);

        var rows = await db
            .Select<UserOrderRow>()
            .From(users)
            .LeftJoin(orders)
            .On(users.Id.Eq(orders.UserId))
            .Columns(
                users.Id.As(nameof(UserOrderRow.UserId)),
                users.Name.As(nameof(UserOrderRow.UserName)),
                orders.Id.As(nameof(UserOrderRow.OrderId)),
                orders.Amount.As(nameof(UserOrderRow.Amount)))
            .OrderBy(users.Id.Asc())
            .ToListAsync();

        Assert.Equal(3, rows.Count);
        Assert.Equal("carol", rows[2].UserName);
        Assert.Equal(0, rows[2].OrderId);
    }

    [Fact]
    public async Task ExecutesSqliteChainedJoinProjectionQueryDsl()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
create table users (
    id integer not null primary key,
    name text not null,
    created_at text not null
);

create table orders (
    id integer not null primary key,
    user_id integer not null,
    amount real not null,
    status integer not null
);

create table payments (
    id integer not null primary key,
    order_id integer not null,
    provider text not null
);

insert into users(id, name, created_at) values
(1, 'alice', '2026-01-01'),
(2, 'bob', '2026-01-02');

insert into orders(id, user_id, amount, status) values
(10, 1, 12.5, 1),
(12, 2, 20.0, 1);

insert into payments(id, order_id, provider) values
(100, 10, 'card'),
(101, 12, 'cash');
""");

        var users = new TestUserTable();
        var orders = new TestOrderTable();
        var payments = new TestPaymentTable();
        var db = new FlyDb(connection, DbDialectKind.Sqlite);

        var query = db
            .Select<UserOrderPaymentRow>()
            .From(users)
            .Join(orders)
            .On(users.Id.Eq(orders.UserId))
            .LeftJoin(payments)
            .On(orders.Id.Eq(payments.OrderId))
            .Columns(
                users.Name.As(nameof(UserOrderPaymentRow.UserName)),
                orders.Id.As(nameof(UserOrderPaymentRow.OrderId)),
                payments.Provider.As(nameof(UserOrderPaymentRow.PaymentProvider)))
            .Where(payments.Provider.Eq("card"))
            .OrderBy(orders.Id.Asc());

        var rendered = query.ToSql();
        Assert.Equal(
            "select \"User\".\"name\" as \"UserName\", \"Order\".\"id\" as \"OrderId\", \"Payment\".\"provider\" as \"PaymentProvider\" from \"users\" as \"User\" join \"orders\" as \"Order\" on \"User\".\"id\" = \"Order\".\"user_id\" left join \"payments\" as \"Payment\" on \"Order\".\"id\" = \"Payment\".\"order_id\" where \"Payment\".\"provider\" = @p0 order by \"Order\".\"id\" asc",
            rendered.Sql);

        var rows = await query.ToListAsync();

        Assert.Single(rows);
        Assert.Equal("alice", rows[0].UserName);
        Assert.Equal(10, rows[0].OrderId);
        Assert.Equal("card", rows[0].PaymentProvider);
    }

    [ExternalDatabaseFact("FLYAOTORM_TEST_POSTGRES")]
    public async Task ExecutesPostgreSqlQueryDslWhenLocalServerIsAvailable()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLYAOTORM_TEST_POSTGRES")!;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, """
drop table if exists users;
create table users (
    id bigint not null primary key,
    name text not null,
    created_at timestamp not null
);

insert into users(id, name, created_at) values
(1, 'alice', timestamp '2026-01-01'),
(2, 'bob', timestamp '2026-01-02'),
(3, 'anna', timestamp '2026-01-03');
""");

        var users = new TestUserTable();
        var db = new FlyDb(connection, DbDialectKind.PostgreSql);

        var rows = await db
            .Select(users)
            .Where(users.Name.StartsWith("a"))
            .OrderBy(users.Id.Desc())
            .ToListAsync();

        Assert.Equal(new long[] { 3, 1 }, rows.Select(static row => row.Id).ToArray());
    }

    [ExternalDatabaseFact("FLYAOTORM_TEST_SQLSERVER")]
    public async Task ExecutesSqlServerQueryDslWhenLocalServerIsAvailable()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLYAOTORM_TEST_SQLSERVER")!;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        try
        {
            await ExecuteAsync(connection, """
if object_id('dbo.flyaotorm_users', 'U') is not null
    drop table dbo.flyaotorm_users;

create table dbo.flyaotorm_users (
    id bigint not null primary key,
    name nvarchar(100) not null,
    created_at datetime2 not null
);

insert into dbo.flyaotorm_users(id, name, created_at) values
(1, N'alice', convert(datetime2, '2026-01-01T00:00:00')),
(2, N'bob', convert(datetime2, '2026-01-02T00:00:00')),
(3, N'anna', convert(datetime2, '2026-01-03T00:00:00'));
""");

            var users = new TestSqlServerUserTable();
            var db = new FlyDb(connection, DbDialectKind.SqlServer);

            var rows = await db
                .Select(users)
                .Where(users.Name.StartsWith("a"))
                .OrderBy(users.Id.Desc())
                .Take(2)
                .ToListAsync();

            Assert.Equal(new long[] { 3, 1 }, rows.Select(static row => row.Id).ToArray());
        }
        finally
        {
            await ExecuteAsync(connection, "if object_id('dbo.flyaotorm_users', 'U') is not null drop table dbo.flyaotorm_users;");
        }
    }

    private sealed class TestUserTable : DbTable<TestUser>
    {
        public TestUserTable() : base("users", "User", Materialize)
        {
            Id = new DbColumn<long>(this, "id");
            Name = new DbColumn<string>(this, "name");
            Email = new DbColumn<string?>(this, "email", isNullable: true);
            CreatedAt = new DbColumn<DateTime>(this, "created_at");
        }

        public DbColumn<long> Id { get; }

        public DbColumn<string> Name { get; }

        public DbColumn<string?> Email { get; }

        public DbColumn<DateTime> CreatedAt { get; }

        private static TestUser Materialize(System.Data.Common.DbDataReader reader)
            => new()
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                CreatedAt = reader.GetDateTime(2)
            };
    }

    private sealed class TestFlagTable : DbTable<TestUser>
    {
        public TestFlagTable() : base("users", "User", Materialize)
        {
            Flag = new DbColumn<bool>(this, "flag");
        }

        public DbColumn<bool> Flag { get; }

        private static TestUser Materialize(System.Data.Common.DbDataReader reader)
            => new();
    }

    private sealed class TestSqlServerUserTable : DbTable<TestUser>
    {
        public TestSqlServerUserTable() : base("flyaotorm_users", "User", Materialize)
        {
            Id = new DbColumn<long>(this, "id");
            Name = new DbColumn<string>(this, "name");
            CreatedAt = new DbColumn<DateTime>(this, "created_at");
        }

        public DbColumn<long> Id { get; }

        public DbColumn<string> Name { get; }

        public DbColumn<DateTime> CreatedAt { get; }

        private static TestUser Materialize(System.Data.Common.DbDataReader reader)
            => new()
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                CreatedAt = reader.GetDateTime(2)
            };
    }

    private sealed class TestOrderTable : DbTable<TestOrder>
    {
        public TestOrderTable() : base("orders", "Order", Materialize)
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

        private static TestOrder Materialize(System.Data.Common.DbDataReader reader)
            => new()
            {
                Id = reader.GetInt64(0),
                UserId = reader.GetInt64(1),
                Amount = reader.GetDecimal(2),
                Status = reader.GetInt32(3)
            };
    }

    private sealed class TestPaymentTable : DbTable<TestPayment>
    {
        public TestPaymentTable() : base("payments", "Payment", Materialize)
        {
            Id = new DbColumn<long>(this, "id");
            OrderId = new DbColumn<long>(this, "order_id");
            Provider = new DbColumn<string>(this, "provider");
        }

        public DbColumn<long> Id { get; }

        public DbColumn<long> OrderId { get; }

        public DbColumn<string> Provider { get; }

        private static TestPayment Materialize(System.Data.Common.DbDataReader reader)
            => new()
            {
                Id = reader.GetInt64(0),
                OrderId = reader.GetInt64(1),
                Provider = reader.GetString(2)
            };
    }

    private sealed class TestTypedQueryRowTable : DbTable<TestTypedQueryRow>
    {
        public TestTypedQueryRowTable() : base("typed_query_rows", "TypedQueryRow", Materialize)
        {
            Id = new DbColumn<Guid>(this, "id");
            BusinessDate = new DbColumn<DateOnly>(this, "business_date");
            StartTime = new DbColumn<TimeOnly>(this, "start_time");
            ChangedAt = new DbColumn<DateTimeOffset>(this, "changed_at");
        }

        public DbColumn<Guid> Id { get; }

        public DbColumn<DateOnly> BusinessDate { get; }

        public DbColumn<TimeOnly> StartTime { get; }

        public DbColumn<DateTimeOffset> ChangedAt { get; }

        private static TestTypedQueryRow Materialize(System.Data.Common.DbDataReader reader)
            => new()
            {
                Id = FlyDbValueConverter.Read<Guid>(reader.GetValue(0)),
                BusinessDate = FlyDbValueConverter.Read<DateOnly>(reader.GetValue(1)),
                StartTime = FlyDbValueConverter.Read<TimeOnly>(reader.GetValue(2)),
                ChangedAt = FlyDbValueConverter.Read<DateTimeOffset>(reader.GetValue(3))
            };
    }

    private sealed class CountOnlyRenderer : ISqlRenderer
    {
        public RenderedSql Render(SelectNode node)
            => new("select fallback", Array.Empty<SqlParameter>());

        public RenderedSql RenderCount(SelectNode node)
            => new("count fallback", new[] { new SqlParameter("p0", 1L) });
    }

    private sealed class TestUser
    {
        public long Id { get; set; }

        public string Name { get; set; } = "";

        public DateTime CreatedAt { get; set; }
    }

    private sealed class TestOrder
    {
        public long Id { get; set; }

        public long UserId { get; set; }

        public decimal Amount { get; set; }

        public int Status { get; set; }
    }

    private sealed class TestPayment
    {
        public long Id { get; set; }

        public long OrderId { get; set; }

        public string Provider { get; set; } = "";
    }

    private sealed class TestTypedQueryRow
    {
        public Guid Id { get; set; }

        public DateOnly BusinessDate { get; set; }

        public TimeOnly StartTime { get; set; }

        public DateTimeOffset ChangedAt { get; set; }
    }

    public sealed class UserNameRow
    {
        public string Name { get; set; } = "";
    }

    public sealed class UserOrderRow
    {
        public long UserId { get; set; }

        public string UserName { get; set; } = "";

        public long OrderId { get; set; }

        public decimal Amount { get; set; }
    }

    public sealed class UserOrderPaymentRow
    {
        public string UserName { get; set; } = "";

        public long OrderId { get; set; }

        public string PaymentProvider { get; set; } = "";
    }

    private static async Task ExecuteAsync(System.Data.Common.DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
