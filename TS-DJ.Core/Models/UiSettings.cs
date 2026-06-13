namespace TS_DJ.Core.Models;

public sealed class UiSettings
{
    public const string WidthKey = "ui.window.width";
    public const string HeightKey = "ui.window.height";
    public const string XKey = "ui.window.x";
    public const string YKey = "ui.window.y";
    public const string WindowStateKey = "ui.window.state";

    public double? Width { get; set; }
    public double? Height { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public string WindowState { get; set; } = "Normal";
}
