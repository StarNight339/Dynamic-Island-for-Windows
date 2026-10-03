using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace DynamicIsland.Core.Settings;

/// <summary>
/// One row on a settings page. Sections describe their settings with these; the settings window and the
/// quick panel render them, so a new system never needs its own settings XAML.
/// Editors bind to <c>Value</c>, so <see cref="Refresh"/> re-reads the model without rebuilding the page.
/// </summary>
public abstract class SettingItem : INotifyPropertyChanged
{
    public required string Label { get; init; }
    public string? Description { get; init; }

    /// <summary>Segoe Fluent glyph shown next to the label in the quick panel.</summary>
    public string? Icon { get; init; }

    /// <summary>Also offer this setting in the right-click quick panel (toggles only).</summary>
    public bool Quick { get; init; }

    /// <summary>Editor goes on its own line under the label instead of on the right.</summary>
    public virtual bool WideEditor => false;

    public event PropertyChangedEventHandler? PropertyChanged;

    public abstract FrameworkElement CreateEditor();

    /// <summary>Re-reads the value, e.g. after the same setting changed somewhere else.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected Binding Bind(string path, BindingMode mode = BindingMode.TwoWay) => new(path) { Source = this, Mode = mode };
}

public sealed class ToggleItem : SettingItem
{
    public required Func<bool> Get { get; init; }
    public required Action<bool> Set { get; init; }

    public bool Value
    {
        get => Get();
        set { Set(value); OnPropertyChanged(nameof(Value)); }
    }

    public override FrameworkElement CreateEditor()
    {
        var toggle = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Focusable = false };
        toggle.SetResourceReference(FrameworkElement.StyleProperty, "ToggleSwitch");
        toggle.SetBinding(ToggleButton.IsCheckedProperty, Bind(nameof(Value)));
        return toggle;
    }
}

/// <summary>A number edited with a slider, or with a text box when the range is too wide for one (ports).</summary>
public sealed class NumberItem : SettingItem
{
    public required Func<int> Get { get; init; }
    public required Action<int> Set { get; init; }
    public int Min { get; init; }
    public int Max { get; init; } = 100;
    public int Step { get; init; } = 1;
    public string Unit { get; init; } = "";
    public bool UseTextBox { get; init; }

    public int Value
    {
        get => Get();
        set
        {
            var clamped = Math.Clamp(value, Min, Max);
            if (clamped != Get()) Set(clamped);
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(Display));
        }
    }

    public string Display => string.IsNullOrEmpty(Unit) ? Value.ToString() : $"{Value} {Unit}";

    public override FrameworkElement CreateEditor()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (UseTextBox)
        {
            var box = new TextBox { Width = 90 };
            box.SetResourceReference(FrameworkElement.StyleProperty, "SettingsTextBox");
            box.SetBinding(TextBox.TextProperty,
                new Binding(nameof(Value)) { Source = this, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            };
            panel.Children.Add(box);
            return panel;
        }

        var slider = new Slider
        {
            Width = 200,
            Minimum = Min,
            Maximum = Max,
            SmallChange = Step,
            LargeChange = Step,
            TickFrequency = Step,
            IsSnapToTickEnabled = true,
            Focusable = false,
        };
        slider.SetResourceReference(FrameworkElement.StyleProperty, "SettingsSlider");
        slider.SetBinding(RangeBase.ValueProperty, Bind(nameof(Value)));
        var label = new TextBlock { MinWidth = 64, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        label.SetBinding(TextBlock.TextProperty, Bind(nameof(Display), BindingMode.OneWay));
        panel.Children.Add(slider);
        panel.Children.Add(label);
        return panel;
    }
}

/// <summary>An editable set of numbers shown as removable chips (timer presets, battery warning levels).</summary>
public sealed class NumberListItem : SettingItem
{
    public required Func<List<int>> Get { get; init; }

    /// <summary>Called after the list was modified in place.</summary>
    public required Action Saved { get; init; }

    public int Min { get; init; } = 1;
    public int Max { get; init; } = 100;
    public int MaxCount { get; init; } = 6;
    public Func<int, string> Format { get; init; } = v => v.ToString();

    public override bool WideEditor => true;

    public override FrameworkElement CreateEditor()
    {
        var panel = new WrapPanel();
        Render(panel);
        PropertyChanged += (_, _) => Render(panel);
        return panel;
    }

    private void Render(WrapPanel panel)
    {
        panel.Children.Clear();
        foreach (var value in Get())
        {
            var remove = new Button { Content = "\uE711", ToolTip = "Remove" };
            remove.SetResourceReference(FrameworkElement.StyleProperty, "ChipRemoveButton");
            remove.Click += (_, _) => Modify(list => list.Remove(value));

            var chip = new Border { Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { new TextBlock { Text = Format(value), VerticalAlignment = VerticalAlignment.Center }, remove },
            } };
            chip.SetResourceReference(FrameworkElement.StyleProperty, "ValueChip");
            panel.Children.Add(chip);
        }

        if (Get().Count >= MaxCount) return;

        var box = new TextBox
        {
            Width = 64,
            Height = 30,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 6, 6),
            ToolTip = $"{Min}–{Max}",
        };
        box.SetResourceReference(FrameworkElement.StyleProperty, "SettingsTextBox");
        var add = new Button { Content = "\uE710", ToolTip = "Add", Margin = new Thickness(0, 0, 0, 6) };
        add.SetResourceReference(FrameworkElement.StyleProperty, "SettingsIconButton");

        void Add()
        {
            if (!int.TryParse(box.Text.Trim(), out var v)) return;
            v = Math.Clamp(v, Min, Max);
            Modify(list => { if (!list.Contains(v)) list.Add(v); });
        }
        add.Click += (_, _) => Add();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Add(); };
        panel.Children.Add(box);
        panel.Children.Add(add);
    }

    private void Modify(Action<List<int>> change)
    {
        var list = Get();
        change(list);
        list.Sort();
        Saved();
        Refresh();
    }
}

public sealed class ActionItem : SettingItem
{
    public required string ButtonText { get; init; }
    public required Action Run { get; init; }

    public override FrameworkElement CreateEditor()
    {
        var button = new Button { Content = ButtonText, VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(FrameworkElement.StyleProperty, "SettingsButton");
        button.Click += (_, _) => Run();
        return button;
    }
}

/// <summary>Read-only, selectable text (URLs, paths, versions).</summary>
public sealed class InfoItem : SettingItem
{
    public required Func<string> Get { get; init; }

    public string Value => Get();

    public override FrameworkElement CreateEditor()
    {
        var text = new TextBox { IsReadOnly = true, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(FrameworkElement.StyleProperty, "InfoText");
        text.SetBinding(TextBox.TextProperty, Bind(nameof(Value), BindingMode.OneWay));
        return text;
    }
}

/// <summary>Escape hatch for a setting the other item types can't express.</summary>
public sealed class CustomItem : SettingItem
{
    public required Func<FrameworkElement> Build { get; init; }
    public bool Wide { get; init; }
    public override bool WideEditor => Wide;
    public override FrameworkElement CreateEditor() => Build();
}
