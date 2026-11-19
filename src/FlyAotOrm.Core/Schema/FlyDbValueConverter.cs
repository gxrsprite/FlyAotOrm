namespace FlyAotOrm;

public static class FlyDbValueConverter
{
    public static T Read<T>(object? value)
    {
        if (value is null || value is DBNull)
        {
            return default!;
        }

        if (value is T typed)
        {
            return typed;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (targetType.IsEnum)
        {
            return (T)Enum.ToObject(targetType, value);
        }

        if (targetType == typeof(Guid))
        {
            return (T)(object)ReadGuid(value);
        }

        if (targetType == typeof(DateOnly))
        {
            return (T)(object)ReadDateOnly(value);
        }

        if (targetType == typeof(TimeOnly))
        {
            return (T)(object)ReadTimeOnly(value);
        }

        if (targetType == typeof(DateTimeOffset))
        {
            return (T)(object)ReadDateTimeOffset(value);
        }

        return (T)Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static object? ToDatabase(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var type = value.GetType();
        if (type.IsEnum)
        {
            return Convert.ChangeType(value, Enum.GetUnderlyingType(type), System.Globalization.CultureInfo.InvariantCulture);
        }

        return value switch
        {
            Guid guid => guid.ToString("D"),
            DateOnly dateOnly => dateOnly.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            TimeOnly timeOnly => timeOnly.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            _ => value
        };
    }

    private static Guid ReadGuid(object value)
        => value switch
        {
            Guid guid => guid,
            string text => Guid.Parse(text),
            byte[] bytes => new Guid(bytes),
            _ => Guid.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!)
        };

    private static DateOnly ReadDateOnly(object value)
        => value switch
        {
            DateOnly dateOnly => dateOnly,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            string text => DateOnly.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
            _ => DateOnly.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!, System.Globalization.CultureInfo.InvariantCulture)
        };

    private static TimeOnly ReadTimeOnly(object value)
        => value switch
        {
            TimeOnly timeOnly => timeOnly,
            TimeSpan timeSpan => TimeOnly.FromTimeSpan(timeSpan),
            DateTime dateTime => TimeOnly.FromDateTime(dateTime),
            string text => TimeOnly.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
            _ => TimeOnly.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!, System.Globalization.CultureInfo.InvariantCulture)
        };

    private static DateTimeOffset ReadDateTimeOffset(object value)
        => value switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            DateTime dateTime => new DateTimeOffset(dateTime),
            string text => DateTimeOffset.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
            _ => DateTimeOffset.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!, System.Globalization.CultureInfo.InvariantCulture)
        };
}
