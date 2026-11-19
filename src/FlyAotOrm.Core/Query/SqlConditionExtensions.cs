namespace FlyAotOrm;

public static class SqlConditionExtensions
{
    public static SqlCondition And(this SqlCondition left, SqlCondition right)
        => new CombinedCondition(left, "and", right);

    public static SqlCondition Or(this SqlCondition left, SqlCondition right)
        => new CombinedCondition(left, "or", right);
}
