using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Maui.Handlers;
using static Microsoft.UI.Reactor.Maui.Factories;

namespace Microsoft.UI.Reactor.Maui.Tests;

/// <summary>The README's flyout and toolbar snippets (spec 070 §4.14), as written there.</summary>
public class ReadmeAuditSnippetTests
{
    public sealed class MailApp : Component
    {
        public override Element Render()
        {
            var (open, setOpen) = UseState(false);
            return Flyout(
                Page("Folders", VStack(Button("Inbox", () => setOpen(false)))),
                NavigationStack([Page("Inbox", Label("No new mail"))]),
                open, setOpen) with { LayoutBehavior = FlyoutLayoutBehavior.Split };
        }
    }

    [Fact]
    public void TheSplitFlyoutSnippet_MountsSplit_AndPresented_WithoutThrowing()
    {
        // Headless MAUI isn't a phone, so Split splits here as on an iPad or a Mac.
        var host = new TestHost(MauiHandlers.CreateRegistry()).Render(Component<MailApp>());
        host.Scheduler.Drain();
        var flyout = host.View<ReactorFlyoutPage>();
        Assert.True(flyout.IsSplit);
        Assert.True(flyout.IsPresented);
    }

    [Fact]
    public void TheToolbarSnippet_ShowsThePrimaryAndTheSecondaryItem_InOrder()
    {
        var log = new List<string>();
        Action compose = () => log.Add("compose"), openSettings = () => log.Add("settings");
        var page = Page("Inbox", Label("No new mail")) with
        {
            ToolbarItems =
            [
                new ToolbarAction("Compose", compose),
                new ToolbarAction("Settings", openSettings) { Order = ToolbarItemOrder.Secondary },
            ],
        };
        var native = new TestHost(MauiHandlers.CreateRegistry()).Render(page).View<ContentPage>();
        Assert.Equal([("Compose", ToolbarItemOrder.Default), ("Settings", ToolbarItemOrder.Secondary)], native.ToolbarItems.Select(i => (i.Text, i.Order)));
        ((IMenuItemController)native.ToolbarItems[1]).Activate();
        Assert.Equal(["settings"], log);
    }
}
