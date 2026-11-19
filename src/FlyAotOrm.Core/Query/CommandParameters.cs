using System.Data.Common;

namespace FlyAotOrm;

internal static class CommandParameters
{
    public static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        // 参数名必须与 SQL 占位符文本完全一致（含 '@' 前缀）：NovaDb 按名精确匹配，
        // 缺 '@' 会报 Parameter '@p0' not found；Sqlite/SqlServer/MySQL/Npgsql 均接受带 '@' 的名。
        parameter.ParameterName = name.StartsWith("@", StringComparison.Ordinal) ? name : "@" + name;
        parameter.Value = FlyDbValueConverter.ToDatabase(value) ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
