namespace Dispatch.UI.Controls;

/// <summary>One button on the controller map: where it sits and how big it is.</summary>
/// <param name="Token">Canonical controller token this button represents.</param>
/// <param name="Label">Short glyph printed on it.</param>
/// <param name="Cx">Centre X, in the layout's coordinate space.</param>
/// <param name="Cy">Centre Y.</param>
/// <param name="R">Radius, for a round button.</param>
/// <param name="Rect">True for a rounded rectangle (bumpers and triggers).</param>
/// <param name="W">Width, for a rectangular button.</param>
/// <param name="H">Height, for a rectangular button.</param>
public sealed record PadButton(
    string Token,
    string Label,
    double Cx,
    double Cy,
    double R,
    bool Rect = false,
    double W = 0,
    double H = 0);

/// <summary>
/// A gamepad drawn top-down, expressed in a fixed coordinate space the control
/// scales to fit.
/// </summary>
/// <remarks>
/// The controller is the keyboard map's counterpart: LSPDFR binds the D-pad, the
/// face buttons, both bumpers, both triggers and the stick clicks, and every one
/// needs somewhere to light up and be clicked. Tokens are the canonical controller
/// forms, so this is also the definitive list of what the controller map can show.
/// </remarks>
public static class ControllerLayout
{
    /// <summary>Width of the layout space.</summary>
    public const double Width = 470;

    /// <summary>Height of the layout space.</summary>
    public const double Height = 300;

    /// <summary>The controller body outline.</summary>
    public const string Body =
        "M70 74 Q46 62 40 116 Q28 214 78 240 Q118 262 158 236 L312 236 " +
        "Q352 262 392 240 Q442 214 430 116 Q424 62 400 74 Q350 88 280 88 L190 88 Q120 88 70 74 Z";

    /// <summary>Every button, with its position.</summary>
    public static readonly IReadOnlyList<PadButton> Buttons =
    [
        new("LeftThumb", "LS", 116, 122, 26),
        new("RightThumb", "RS", 300, 192, 26),

        new("DPadUp", "▲", 182, 166, 15),
        new("DPadDown", "▼", 182, 216, 15),
        new("DPadLeft", "◀", 155, 191, 15),
        new("DPadRight", "▶", 209, 191, 15),

        new("PadY", "Y", 374, 102, 18),
        new("PadA", "A", 374, 166, 18),
        new("PadX", "X", 346, 134, 18),
        new("PadB", "B", 402, 134, 18),

        new("PadBack", "◇", 212, 122, 12),
        new("PadStart", "≡", 266, 122, 12),

        new("LeftShoulder", "LB", 80, 48, 0, Rect: true, W: 80, H: 22),
        new("RightShoulder", "RB", 322, 48, 0, Rect: true, W: 80, H: 22),
        new("LeftTrigger", "LT", 86, 20, 0, Rect: true, W: 66, H: 18),
        new("RightTrigger", "RT", 326, 20, 0, Rect: true, W: 66, H: 18),
    ];

    /// <summary>The PlayStation glyph for a face button, or null for the other buttons.</summary>
    public static string? PlayStationGlyph(string token) => token switch
    {
        "PadA" => "✕", // cross
        "PadB" => "○", // circle
        "PadX" => "□", // square
        "PadY" => "△", // triangle
        _ => null,
    };
}
