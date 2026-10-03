using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Core;
using DynamicIsland.Core.Settings;

namespace DynamicIsland;

/// <summary>Sidebar of settings sections; each page is rendered from the section's <see cref="SettingItem"/>s.</summary>
public partial class SettingsWindow : Window
{
    private static int _lastPage;

    private readonly SettingsStore _store;
    private List<SettingItem> _items = [];

    public SettingsWindow(SettingsRegistry registry, SettingsStore store)
    {
        InitializeComponent();
        _store = store;
        Nav.ItemsSource = registry.Sections;
        Nav.SelectedIndex = Math.Clamp(_lastPage, 0, registry.Sections.Count - 1);

        // A setting may change elsewhere (quick panel) while this page is open.
        _store.Changed += OnStoreChanged;
        Closed += (_, _) => _store.Changed -= OnStoreChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (NativeMethods.ApplyDarkMica(hwnd))
        {
            Background = Brushes.Transparent;
            HwndSource.FromHwnd(hwnd)!.CompositionTarget.BackgroundColor = Colors.Transparent;
        }
    }

    private void OnStoreChanged(string _)
    {
        foreach (var item in _items) item.Refresh();
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not ISettingsSection section) return;
        _lastPage = Nav.SelectedIndex;

        PageTitle.Text = section.Title;
        _items = section.Build().ToList();
        Page.Children.Clear();
        foreach (var item in _items) Page.Children.Add(CreateCard(item));
        AnimatePageIn();
    }

    private static Border CreateCard(SettingItem item)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = item.Label, FontSize = 14 });
        if (!string.IsNullOrEmpty(item.Description))
        {
            text.Children.Add(new TextBlock
            {
                Text = item.Description,
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
            });
        }

        var editor = item.CreateEditor();
        var grid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
            RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto } },
        };
        grid.Children.Add(text);
        if (item.WideEditor)
        {
            editor.Margin = new Thickness(0, 12, 0, 0);
            Grid.SetRow(editor, 1);
            Grid.SetColumnSpan(editor, 2);
        }
        else
        {
            editor.Margin = new Thickness(24, 0, 0, 0);
            Grid.SetColumn(editor, 1);
        }
        grid.Children.Add(editor);

        return new Border
        {
            Child = grid,
            MinHeight = 64,
            Padding = new Thickness(18, 12, 18, 12),
            Margin = new Thickness(0, 0, 0, 4),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
        };
    }

    private void AnimatePageIn()
    {
        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        PageShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, duration) { EasingFunction = ease });
    }
}
