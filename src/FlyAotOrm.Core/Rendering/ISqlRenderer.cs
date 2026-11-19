namespace FlyAotOrm;

public interface ISqlRenderer
{
    RenderedSql Render(SelectNode node);

    RenderedSql RenderCount(SelectNode node);

    RenderedSql RenderAny(SelectNode node)
        => RenderCount(node);
}
