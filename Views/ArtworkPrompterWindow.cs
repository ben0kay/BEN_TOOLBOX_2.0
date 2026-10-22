using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BEN_TOOLBOX_2_0.Services;

namespace BEN_TOOLBOX_2_0.Views;

// Dedicated fantasy artwork planner. It composes prompts; it does not write Godot resources.
public sealed class ArtworkPrompterWindow : Window
{
    private readonly ComboBox style = new() { ItemsSource = ArtworkSiblingPromptService.Styles, SelectedIndex = 0 };
    private readonly ComboBox category = new() { ItemsSource = ArtworkSiblingPromptService.Categories, SelectedIndex = 0 };
    private readonly TextBox subject = new();
    private readonly TextBox shape = new();
    private readonly TextBox material = new();
    private readonly TextBox details = new();
    private readonly TextBox colours = new();
    private readonly TextBox notes = new() { Watermark = "Optional art-direction notes", AcceptsReturn = true, Height = 66 };
    private readonly CheckBox transparent = new() { Content = "Transparent background", IsChecked = true };
    private readonly CheckBox noShadow = new() { Content = "No cast shadow", IsChecked = true };
    private readonly CheckBox generateSibling = new() { Content = "Generate matching sibling artwork", IsChecked = true };
    private readonly ComboBox siblingVariant = new() { MinWidth = 225 };
    private readonly CheckBox preserveStructure = new() { Content = "Preserve identity, scale and core structure", IsChecked = true };
    private readonly CheckBox preserveTrunkRoots = new() { Content = "Keep main trunk and roots intact", IsChecked = true };
    private readonly TextBox siblingNotes = new() { Watermark = "Optional extra instructions for the sibling", AcceptsReturn = true, Height = 64 };
    private readonly TextBlock presetDescription = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#B1D4DF") };
    private readonly TextBox mainOutput = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 275 };
    private readonly TextBox siblingOutput = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 275 };
    private readonly Button copyMain = new() { Content = "COPY MAIN" };
    private readonly Button copySibling = new() { Content = "COPY SIBLING" };
    private readonly Button copyBoth = new() { Content = "COPY BOTH" };
    private readonly TextBlock status = new() { Text = "Ready", TextWrapping = TextWrapping.Wrap };
    private ArtworkSiblingPromptResult? latest;

    public ArtworkPrompterWindow()
    {
        Title = "Artwork Design Prompter / Fantasy + Siblings";
        Width = 1200;
        Height = 900;
        MinWidth = 800;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#091522");
        Foreground = Brush.Parse("#E8F4FF");

        var body = new StackPanel { Margin = new Thickness(22), Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            Text = "ARTWORK DESIGN PROMPTER / FANTASY ASSETS",
            FontSize = 23, FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#86E5F6")
        });
        body.Children.Add(new TextBlock
        {
            Text = "Design your main sprite and an optional matching sibling (charred, dead, snowy, wet, and more). Prompts only; no artwork or game resources are changed.",
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(TwoColumns(
            Field("ART DIRECTION", style),
            Field("ASSET CATEGORY", category)));

        var example = new Button { Content = "LOAD CATEGORY EXAMPLE", HorizontalAlignment = HorizontalAlignment.Left };
        example.Click += (_, _) => { LoadCategoryExample(); GeneratePrompts(); };
        body.Children.Add(example);
        foreach (var pair in new[]
        {
            ("SUBJECT", subject), ("SHAPE", shape), ("MATERIAL", material),
            ("DETAILS", details), ("COLOURS", colours), ("NOTES", notes)
        })
            body.Children.Add(Field(pair.Item1, pair.Item2));

        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        options.Children.Add(transparent);
        options.Children.Add(noShadow);
        body.Children.Add(options);

        body.Children.Add(new TextBlock
        {
            Text = "SIBLING ARTWORK",
            FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#86E5F6"),
            Margin = new Thickness(0, 8, 0, 0)
        });
        body.Children.Add(generateSibling);
        body.Children.Add(Field("SIBLING TREATMENT", siblingVariant));
        body.Children.Add(presetDescription);
        body.Children.Add(preserveStructure);
        body.Children.Add(preserveTrunkRoots);
        body.Children.Add(Field("VARIANT-SPECIFIC NOTES", siblingNotes));
        body.Children.Add(TwoColumns(
            Field("MAIN ARTWORK PROMPT", mainOutput),
            Field("SIBLING ARTWORK PROMPT", siblingOutput)));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var generate = new Button { Content = "GENERATE PROMPTS" };
        generate.Click += (_, _) => GeneratePrompts();
        actions.Children.Add(generate);
        actions.Children.Add(copyMain);
        actions.Children.Add(copySibling);
        actions.Children.Add(copyBoth);
        body.Children.Add(actions);
        body.Children.Add(status);
        Content = new ScrollViewer { Content = body };

        category.SelectionChanged += (_, _) =>
        {
            UpdateVariantChoices();
            // Switching categories never silently overwrites the edited main artwork fields.
            GeneratePrompts();
        };
        siblingVariant.SelectionChanged += (_, _) => { UpdateVariantHelp(); GeneratePrompts(); };
        style.SelectionChanged += (_, _) => GeneratePrompts();
        generateSibling.Checked += (_, _) => { UpdateSiblingControls(); GeneratePrompts(); };
        generateSibling.Unchecked += (_, _) => { UpdateSiblingControls(); GeneratePrompts(); };
        transparent.Checked += (_, _) => GeneratePrompts();
        transparent.Unchecked += (_, _) => GeneratePrompts();
        noShadow.Checked += (_, _) => GeneratePrompts();
        noShadow.Unchecked += (_, _) => GeneratePrompts();
        preserveStructure.Checked += (_, _) => GeneratePrompts();
        preserveStructure.Unchecked += (_, _) => GeneratePrompts();
        preserveTrunkRoots.Checked += (_, _) => GeneratePrompts();
        preserveTrunkRoots.Unchecked += (_, _) => GeneratePrompts();
        foreach (var input in new[] { subject, shape, material, details, colours, notes, siblingNotes })
            input.TextChanged += (_, _) => GeneratePrompts();

        copyMain.Click += async (_, _) => await CopyAsync(latest?.MainPrompt, "Main prompt");
        copySibling.Click += async (_, _) => await CopyAsync(latest?.SiblingPrompt, "Sibling prompt");
        copyBoth.Click += async (_, _) => await CopyAsync(latest?.Combined, "Both prompts");

        LoadCategoryExample();
        UpdateVariantChoices();
        UpdateSiblingControls();
        GeneratePrompts();
    }

    private static StackPanel Field(string label, Control control)
    {
        var result = new StackPanel { Spacing = 5 };
        result.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11, Foreground = Brush.Parse("#9DCBDD")
        });
        result.Children.Add(control);
        return result;
    }

    private static Grid TwoColumns(Control left, Control right)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private void LoadCategoryExample()
    {
        var example = ArtworkSiblingPromptService.ExampleFor(category.SelectedItem?.ToString() ?? "Tree");
        subject.Text = example.Subject;
        shape.Text = example.Shape;
        material.Text = example.Material;
        details.Text = example.Details;
        colours.Text = example.Colours;
    }

    private void UpdateVariantChoices()
    {
        var categoryName = category.SelectedItem?.ToString() ?? "Tree";
        var previousId = (siblingVariant.SelectedItem as ArtworkSiblingVariant)?.Id;
        var available = ArtworkSiblingPromptService.VariantsFor(categoryName);
        siblingVariant.ItemsSource = available;
        siblingVariant.SelectedItem = available.FirstOrDefault(x => x.Id == previousId)
            ?? available.FirstOrDefault();
        preserveTrunkRoots.IsVisible = categoryName is "Tree" or "Shrub";
        UpdateVariantHelp();
    }

    private void UpdateVariantHelp()
    {
        var preset = siblingVariant.SelectedItem as ArtworkSiblingVariant;
        presetDescription.Text = preset is null
            ? "Select a sibling treatment to see what will change."
            : preset.Transformation + Environment.NewLine + "Palette: " + preset.Palette;
    }

    private void UpdateSiblingControls()
    {
        var enabled = generateSibling.IsChecked == true;
        siblingVariant.IsEnabled = enabled;
        siblingNotes.IsEnabled = enabled;
        preserveStructure.IsEnabled = enabled;
        preserveTrunkRoots.IsEnabled = enabled;
    }

    private void GeneratePrompts()
    {
        try
        {
            var request = new ArtworkSiblingPromptRequest(
                (style.SelectedItem as ArtworkStylePreset)?.Id ?? "fantasy-topdown",
                category.SelectedItem?.ToString() ?? "Tree",
                subject.Text ?? "",
                shape.Text ?? "",
                material.Text ?? "",
                details.Text ?? "",
                colours.Text ?? "",
                notes.Text ?? "",
                1,
                transparent.IsChecked == true,
                noShadow.IsChecked == true,
                generateSibling.IsChecked == true
                    ? (siblingVariant.SelectedItem as ArtworkSiblingVariant)?.Id : null,
                siblingNotes.Text ?? "",
                preserveStructure.IsChecked == true,
                preserveTrunkRoots.IsChecked == true);
            latest = ArtworkSiblingPromptService.Compose(request);
            mainOutput.Text = latest.MainPrompt;
            siblingOutput.Text = latest.SiblingPrompt ?? "Enable sibling artwork to generate a companion prompt.";
            copyMain.IsEnabled = true;
            copySibling.IsEnabled = latest.SiblingPrompt is not null;
            copyBoth.IsEnabled = latest.SiblingPrompt is not null;
            status.Text = latest.SiblingPrompt is null
                ? "Main prompt ready."
                : "Main and sibling prompts ready. Copy separately or as a pair.";
        }
        catch (ArgumentException ex)
        {
            latest = null;
            mainOutput.Text = "";
            siblingOutput.Text = "";
            copyMain.IsEnabled = copySibling.IsEnabled = copyBoth.IsEnabled = false;
            status.Text = ex.Message;
        }
    }

    private async Task CopyAsync(string? value, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            status.Text = "Generate the prompt first.";
            return;
        }
        try
        {
            if (Clipboard is null)
                throw new InvalidOperationException("Clipboard is unavailable.");
            await Clipboard.SetTextAsync(value);
            status.Text = description + " copied to clipboard.";
        }
        catch (Exception ex)
        {
            status.Text = "Could not copy prompt: " + ex.Message;
        }
    }
}
