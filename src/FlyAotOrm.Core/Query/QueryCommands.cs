using System.Data.Common;

namespace FlyAotOrm;

internal static class QueryCommands
{
    public static DbCommand Create(FlyDb db, RenderedSql rendered)
    {
        var command = db.Connection.CreateCommand();
        command.CommandText = rendered.Sql;
        if (db.Transaction is not null)
        {
            command.Transaction = db.Transaction;
        }

        foreach (var parameter in rendered.Parameters)
        {
            CommandParameters.Add(command, parameter.Name, parameter.Value);
        }

        return command;
    }
}
