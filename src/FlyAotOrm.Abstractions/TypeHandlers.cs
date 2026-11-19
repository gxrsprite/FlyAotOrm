namespace FlyAotOrm;

public interface IDbTypeHandler<T>
{
    object? ToDatabase(T value);

    T FromDatabase(object? value);
}
