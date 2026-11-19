namespace FlyAotOrm;

public sealed class SqlRendererOptions
{
    public static SqlRendererOptions Default { get; } = new();

    public SqlRendererOptions(
        string valueParameterNamePrefix = "p",
        string limitParameterNamePrefix = "limit",
        string offsetParameterNamePrefix = "offset")
    {
        ValueParameterNamePrefix = ValidatePrefix(valueParameterNamePrefix, nameof(valueParameterNamePrefix));
        LimitParameterNamePrefix = ValidatePrefix(limitParameterNamePrefix, nameof(limitParameterNamePrefix));
        OffsetParameterNamePrefix = ValidatePrefix(offsetParameterNamePrefix, nameof(offsetParameterNamePrefix));
    }

    public string ValueParameterNamePrefix { get; }

    public string LimitParameterNamePrefix { get; }

    public string OffsetParameterNamePrefix { get; }

    private static string ValidatePrefix(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Parameter name prefix must not be empty.", parameterName);
        }

        if (!IsIdentifierStart(value[0]))
        {
            throw new ArgumentException("Parameter name prefix must start with a letter or underscore.", parameterName);
        }

        for (var i = 1; i < value.Length; i++)
        {
            if (!IsIdentifierPart(value[i]))
            {
                throw new ArgumentException("Parameter name prefix must contain only letters, digits, or underscores.", parameterName);
            }
        }

        return value;
    }

    private static bool IsIdentifierStart(char value)
        => value == '_' || char.IsLetter(value);

    private static bool IsIdentifierPart(char value)
        => value == '_' || char.IsLetterOrDigit(value);
}
