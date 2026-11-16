using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Notifications;
using SiloPlayer.Views;

internal static class NotificationFixtureUi
{
    internal static Border[] Rows(NotificationsPage page) => Descendants((DependencyObject)page.FindName("InboxGroupsHost"))
        .OfType<Border>().Where(row => row.Name == "NotificationRowRoot" && row.DataContext is NotificationInboxEntry).ToArray();

    internal static Border Row(NotificationsPage page, string id) => Rows(page)
        .Single(row => ((NotificationInboxEntry)row.DataContext).Notifications.Any(item => item.Id == id));

    internal static T Named<T>(DependencyObject row, string name) where T : FrameworkElement
        => Descendants(row).OfType<T>().Single(element => element.Name == name);

    internal static Button Link(Border row) => Descendants(row).OfType<Button>()
        .Single(button => button.Name != "InlineMarkReadButton");

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    internal static async Task DetachAsync(FrameworkElement root, Action detach)
    {
        var elements = new[] { root }.Concat(Descendants(root).OfType<Page>())
            .Concat(root is Frame { Content: FrameworkElement page } ? new[] { page } : Array.Empty<FrameworkElement>())
            .Where(element => element.IsLoaded).Distinct().ToArray();
        var barriers = new List<(FrameworkElement Element, RoutedEventHandler Handler, Task Completion)>();
        foreach (var element in elements)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            RoutedEventHandler handler = (_, _) => completion.TrySetResult();
            element.Unloaded += handler; barriers.Add((element, handler, completion.Task));
        }
        try
        {
            detach();
            await Task.WhenAll(barriers.Select(barrier => barrier.Completion)).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { foreach (var barrier in barriers) barrier.Element.Unloaded -= barrier.Handler; }
    }
}
