using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using FlyAotOrm.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace FlyAotOrm.Generator.Tests;

public sealed class FlyAotOrmGeneratorTests
{
    [Fact]
    public void GeneratesEntityMetadata()
    {
        const string source = """
#nullable enable
using FlyAotOrm;

namespace Demo;

[DbTable("users")]
public partial class User
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("name")]
    public string Name { get; set; } = "";

    [DbColumn("email")]
    public string? Email { get; set; }
}
""";

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(result.GeneratedTrees, static tree => tree.FilePath.EndsWith("UserMetadata.g.cs"));
        var text = generated.GetText().ToString();
        Assert.Contains("public const string TableName = \"users\";", text);
        Assert.Contains("public const string IdColumn = \"id\";", text);
        Assert.Contains("public const bool NameIsNullable = false;", text);
        Assert.Contains("public const bool EmailIsNullable = true;", text);
        Assert.Contains("public static global::System.ReadOnlySpan<string> NullableColumnNames", text);
        Assert.Contains("\"email\",", text);
        Assert.Contains("Email = new global::FlyAotOrm.DbColumn<string?>(this, \"email\", isNullable: true);", text);
        Assert.Contains("sealed class UserTable : global::FlyAotOrm.DbTable<global::Demo.User>", text);
        Assert.Contains("public static readonly UserTable User = new();", text);
        Assert.Contains("static class UserCrud", text);
        Assert.Contains("private const string SqliteSelectByIdSql", text);
        Assert.Contains("private const string SqlServerUpdateSql", text);
        Assert.Contains("private const string MySqlUpdateSql", text);
        Assert.Contains("private const string NovaDbSelectByIdSql", text);
        Assert.Contains("private const string NovaDbInsertSql", text);
        Assert.Contains("private const string NovaDbUpdateSql", text);
        Assert.Contains("global::FlyAotOrm.DbDialectKind.NovaDb => NovaDbSelectByIdSql", text);
        Assert.Contains("select \\\"id\\\" as \\\"Id\\\", \\\"name\\\" as \\\"Name\\\", \\\"email\\\" as \\\"Email\\\" from \\\"users\\\" where \\\"id\\\" = @Id", text);
        Assert.Contains("update [users] set [name] = @Name, [email] = @Email where [id] = @Id", text);
        Assert.Contains("update `users` set `name` = @Name, `email` = @Email where `id` = @Id", text);
        Assert.Contains("global::FlyAotOrm.DbDialectKind dialect", text);
        Assert.Contains("public static async global::System.Threading.Tasks.Task<bool> ExistsAsync", text);
        Assert.Contains("select case when exists (select 1 from \\\"users\\\" where \\\"id\\\" = @Id) then 1 else 0 end", text);
        Assert.Contains("InsertReturningAsync", text);
        Assert.Contains("returning \\\"id\\\" as \\\"Id\\\", \\\"name\\\" as \\\"Name\\\", \\\"email\\\" as \\\"Email\\\"", text);
        Assert.Contains("output inserted.[id] as [Id], inserted.[name] as [Name], inserted.[email] as [Email]", text);
        Assert.Contains("global::FlyAotOrm.DbDialectKind.SqlServer => SqlServerInsertReturningSql", text);
        Assert.Contains("InsertReturningAsync is currently generated only for SQLite, PostgreSQL and SQL Server.", text);
        Assert.Contains("DbConnection connection", text);
        Assert.Contains("ExecuteNonQueryAsync", text);
        Assert.Contains("UserTable.Materialize(reader)", text);
        Assert.DoesNotContain("SqlMapper", text);
        AssertMatchesSnapshot("UserMetadata.g.cs", text);
    }

