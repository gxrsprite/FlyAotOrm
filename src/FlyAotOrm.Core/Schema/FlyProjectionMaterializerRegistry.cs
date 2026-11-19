using System.Data.Common;

namespace FlyAotOrm;

public static class FlyProjectionMaterializerRegistry
{
    private static readonly Dictionary<Type, Delegate> Materializers = new();

    public static void Register<TProjection>(DbRowMaterializer<TProjection> materializer)
    {
        ArgumentNullException.ThrowIfNull(materializer);
        Materializers[typeof(TProjection)] = materializer;
    }

    public static bool TryGet<TProjection>(out DbRowMaterializer<TProjection> materializer)
    {
        if (Materializers.TryGetValue(typeof(TProjection), out var value) &&
            value is DbRowMaterializer<TProjection> typed)
        {
            materializer = typed;
            return true;
        }

        materializer = default!;
        return false;
    }
}
