using Avalonia.Controls.Primitives;

namespace Dispatch.UI.Controls;

/// <summary>
/// The concentric radio-wave motif, pulsing outward, for scanning and loading
/// states.
/// </summary>
/// <remarks>
/// The arcs are the same geometry as the app mark's radio wave; here they ripple
/// outward on a loop to say "working" without a spinner. The animation is in the
/// control theme, keyed off the shared motion tokens, so it stops under reduced
/// motion along with everything else.
/// </remarks>
public sealed class RadioWavePulse : TemplatedControl;
