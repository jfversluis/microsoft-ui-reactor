using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core.Diagnostics;

namespace Microsoft.UI.Reactor.Maui.Handlers;

/// <summary>
/// MAUI lets an automation id be set once, so Reactor writes a page's or view's at mount only; a changed
/// declaration is reported once per native (spec 070 §4.16.4 item 4).
/// </summary>
internal static class AutomationIds
{
    private static readonly ConditionalWeakTable<object, object> Warned = new();

    public static void WarnIfChanged(object native, string? previous, string? next, string operation)
    {
        if (previous == next || !Warned.TryAdd(native, native)) return;
        DiagnosticLog.Warning(LogCategory.Reactor, operation,
            "An AutomationId changed after mount; MAUI sets an automation id once, so the view keeps the one it was mounted with. Give it a new key to mount it with the new id.");
    }
}
