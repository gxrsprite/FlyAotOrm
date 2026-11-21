using Microsoft.Data.Sqlite;

namespace FlyAotOrm.Generator.Tests;

public sealed class EnumStorageTests
{
    [Theory]
    [InlineData("Active")]
    [InlineData("2")]
    [InlineData(2)]
    [InlineData(2L)]
    public void ReadsNamesAndNumbers(object value)
    {
        Assert.Equal(EnumRowState.Active, FlyDbValueConverter.Read<EnumRowState>(value));
        Assert.Equal(EnumRowState.Active, FlyDbValueConverter.Read<EnumRowState?>(value));
    }

    [Fact]
    public void ConvertsNullFlagsAndUnsignedValues()
    {
        Assert.Null(FlyDbValueConverter.Read<EnumRowState?>(DBNull.Value));
        Assert.Null(FlyDbValueConverter.ToDatabase(null, DbEnumStorage.String));
        Assert.Equal(2, FlyDbValueConverter.ToDatabase(EnumRowState.Active));
        Assert.Equal("Active", FlyDbValueConverter.ToDatabase(EnumRowState.Active, DbEnumStorage.String));
        Assert.Equal(AccessFlags.Read | AccessFlags.Write, FlyDbValueConverter.Read<AccessFlags>("Read, Write"));
        Assert.Equal("Read, Write", FlyDbValueConverter.ToDatabase(AccessFlags.Read | AccessFlags.Write, DbEnumStorage.String));
        Assert.Equal(ulong.MaxValue, FlyDbValueConverter.ToDatabase(WideEnum.Max));
        Assert.Equal(WideEnum.Max, FlyDbValueConverter.Read<WideEnum>(ulong.MaxValue));
        Assert.Equal((EnumRowState)(-1), FlyDbValueConverter.Read<EnumRowState>(-1L));
        Assert.Throws<ArgumentException>(() => FlyDbValueConverter.Read<EnumRowState>("unknown"));
        Assert.Throws<ArgumentException>(() => FlyDbValueConverter.Read<EnumRowState>("active"));
    }

    [Theory]
    [InlineData(DbDialectKind.Sqlite)]
    [InlineData(DbDialectKind.PostgreSql)]
    [InlineData(DbDialectKind.MySql)]
    [InlineData(DbDialectKind.SqlServer)]
    [InlineData(DbDialectKind.NovaDb)]
    public void RendersStringParametersAndInlineNames(DbDialectKind dialect)
    {
        using var connection = new SqliteConnection();
        var db = new FlyDb(connection, dialect);
        var table = Tables.EnumStorageRow;
        var query = db.Select(table).Where(table.Text.Eq(EnumRowState.Active));
        Assert.Equal(DbEnumStorage.String, table.Text.EnumStorage);
        Assert.Equal(DbEnumStorage.Number, table.Number.EnumStorage);
        Assert.Equal("Active", Assert.Single(query.ToSql().Parameters).Value);
        Assert.Contains("'Active'", query.InlineParameters().ToSql().Sql);
        Assert.Empty(query.InlineParameters().ToSql().Parameters);

        var values = db.Select(table)
            .Where(table.Text.In(EnumRowState.Pending, EnumRowState.Active))
            .And(table.Text.Between(EnumRowState.Pending, EnumRowState.Active))
            .ToSql().Parameters.Select(static parameter => parameter.Value).ToArray();
        Assert.Equal(new object?[] { "Pending", "Active", "Pending", "Active" }, values);
        Assert.Contains("is null", db.Select(table).Where(table.Text.Eq((EnumRowState?)null)).ToSql().Sql);
    }

    [Fact]
    public async Task GeneratedCrudAndQueriesRoundTripBothStorageModes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "create table enum_rows (id text primary key, number integer null, text text null)";
        await command.ExecuteNonQueryAsync();

        var inserted = await EnumStorageRowCrud.InsertReturningAsync(connection, new EnumStorageRow
        {
            Id = EnumRowState.Pending,
            Number = EnumRowState.Active,
            Text = EnumRowState.Active
        });
        Assert.Equal(EnumRowState.Active, inserted.Number);
        Assert.Equal(EnumRowState.Active, inserted.Text);
        command.CommandText = "select typeof(number) || ':' || number || ':' || typeof(text) || ':' || text from enum_rows";
        Assert.Equal("integer:2:text:Active", await command.ExecuteScalarAsync());
        command.CommandText = "select id from enum_rows";
        Assert.Equal("Pending", await command.ExecuteScalarAsync());

        Assert.True(await EnumStorageRowCrud.ExistsAsync(connection, EnumRowState.Pending));
        Assert.Equal(EnumRowState.Active, (await EnumStorageRowCrud.GetByIdAsync(connection, EnumRowState.Pending))!.Text);
        var db = new FlyDb(connection, DbDialectKind.Sqlite);
        var table = Tables.EnumStorageRow;
        Assert.Single(await db.Select(table).Where(table.Text.In(new EnumRowState?[] { EnumRowState.Active })).ToListAsync());
        Assert.Single(await db.Select(table).Where(table.Number.Eq(EnumRowState.Active)).ToListAsync());
        Assert.Single(await db.Select(table).Where(table.Text.Eq(EnumRowState.Active)).InlineParameters().ToListAsync());
        var projected = await db.Select<EnumStorageProjection>().From(table)
            .Columns(table.Text.As(nameof(EnumStorageProjection.State))).FirstOrDefaultAsync();
        Assert.Equal(EnumRowState.Active, projected!.State);
        Assert.Equal(EnumRowState.Active, await new EnumStorageRepository(connection).ReadNameAsync());

        Assert.Equal(1, await EnumStorageRowCrud.UpdateAsync(connection, new EnumStorageRow { Id = EnumRowState.Pending }));
        var updated = await EnumStorageRowCrud.GetByIdAsync(connection, EnumRowState.Pending);
        Assert.Null(updated!.Text);
        Assert.Null(updated.Number);
        Assert.Single(await db.Select(table).Where(table.Text.EqNull()).ToListAsync());
        Assert.Equal(1, await EnumStorageRowCrud.DeleteByIdAsync(connection, EnumRowState.Pending));
        Assert.False(await EnumStorageRowCrud.ExistsAsync(connection, EnumRowState.Pending));

        Assert.Equal(1, await EnumStorageRowCrud.InsertAsync(connection, new EnumStorageRow
        {
            Id = EnumRowState.Active, Text = EnumRowState.Pending, Number = EnumRowState.Pending
        }));
        Assert.Equal(EnumRowState.Pending, (await EnumStorageRowCrud.ListAllAsync(connection))[0].Text);
    }

    [Flags]
    private enum AccessFlags { Read = 1, Write = 2 }
    private enum WideEnum : ulong { Max = ulong.MaxValue }
}

public enum EnumRowState { Pending = 1, Active = 2 }

[DbTable("enum_rows")]
public sealed partial class EnumStorageRow
{
    [DbKey(IsIdentity = false)]
    [DbColumn("id", EnumStorage = DbEnumStorage.String)]
    public EnumRowState Id { get; set; }

    [DbColumn("number")]
    public EnumRowState? Number { get; set; }

    [DbColumn("text", EnumStorage = DbEnumStorage.String)]
    public EnumRowState? Text { get; set; }
}

public sealed class EnumStorageProjection
{
    public EnumRowState? State { get; set; }
}

[DbRepository]
public partial interface IEnumStorageRepository
{
    [SqlScalar("select 'Active'")]
    Task<EnumRowState> ReadNameAsync();
}
