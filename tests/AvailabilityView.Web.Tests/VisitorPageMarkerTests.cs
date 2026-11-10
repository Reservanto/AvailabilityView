using System.Reflection;
using AvailabilityView.Web.Availability;
using Microsoft.AspNetCore.Mvc;

namespace AvailabilityView.Web.Tests;

/// <summary>
/// Označení stránky návštěvníka řídí jazyk z adresy, vložení do iframe a vlastní CSS.
/// Průvodce ani generátor ho mít nesmí: vložení do cizí stránky by z nich udělalo cíl clickjackingu.
/// </summary>
public sealed class VisitorPageMarkerTests
{
    [Fact]
    public void OnlyVisitorController_IsMarked()
    {
        Type[] marked = [.. typeof(VisitorController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && type.GetCustomAttribute<VisitorPageAttribute>() is not null)];

        Assert.Equal([typeof(VisitorController)], marked);
    }

    [Fact]
    public void ActionsCannotBeMarkedOneByOne()
    {
        // Označení je jen na třídě, celý kontrolér je buď stránka návštěvníka, nebo ne.
        Assert.Equal(AttributeTargets.Class, typeof(VisitorPageAttribute).GetCustomAttribute<AttributeUsageAttribute>()!.ValidOn);
    }
}
