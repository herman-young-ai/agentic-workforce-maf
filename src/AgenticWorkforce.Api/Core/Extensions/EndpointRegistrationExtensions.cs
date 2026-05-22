using System.Reflection;

namespace AgenticWorkforce.Api.Core.Extensions;

/// <summary>
/// Plugin-style endpoint discovery (Principle 12). Each vertical slice in
/// <c>Features/</c> is a <c>static</c> class with a
/// <c>public static void MapEndpoints(IEndpointRouteBuilder app)</c> method;
/// adding a new endpoint requires creating that file and nothing else.
/// <para>
/// The previous incarnation of this file was a 245-line hand-maintained
/// registry that had to be edited every time a slice was added — exactly the
/// "core change required to add a plugin" anti-pattern P12 forbids. The
/// reflection scan runs once at startup; the discovered set is fixed for the
/// process lifetime, so the cost is paid once.
/// </para>
/// </summary>
public static class EndpointRegistrationExtensions
{
    private const string FeaturesNamespacePrefix = "AgenticWorkforce.Api.Features.";
    private const string MapEndpointsMethodName  = "MapEndpoints";

    public static void MapFeatureSlices(this IEndpointRouteBuilder app)
    {
        var assembly = typeof(EndpointRegistrationExtensions).Assembly;
        var sliceMethods = DiscoverSliceMethods(assembly);

        foreach (var method in sliceMethods)
            method.Invoke(null, [app]);
    }

    /// <summary>
    /// Returns the <c>MapEndpoints</c> method on every static class under the
    /// <c>Features</c> namespace. Ordering follows assembly metadata order,
    /// which is sufficient because ASP.NET routing resolves by pattern, not
    /// by registration order. Exposed internal so an architecture test can
    /// verify each slice file contributes exactly one entry.
    /// </summary>
    internal static IReadOnlyList<MethodInfo> DiscoverSliceMethods(Assembly assembly)
    {
        var found = new List<MethodInfo>();
        foreach (var type in assembly.GetTypes())
        {
            if (type.Namespace?.StartsWith(FeaturesNamespacePrefix, StringComparison.Ordinal) != true)
                continue;
            // Static classes in C# are emitted as `abstract sealed`.
            if (!(type.IsClass && type.IsAbstract && type.IsSealed))
                continue;

            var method = type.GetMethod(
                MapEndpointsMethodName,
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(IEndpointRouteBuilder)],
                modifiers: null);
            if (method is not null)
                found.Add(method);
        }
        return found;
    }
}