    [Fact]
    public void GeneratesRepositoryImplementation()
    {
        const string source = """
using System.Threading.Tasks;
using System.Threading;
using FlyAotOrm;

namespace Demo;

[DbDefaultFilter(typeof(UserFilters), nameof(UserFilters.Active))]
[DbTable("users")]
public sealed partial class User
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("is_deleted")]
    [DbSoftDelete]
    public bool IsDeleted { get; set; }

    [DbColumn("status")]
    public int Status { get; set; }
}

public static class UserFilters
{
    public static SqlCondition Active() => Tables.User.Status.Eq(1);
}

[DbRepository]
public partial interface IUserRepository
{
    Query<User> Query();

    CountQuery<User> CountQuery();

    AnyQuery<User> AnyQuery();

    [SqlQuery("select * from users where id = @id")]
    Task<User?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    [SqlExecute("delete from users where id = @id")]
    Task<int> DeleteAsync(long id);
}
""";

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(result.GeneratedTrees, static tree => tree.FilePath.EndsWith("UserRepository.g.cs"));
        var text = generated.GetText().ToString();
        Assert.Contains("sealed class UserRepository : global::Demo.IUserRepository", text);
        Assert.Contains("DbConnection connection", text);
        Assert.Contains("public global::FlyAotOrm.Query<global::Demo.User> Query()", text);
        Assert.Contains("return _db.Select(global::Demo.Tables.User).Where(global::Demo.Tables.User.IsDeleted.Eq(false)).And(global::Demo.UserFilters.Active());", text);
        Assert.Contains("public global::FlyAotOrm.CountQuery<global::Demo.User> CountQuery()", text);
        Assert.Contains("return _db.SelectCount(global::Demo.Tables.User).Where(global::Demo.Tables.User.IsDeleted.Eq(false)).And(global::Demo.UserFilters.Active());", text);
        Assert.Contains("public global::FlyAotOrm.AnyQuery<global::Demo.User> AnyQuery()", text);
        Assert.Contains("return _db.SelectAny(global::Demo.Tables.User).Where(global::Demo.Tables.User.IsDeleted.Eq(false)).And(global::Demo.UserFilters.Active());", text);
        Assert.Contains("public UserRepository(DbConnection connection, global::FlyAotOrm.DbDialectKind dialect, DbTransaction? transaction = null)", text);
        Assert.Contains("ExecuteNonQueryAsync", text);
        Assert.Contains("Materialize<global::Demo.User>(reader)", text);
        Assert.Contains("ExecuteReaderAsync(cancellationToken)", text);
        Assert.Contains("ReadAsync(cancellationToken)", text);
        Assert.DoesNotContain("SqlMapper", text);
        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Id == "DORM002" && diagnostic.GetMessage().Contains("cancellationToken"));

