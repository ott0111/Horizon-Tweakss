using System.Windows;
using System.Windows.Controls;

namespace Horizon.App.Views;

/// <summary>
/// Keeps a small number of recently visited page visuals alive. WPF otherwise
/// rebuilds an implicit DataTemplate every time ContentControl navigation returns
/// to a page, which is noticeable on catalogue and system-information screens.
/// </summary>
public sealed class CachedPageHost : ContentControl
{
    private const int Capacity = 4;
    private readonly Dictionary<object, FrameworkElement> _views = new(ReferenceEqualityComparer.Instance);
    private readonly LinkedList<object> _recency = [];

    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page), typeof(object), typeof(CachedPageHost), new PropertyMetadata(null, OnPageChanged));

    public object? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    private static void OnPageChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((CachedPageHost)sender).ShowPage(args.NewValue);

    private void ShowPage(object? page)
    {
        if (page is null)
        {
            Content = null;
            return;
        }

        if (!_views.TryGetValue(page, out var view))
        {
            var key = new DataTemplateKey(page.GetType());
            var template = TryFindResource(key) as DataTemplate ?? Application.Current.TryFindResource(key) as DataTemplate;
            view = template?.LoadContent() as FrameworkElement
                ?? throw new InvalidOperationException($"No page template is registered for {page.GetType().Name}.");
            view.DataContext = page;
            _views.Add(page, view);
        }

        Touch(page);
        Content = view;
        TrimCache(page);
    }

    private void Touch(object page)
    {
        var node = _recency.Find(page);
        if (node is not null) _recency.Remove(node);
        _recency.AddFirst(page);
    }

    private void TrimCache(object currentPage)
    {
        while (_views.Count > Capacity && _recency.Last is { } oldest)
        {
            if (ReferenceEquals(oldest.Value, currentPage)) return;
            _recency.RemoveLast();
            _views.Remove(oldest.Value);
        }
    }
}
