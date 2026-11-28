namespace Microsoft.UI.Reactor.Maui.Tests;

// The properties still waiting for executable evidence (spec 070 §4.8, §4.14.2), each with the phase
// that owns it. It started as CensusBaseline.Original; 4c-3a, 4c-3b and 4c-3c replaced their entries with
// matrix cases and covered-by claims. It may only shrink: every entry is a key of its phase's frozen
// list (CensusBaseline.Phases), and the ceiling is lowered with every removal.
public static partial class CensusBaseline
{
    /// <summary>The list's size: lowered with every entry removed, never raised.</summary>
    public const int Ceiling = 0;

    public static readonly (string Key, string Phase)[] Pending = [];
}
