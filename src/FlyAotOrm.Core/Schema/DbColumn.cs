namespace FlyAotOrm;

public sealed class DbColumn<T>
{
    public DbColumn(DbTable table, string columnName, bool isNullable = false)
    {
        Table = table;
        ColumnName = columnName;
        IsNullable = isNullable;
    }

    public DbTable Table { get; }

    public string ColumnName { get; }

    public bool IsNullable { get; }

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

    public SqlCondition In(params T[] values) => new InCondition(ToExpression(), values.Cast<object?>().ToArray());

    public SqlCondition In(IEnumerable<T> values) => new InCondition(ToExpression(), values.Cast<object?>().ToArray());

    public SqlCondition Between(T lower, T upper) => new BetweenCondition(ToExpression(), lower, upper);

    public SqlCondition IsNull() => new NullCondition(ToExpression(), IsNot: false);

    public SqlCondition IsNotNull() => new NullCondition(ToExpression(), IsNot: true);

    public SqlOrder Asc() => new(ToExpression(), Descending: false);

    public SqlOrder Desc() => new(ToExpression(), Descending: true);

    public SelectedColumn As(string alias) => new(ToExpression(), alias);

    private SqlCondition Compare(string op, T value)
        => new BinaryCondition(ToExpression(), op, new ValueExpression(value));

    private SqlCondition Compare(string op, DbColumn<T> column)
        => new BinaryCondition(ToExpression(), op, column.ToExpression());
}
