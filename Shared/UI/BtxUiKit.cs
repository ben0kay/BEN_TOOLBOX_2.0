using System;
using Avalonia.Controls;

namespace BEN_TOOLBOX_2_0.Shared.UI;

/// <summary>
/// Opt-in factory for code-built Avalonia windows. Styling lives in Shared/UI/Styles,
/// and AXAML users can apply the same classes directly.
/// </summary>
public static class BtxUiKit
{
    public enum ButtonKind { Primary, Secondary, Danger, Quiet }

    public static Button CreateButton(string text, ButtonKind kind = ButtonKind.Secondary)
    {
        var button = new Button { Content = text };
        ApplyButtonStyle(button, kind);
        return button;
    }

    /// <summary>Restyles an existing button without replacing Command or Click handlers.</summary>
    public static void ApplyButtonStyle(Button button, ButtonKind kind = ButtonKind.Secondary)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.Classes.Add("btx-button");
        button.Classes.Remove("btx-primary");
        button.Classes.Remove("btx-secondary");
        button.Classes.Remove("btx-danger");
        button.Classes.Remove("btx-quiet");
        button.Classes.Add(kind switch
        {
            ButtonKind.Primary => "btx-primary",
            ButtonKind.Danger => "btx-danger",
            ButtonKind.Quiet => "btx-quiet",
            _ => "btx-secondary"
        });
    }

    public static Border CreatePanel(Control content, bool emphasized = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        var panel = new Border { Child = content };
        panel.Classes.Add("btx-panel");
        if (emphasized) panel.Classes.Add("btx-panel-active");
        return panel;
    }

    public static StackPanel CreateHeader(string kicker, string title, string? subtitle = null)
    {
        var result = new StackPanel { Spacing = 5 };
        result.Children.Add(Text(kicker, "btx-kicker"));
        result.Children.Add(Text(title, "btx-heading"));
        if (!string.IsNullOrWhiteSpace(subtitle))
            result.Children.Add(Text(subtitle, "btx-muted"));
        return result;
    }

    public static StackPanel CreateField(string label, Control content, string? hint = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        var result = new StackPanel { Spacing = 6 };
        result.Children.Add(Text(label, "btx-field-label"));
        result.Children.Add(content);
        if (!string.IsNullOrWhiteSpace(hint))
            result.Children.Add(Text(hint, "btx-muted"));
        return result;
    }

    private static TextBlock Text(string value, string className)
    {
        var block = new TextBlock { Text = value, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        block.Classes.Add(className);
        return block;
    }
}
