using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace NcaafPickEm.Web.Services;

/// <summary>
/// Whether the page on screen is the one the app was opened on: a launch from the home screen, a
/// typed URL, or a reload, as opposed to anywhere the member navigated to inside the app since.
/// </summary>
/// <remarks>
/// The league picker uses it to open a member's only league straight away when the app is
/// entered (D-187), while a later tap back to <c>/</c> still shows the picker with its "Join by
/// code" box. Registered scoped (one per app session in WebAssembly) and injected by
/// <c>MainLayout</c>, which every page renders inside, so it is listening before the first
/// in-app navigation can happen.
/// </remarks>
public sealed class AppEntry : IDisposable
{
    private readonly NavigationManager _navigation;

    /// <summary>Starts watching for the first in-app navigation.</summary>
    /// <param name="navigation">The app's navigation manager.</param>
    public AppEntry(NavigationManager navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);

        _navigation = navigation;
        _navigation.LocationChanged += OnLocationChanged;
    }

    /// <summary>True until the member (or the app) navigates anywhere in-app.</summary>
    public bool IsEntryPage { get; private set; } = true;

    /// <inheritdoc />
    public void Dispose() => _navigation.LocationChanged -= OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        IsEntryPage = false;
        _navigation.LocationChanged -= OnLocationChanged;
    }
}
