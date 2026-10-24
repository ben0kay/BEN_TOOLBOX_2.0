using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BEN_TOOLBOX_2_0;
using BEN_TOOLBOX_2_0.Views;
using BEN_TOOLBOX_2_0.Views.Controls;

AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var window=new SeasonalSpriteStudioWindow();window.Show();Dispatcher.UIThread.RunJobs();
var canvas=window.GetVisualDescendants().OfType<SeasonalMaskCanvas>().Single();
if(canvas.Bounds.Width<200 || canvas.Bounds.Height<200)throw new Exception("Preview has no useful area.");
if(window.GetVisualDescendants().OfType<Expander>().Count()<4)throw new Exception("Settings groups missing.");
window.Width=1100;window.Height=650;Dispatcher.UIThread.RunJobs();
if(canvas.Bounds.Width<=0 || canvas.Bounds.Height<=0)throw new Exception("Preview collapsed at minimum size.");
window.Close();Dispatcher.UIThread.RunJobs();
Console.WriteLine("PASS: Studio constructor, headless layout, grouped controls, preview at default/minimum size and clean close.");