        var materializer = Assert.Single(result.GeneratedTrees, static tree => tree.FilePath.EndsWith("IUserRepository_GetByIdAsync_UserMaterializer.g.cs"));
        var materializerText = materializer.GetText().ToString();
        Assert.Contains("FlyProjectionMaterializerRegistry.Register<global::Demo.User>(Materialize)", materializerText);
        Assert.Contains("value.Id = Read<long>(reader, ordinal);", materializerText);
        AssertMatchesSnapshot("UserRepository.g.cs", text);
    }

    [Fact]
    public void GeneratesProjectionMaterializer()
    {
        const string source = """
using FlyAotOrm;

namespace Demo;

[DbProjection]
public sealed class UserOrderRow
{
    public long UserId { get; set; }

    public string UserName { get; set; } = "";
}
""";

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(result.GeneratedTrees, static tree => tree.FilePath.EndsWith("UserOrderRowProjectionMaterializer.g.cs"));
        var text = generated.GetText().ToString();
        Assert.Contains("FlyProjectionMaterializerRegistry.Register<global::Demo.UserOrderRow>(Materialize)", text);
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", text);
        Assert.Contains("value.UserId = Read<long>(reader, ordinal);", text);
        Assert.Contains("value.UserName = Read<string>(reader, ordinal);", text);
    }

    [Fact]
    public void GeneratesFlyDbContextQueryEntrypoints()
    {
        const string source = """
using FlyAotOrm;

namespace Demo;

[DbTable("users")]
public partial class User
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }
}

[DbTable("categories")]
public partial class Category
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }
}
""";

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(result.GeneratedTrees, static tree => tree.FilePath.EndsWith("Demo_FlyDbContext.g.cs"));
        var text = generated.GetText().ToString();
        Assert.Contains("public sealed class FlyDbContext", text);
        Assert.Contains("public FlyDbContext(global::FlyAotOrm.FlyDb db)", text);
        Assert.Contains("public global::FlyAotOrm.Query<global::Demo.Category> Categories => _db.Select(Tables.Category);", text);
        Assert.Contains("public global::FlyAotOrm.Query<global::Demo.User> Users => _db.Select(Tables.User);", text);
        Assert.Equal(
            NormalizeSnapshot(ReadSnapshot("Demo_FlyDbContext.g.cs")),
            NormalizeSnapshot(text));
    }

    [Fact]
    public void GeneratedFlyDbContextAvoidsDuplicatePropertyNames()
    {
        const string source = """
using FlyAotOrm;

namespace Demo;

[DbTable("user")]
public partial class User
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }
}

[DbTable("users")]
public partial class Users
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }
}
""";

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(result.GeneratedTrees, static tree => tree.FilePath.EndsWith("Demo_FlyDbContext.g.cs"));
        var text = generated.GetText().ToString();
        Assert.Contains("public global::FlyAotOrm.Query<global::Demo.User> Users => _db.Select(Tables.User);", text);
        Assert.Contains("public global::FlyAotOrm.Query<global::Demo.Users> UsersQuery => _db.Select(Tables.Users);", text);
    }

    [Fact]
    public void GeneratedFlyDbContextAppliesSoftDeleteDefaultFilter()
    {
        const string source = """
using FlyAotOrm;

namespace Demo;

[DbTable("soft_delete_users")]
public partial class SoftDeleteUser
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("is_deleted")]
    [DbSoftDelete]
    public bool IsDeleted { get; set; }
}
""";

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(result.GeneratedTrees, static tree => tree.FilePath.EndsWith("Demo_FlyDbContext.g.cs"));
        var text = generated.GetText().ToString();
        Assert.Contains("public global::FlyAotOrm.Query<global::Demo.SoftDeleteUser> SoftDeleteUsers => _db.Select(Tables.SoftDeleteUser).Where(Tables.SoftDeleteUser.IsDeleted.Eq(false));", text);
    }

    [Fact]
    public void GeneratedFlyDbContextAppliesCustomDefaultFilter()
    {
        const string source = """
using FlyAotOrm;

namespace Demo;

[DbDefaultFilter(typeof(UserFilters), nameof(UserFilters.Active))]
[DbTable("users")]
public partial class User
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("status")]
    public int Status { get; set; }
}

public static class UserFilters
{
    public static SqlCondition Active() => Tables.User.Status.Eq(1);
}
""";

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(result.GeneratedTrees, static tree => tree.FilePath.EndsWith("Demo_FlyDbContext.g.cs"));
        var text = generated.GetText().ToString();
        Assert.Contains("public global::FlyAotOrm.Query<global::Demo.User> Users => _db.Select(Tables.User).Where(global::Demo.UserFilters.Active());", text);
    }

    [Fact]
    public void GeneratedFlyDbContextAppliesTenantDefaultFilter()
    {
        const string source = """
using FlyAotOrm;

namespace Demo;

[DbTable("tenant_users")]
public partial class TenantUser
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("tenant_id")]
    [DbTenant]
    public long TenantId { get; set; }
}
""";

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(result.GeneratedTrees, static tree => tree.FilePath.EndsWith("Demo_FlyDbContext.g.cs"));
        var text = generated.GetText().ToString();
        Assert.Contains("public global::FlyAotOrm.Query<global::Demo.TenantUser> TenantUsers => _db.Select(Tables.TenantUser).Where(Tables.TenantUser.TenantId.Eq(global::FlyAotOrm.FlyTenant.Get<long>()));", text);
    }

    [Fact]
    public void GeneratedProjectionMaterializerMatchesSnapshot()
    {
        const string source = """
using FlyAotOrm;

namespace Demo;

[DbProjection]
public sealed class UserOrderRow
{
    public long UserId { get; set; }

    public string UserName { get; set; } = "";
}
""";

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(result.GeneratedTrees, static tree => tree.FilePath.EndsWith("UserOrderRowProjectionMaterializer.g.cs"));
        Assert.Equal(
            NormalizeSnapshot(ReadSnapshot("UserOrderRowProjectionMaterializer.g.cs")),
            NormalizeSnapshot(generated.GetText().ToString()));
    }

    [Fact]
    public void AutoDiscoversProjectionMaterializerFromQueryDslSelect()
    {
        const string source = """
using FlyAotOrm;

namespace Demo;

public sealed class UserOrderRow
{
    public long UserId { get; set; }

    public string UserName { get; set; } = "";
}

public static class Queries
{
    public static void Build(FlyDb db)
    {
        _ = db.Select<UserOrderRow>();
    }
}
""";

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var generated = Assert.Single(result.GeneratedTrees, static tree => tree.FilePath.EndsWith("UserOrderRowProjectionMaterializer.g.cs"));
        var text = generated.GetText().ToString();
        Assert.Contains("FlyProjectionMaterializerRegistry.Register<global::Demo.UserOrderRow>(Materialize)", text);
        Assert.Contains("value.UserId = Read<long>(reader, ordinal);", text);
        Assert.Contains("value.UserName = Read<string>(reader, ordinal);", text);
    }

    [Fact]
    public void ReportsProjectionMaterializerUnavailableForInaccessibleQueryDslProjection()
    {
        const string source = """
using FlyAotOrm;

namespace Demo;

public static class Queries
{
    private sealed class HiddenRow
    {
        public long Id { get; set; }
    }

    public static void Build(FlyDb db)
    {
        _ = db.Select<HiddenRow>();
    }
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "DORM004" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void DoesNotTreatLinqSelectAsProjectionQueryDsl()
    {
        const string source = """
using System.Linq;
using FlyAotOrm;

namespace Demo;

public sealed class UserOrderRow
{
    public long UserId { get; set; }
}

public static class Queries
{
    public static void Build(UserOrderRow[] rows)
    {
        _ = rows.Select(row => row.UserId);
    }
}
""";

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.GeneratedTrees, static tree => tree.FilePath.EndsWith("UserOrderRowProjectionMaterializer.g.cs"));
        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Id == "DORM004");
    }

    [Fact]
    public void ReportsMissingSqlParameter()
    {
        const string source = """
using System.Threading.Tasks;
using FlyAotOrm;

[DbRepository]
public partial interface IUserRepository
{
    [SqlQuery("select * from users where id = @missing")]
    Task<int?> GetByIdAsync(long id);
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "DORM001");
    }

    [Fact]
    public void ReportsCrudKeyMissing()
    {
        const string source = """
using FlyAotOrm;

[DbTable("users")]
public partial class User
{
    public long Id { get; set; }
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "DORM003");
    }

    [Fact]
    public void ReportsCrudHasNoUpdateableColumns()
    {
        const string source = """
using FlyAotOrm;

[DbTable("audit_events")]
public partial class AuditEvent
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "DORM005" && diagnostic.Severity == DiagnosticSeverity.Warning);
        var generated = Assert.Single(result.GeneratedTrees, static tree => tree.FilePath.EndsWith("AuditEventMetadata.g.cs"));
        Assert.DoesNotContain("UpdateAsync", generated.GetText().ToString());
    }

    [Fact]
    public void ReportsSoftDeleteDiagnosticForNonBooleanProperty()
    {
        const string source = """
using FlyAotOrm;

[DbTable("users")]
public partial class User
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("deleted_at")]
    [DbSoftDelete]
    public string DeletedAt { get; set; } = "";
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "DORM006" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ReportsDefaultFilterDiagnosticForInvalidMethod()
    {
        const string source = """
using FlyAotOrm;

[DbDefaultFilter(typeof(UserFilters), nameof(UserFilters.Active))]
[DbTable("users")]
public partial class User
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }
}

