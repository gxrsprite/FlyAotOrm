namespace FlyAotOrm;

public sealed class DbColumn<T>
{
    public DbColumn(DbTable table, string columnName, bool isNullable = false)
        : this(table, columnName, isNullable, DbEnumStorage.Number)
    {
    }

    public DbColumn(DbTable table, string columnName, bool isNullable, DbEnumStorage enumStorage)
    {
        Table = table;
        ColumnName = columnName;
        IsNullable = isNullable;
        EnumStorage = enumStorage;
    }

    public DbTable Table { get; }

    public string ColumnName { get; }

    public bool IsNullable { get; }

    public DbEnumStorage EnumStorage { get; }

    public ColumnExpression ToExpression() => new(Table.Alias, ColumnName);

    public SqlCondition Eq(T value) => Compare("=", value);

    public SqlCondition Eq(DbColumn<T> column) => Compare("=", column);

    public SqlCondition NotEq(T value) => Compare("<>", value);

    public SqlCondition NotEq(DbColumn<T> column) => Compare("<>", column);

    public SqlCondition EqNull() => IsNull();

    public SqlCondition NotEqNull() => IsNotNull();

    public SqlCondition GreaterThan(T value) => Compare(">", value);

    public SqlCondition GreaterThan(DbColumn<T> column) => Compare(">", column);

    public SqlCondition GreaterOrEqual(T value) => Compare(">=", value);

    public SqlCondition GreaterOrEqual(DbColumn<T> column) => Compare(">=", column);

    public SqlCondition LessThan(T value) => Compare("<", value);

    public SqlCondition LessThan(DbColumn<T> column) => Compare("<", column);

    public SqlCondition LessOrEqual(T value) => Compare("<=", value);

    public SqlCondition LessOrEqual(DbColumn<T> column) => Compare("<=", column);

    public SqlCondition Like(string pattern) => new LikeCondition(ToExpression(), pattern);

    public SqlCondition StartsWith(string value) => Like(value + "%");

    public SqlCondition EndsWith(string value) => Like("%" + value);

    public SqlCondition Contains(string value) => Like("%" + value + "%");

    public SqlCondition In(params T[] values) => In((IEnumerable<T>)values);

    public SqlCondition In(IEnumerable<T> values) => new InCondition(ToExpression(), values.Select(ToConditionValue).ToArray());

    public SqlCondition Between(T lower, T upper) => new BetweenCondition(ToExpression(), ToConditionValue(lower), ToConditionValue(upper));

    public SqlCondition IsNull() => new NullCondition(ToExpression(), IsNot: false);

    public SqlCondition IsNotNull() => new NullCondition(ToExpression(), IsNot: true);

    public SqlOrder Asc() => new(ToExpression(), Descending: false);

    public SqlOrder Desc() => new(ToExpression(), Descending: true);

    public SelectedColumn As(string alias) => new(ToExpression(), alias);

    private SqlCondition Compare(string op, T value)
        => new BinaryCondition(ToExpression(), op, new ValueExpression(ToConditionValue(value)));

    private object? ToConditionValue(T value)
        => EnumStorage == DbEnumStorage.String
            ? FlyDbValueConverter.ToDatabase(value, EnumStorage)
            : value;

    private SqlCondition Compare(string op, DbColumn<T> column)
        => new BinaryCondition(ToExpression(), op, column.ToExpression());
}
