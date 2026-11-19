namespace FlyAotOrm.Generator.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class ExternalDatabaseFactAttribute : FactAttribute
{
    public ExternalDatabaseFactAttribute(string connectionVariable)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(connectionVariable)))
        {
            Skip = $"Set {connectionVariable} to a dedicated disposable test database to run this test.";
        }
    }
}