public static class UserFilters
{
    public static bool Active() => true;
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "DORM007" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ReportsDefaultFilterDiagnosticForMultipleSoftDeleteProperties()
    {
        const string source = """
using FlyAotOrm;

[DbTable("users")]
public partial class User
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("is_deleted")]
    [DbSoftDelete]
    public bool IsDeleted { get; set; }

    [DbColumn("is_archived")]
    [DbSoftDelete]
    public bool IsArchived { get; set; }
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "DORM008" && diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.GetMessage().Contains("[DbSoftDelete]"));
    }

    [Fact]
    public void ReportsDefaultFilterDiagnosticForMultipleTenantProperties()
    {
        const string source = """
using FlyAotOrm;

[DbTable("users")]
public partial class User
{
    [DbKey]
    [DbColumn("id")]
    public long Id { get; set; }

    [DbColumn("tenant_id")]
    [DbTenant]
    public long TenantId { get; set; }

    [DbColumn("workspace_id")]
    [DbTenant]
    public long WorkspaceId { get; set; }
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "DORM008" && diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.GetMessage().Contains("[DbTenant]"));
    }

    [Fact]
    public void ReportsRepositoryQueryDiagnosticForParameterizedQueryEntryPoint()
    {
        const string source = """
using FlyAotOrm;

[DbTable("users")]
public partial class User
{
    [DbKey]
    public long Id { get; set; }
}

[DbRepository]
public partial interface IUserRepository
{
    Query<User> Query(long id);
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "DORM009" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ReportsRepositoryQueryDiagnosticForNonEntityQueryEntryPoint()
    {
        const string source = """
using FlyAotOrm;

public sealed class User
{
    public long Id { get; set; }
}

[DbRepository]
public partial interface IUserRepository
{
    Query<User> Query();
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "DORM009" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ReportsRepositoryQueryMaterializerDiagnosticForScalarSqlQuery()
    {
        const string source = """
using System.Threading.Tasks;
using FlyAotOrm;

[DbRepository]
public partial interface IUserRepository
{
    [SqlQuery("select count(1) from users")]
    Task<int> CountAsync();
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "DORM010" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ReportsRepositoryQueryMaterializerDiagnosticForUnsupportedDto()
    {
        const string source = """
using System.Threading.Tasks;
using FlyAotOrm;

public sealed class UserRow
{
    public long Id { get; }
}

[DbRepository]
public partial interface IUserRepository
{
    [SqlQuery("select id from users")]
    Task<UserRow?> GetAsync();
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "DORM010" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ReportsRepositoryMethodDiagnosticForUnsupportedMethod()
    {
        const string source = """
using System.Threading.Tasks;
using FlyAotOrm;

[DbRepository]
public partial interface IUserRepository
{
    Task<int> CountAsync();
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "DORM011" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ReportsRepositorySqlAttributeDiagnosticForAmbiguousMethod()
    {
        const string source = """
using System.Threading.Tasks;
using FlyAotOrm;

[DbRepository]
public partial interface IUserRepository
{
    [SqlScalar("select count(1) from users")]
    [SqlExecute("delete from users")]
    Task<int> CountAsync();
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "DORM012" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ReportsRepositorySqlReturnTypeDiagnosticForInvalidExecuteReturn()
    {
        const string source = """
using System.Threading.Tasks;
using FlyAotOrm;

[DbRepository]
public partial interface IUserRepository
{
    [SqlExecute("delete from users")]
    Task<string> DeleteAsync();
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "DORM013" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ReportsRepositorySqlReturnTypeDiagnosticForNonGenericTask()
    {
        const string source = """
using System.Threading.Tasks;
using FlyAotOrm;

[DbRepository]
public partial interface IUserRepository
{
    [SqlScalar("select count(1) from users")]
    Task CountAsync();
}
""";

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Id == "DORM013" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static GeneratorRunResult RunGenerator(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Concat(new[]
            {
                MetadataReference.CreateFromFile(typeof(DbTableAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(FlyDb).Assembly.Location)
            });

        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorTests",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        IIncrementalGenerator generator = new FlyAotOrmGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator.AsSourceGenerator() }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var outputDiagnostics = outputCompilation.GetDiagnostics()
            .Concat(diagnostics)
            .ToImmutableArray();

        return new GeneratorRunResult(
            driver.GetRunResult().GeneratedTrees.ToImmutableArray(),
            outputDiagnostics);
    }

    private static string ReadSnapshot(string fileName, [CallerFilePath] string callerPath = "")
    {
        var directory = Path.GetDirectoryName(callerPath) ?? throw new InvalidOperationException("Cannot resolve test source directory.");
        return File.ReadAllText(Path.Combine(directory, "Snapshots", fileName));
    }

    private static void AssertMatchesSnapshot(string fileName, string text, [CallerFilePath] string callerPath = "")
    {
        var directory = Path.GetDirectoryName(callerPath) ?? throw new InvalidOperationException("Cannot resolve test source directory.");
        var snapshotPath = Path.Combine(directory, "Snapshots", fileName);
        if (Environment.GetEnvironmentVariable("FLYAOTORM_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            File.WriteAllText(snapshotPath, text);
        }

        Assert.Equal(
            NormalizeSnapshot(File.ReadAllText(snapshotPath)),
            NormalizeSnapshot(text));
    }

    private static string NormalizeSnapshot(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

    private sealed record GeneratorRunResult(ImmutableArray<SyntaxTree> GeneratedTrees, ImmutableArray<Diagnostic> Diagnostics);
}
