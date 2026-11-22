using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// One of a set of buttons of which one is selected (a Libraries tab, a Catalog Health tile; Radix <c>role="tab"</c> with <c>aria-selected</c>). Besides invoking,
/// it exposes the selection to assistive technology through UI Automation's SelectionItem pattern, so a screen reader says "selected" for the current one
/// and a UI Automation client can read and set it; the item status text is not used for this.
/// </summary>
internal sealed class SelectableButton : Button
{
    /// <summary>The set's container for UI Automation (the tab row, the tile grid); optional.</summary>
    internal UIElement? SelectionContainer { get; set; }

    /// <summary>Whether this is the selected one of its set (the page sets it as the selection changes).</summary>
    internal bool IsSelected { get; set; }

    /// <summary>What the page does when automation selects this one.</summary>
    internal Action? Choose { get; set; }

    /// <summary>The role reported to automation (a tab by default).</summary>
    internal AutomationControlType Role { get; set; } = AutomationControlType.TabItem;

    internal void ChangeSelection(bool selected)
    {
        if (IsSelected == selected) return;
        IsSelected = selected;
        // Tell clients the selection changed (no-op until a client listens).
        if (FrameworkElementAutomationPeer.FromElement(this) is SelectableButtonPeer peer) peer.RaiseSelectionChanged(!selected, selected);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SelectableButtonPeer(this);
}

internal sealed class SelectableButtonPeer(SelectableButton owner) : ButtonAutomationPeer(owner), ISelectionItemProvider
{
    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.SelectionItem ? this : base.GetPatternCore(patternInterface);

    protected override AutomationControlType GetAutomationControlTypeCore() => owner.Role;

    public bool IsSelected => owner.IsSelected;

    public IRawElementProviderSimple SelectionContainer =>
        owner.SelectionContainer is { } container && CreatePeerForElement(container) is { } peer ? ProviderFromPeer(peer) : null!;

    public void AddToSelection() => Select();

    // A set that always has one selected (Radix tabs): another item's selection is what clears this one.
    public void RemoveFromSelection() { }

    public void Select()
    {
        if (!owner.IsEnabled) throw new ElementNotEnabledException();
        if (!owner.IsSelected) owner.Choose?.Invoke();
    }

    internal void RaiseSelectionChanged(bool oldValue, bool newValue) =>
        RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, oldValue, newValue);
}
