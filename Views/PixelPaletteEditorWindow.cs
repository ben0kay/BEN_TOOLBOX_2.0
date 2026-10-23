using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BEN_TOOLBOX_2_0.Services;

namespace BEN_TOOLBOX_2_0.Views;

// Edits a private copy. Live callbacks publish snapshots; built-in palettes never change.
public sealed class PixelPaletteEditorWindow : Window
{
    public PixelPaletteEditorWindow(string[] initial, Action<string[]> changed, Action<string, string[]> saved)
    {
        var colours = PixelPalettePresets.Validate(initial);
        Title = "Custom Palette / edit a copy"; Width = 650; Height = 620; MinWidth = 480; MinHeight = 450;
        Background = Brush.Parse("#091522"); Foreground = Brush.Parse("#E8F4FF"); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        int selected = 0; bool loading = false;
        var swatches = new WrapPanel { ItemSpacing = 5, LineSpacing = 5 };
        var hex = new TextBox { Width = 130 };
        var name = new TextBox { Watermark = "Preset name, e.g. Ben Forest 32" };
        var message = new TextBlock { Text = "Select a swatch, then adjust RGB or enter a hex colour. Changes preview live.", TextWrapping = TextWrapping.Wrap };
        var selectedLabel = new TextBlock();
        var red = new Slider { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true };
        var green = new Slider { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true };
        var blue = new Slider { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true };
        var body = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        body.Children.Add(new TextBlock { Text = "CUSTOM PALETTE", FontSize = 23 });
        body.Children.Add(message); body.Children.Add(swatches); body.Children.Add(selectedLabel);
        body.Children.Add(new TextBlock { Text = "RED" }); body.Children.Add(red);
        body.Children.Add(new TextBlock { Text = "GREEN" }); body.Children.Add(green);
        body.Children.Add(new TextBlock { Text = "BLUE" }); body.Children.Add(blue);
        var apply = new Button { Content = "APPLY HEX" };
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { hex, apply } });
        body.Children.Add(name);
        var save = new Button { Content = "SAVE AS NEW PRESET" };
        body.Children.Add(save); Content = new ScrollViewer { Content = body };
        void Rebuild()
        {
            swatches.Children.Clear();
            for (int i = 0; i < colours.Length; i++)
            {
                int slot = i;
                var swatch = new Button { Width = 36, Height = 30, Padding = new Thickness(0), Background = Brush.Parse(colours[i]), BorderBrush = i == selected ? Brushes.White : Brushes.Gray, BorderThickness = new Thickness(i == selected ? 3 : 1) };
                ToolTip.SetTip(swatch, $"{i+1}: {colours[i]}");
                swatch.Click += (_, _) => { selected = slot; Load(); Rebuild(); };
                swatches.Children.Add(swatch);
            }
        }
        void Load()
        {
            loading = true;
            var colour = Color.Parse(colours[selected]);
            red.Value = colour.R; green.Value = colour.G; blue.Value = colour.B;
            hex.Text = colours[selected]; selectedLabel.Text = $"Colour {selected+1} / {colours.Length} — RGB {colour.R}, {colour.G}, {colour.B}";
            loading = false;
        }
        void Publish()
        {
            changed(colours.ToArray()); Load(); Rebuild();
            message.Text = "Live draft applied to the queue. Save a named preset to keep it after closing Toolbox.";
        }
        foreach (var slider in new[] { red, green, blue })
            slider.PropertyChanged += (_, e) =>
            {
                if (loading || e.Property != Slider.ValueProperty) return;
                colours[selected] = $"#{(int)red.Value:X2}{(int)green.Value:X2}{(int)blue.Value:X2}"; Publish();
            };
        apply.Click += (_, _) =>
        {
            try { colours[selected] = PixelPalettePresets.Validate(new[] { (hex.Text ?? "").Trim() })[0]; Publish(); }
            catch (Exception ex) { message.Text = ex.Message; }
        };
        save.Click += (_, _) =>
        {
            try
            {
                var presetName = (name.Text ?? "").Trim();
                PixelPalettePresets.SaveNew(presetName, colours);
                saved(presetName, colours.ToArray()); message.Text = "Saved: " + presetName;
            }
            catch (Exception ex) { message.Text = "Save failed: " + ex.Message; }
        };
        Load(); Rebuild();
    }
}
