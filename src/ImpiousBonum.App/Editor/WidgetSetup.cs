using System.Windows;

namespace ImpiousBonum.App.Editor;

/// <summary>
/// Something a widget needs set up outside the layout (a weather location, calendars, news feeds), offered as a button
/// in the editor when the widget is selected. The tray has the same, for when the editor isn't open.
/// </summary>
/// <param name="Button">The button's text, e.g. "Change location…".</param>
/// <param name="Status">What's set up now, e.g. "Forecasts for Leeds"; asked again after the button is used.</param>
/// <param name="Open">Opens the setup over the editor.</param>
public sealed record WidgetSetup(string Button, Func<string> Status, Action<Window> Open);
