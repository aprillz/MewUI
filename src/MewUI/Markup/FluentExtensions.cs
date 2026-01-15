namespace Aprillz.MewUI;

public static class FluentExtensions
{
    public static T Ref<T>(this T control, out T field) where T : class
    {
        field = control;
        return control;
    }
}
