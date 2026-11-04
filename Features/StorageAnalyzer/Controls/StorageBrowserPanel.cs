using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Models;
using BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Services;
using BEN_TOOLBOX_2_0.Shared.UI;

namespace BEN_TOOLBOX_2_0.Features.StorageAnalyzer.Controls;

/// <summary>
/// SA3-A read-only browser. Sorting/index creation use the completed SA1
/// StorageNode snapshot on a worker. UI shows a maximum of 80 ListBox rows,
/// not one control per file or a ListBox with millions of item containers.
/// </summary>
public sealed class StorageBrowserPanel : StackPanel
{
    private readonly ComboBox scope = new()
    {
        ItemsSource = new[] { "Current folder", "Largest files / whole scan" },
        SelectedIndex = 0, MinWidth = 180
    };
    private readonly ComboBox sort = new()
    {
        ItemsSource = new[] { "Name", "Size", "Extension" },
        SelectedIndex = 0, MinWidth = 100
    };
    private readonly Button direction = BtxUiKit.CreateButton("A → Z");
    private readonly TextBox search = new()
    {
        Watermark = "Filter names in current view", MinWidth = 210, Width = 250
    };
    private readonly Button apply = BtxUiKit.CreateButton("FILTER");
    private readonly Button clear = BtxUiKit.CreateButton("CLEAR");
    private readonly ListBox list = new()
    {
        Height = 300, MinWidth = 220, FontSize = 12,
        FontFamily = new FontFamily("Consolas")
    };
    private readonly Button previous = BtxUiKit.CreateButton("◀ PREV");
    private readonly Button next = BtxUiKit.CreateButton("NEXT ▶");
    private readonly Button openFolder = BtxUiKit.CreateButton("GO TO SELECTED FOLDER");
    private readonly TextBlock summary = new()
    {
        Text = "Scan a drive to browse files.", TextWrapping = TextWrapping.Wrap,
        Foreground = Brush.Parse("#8AAFC4")
    };

    private StorageScanResult? snapshot;
    private StorageNode? folder;
    private StorageNode? selected;
    private StorageNode[]? largestCache;
    private StorageNode[] ordered = Array.Empty<StorageNode>();
    private StorageNode[] pageRows = Array.Empty<StorageNode>();
    private string activeFilter = "";
    private int pageIndex;
    private bool descending;
    private bool syncing;
    private bool indexing;
    private long generation;
    private CancellationTokenSource? work;
    private bool closed;

    public event Action<StorageNode>? NodeSelected;
    public event Action<StorageNode>? NavigateRequested;

    public StorageBrowserPanel()
    {
        Spacing = 10;
        BtxUiKit.ApplyInputStyle(scope);
        BtxUiKit.ApplyInputStyle(sort);
        BtxUiKit.ApplyInputStyle(search);
        list.Classes.Add("btx-results");

        var top = new WrapPanel { ItemSpacing = 8, LineSpacing = 7 };
        top.Children.Add(new TextBlock
        {
            Text = "VIEW", Foreground = Brush.Parse("#35D9FA"),
            VerticalAlignment = VerticalAlignment.Center
        });
        top.Children.Add(scope);
        top.Children.Add(new TextBlock
        {
            Text = "SORT", Foreground = Brush.Parse("#35D9FA"),
            VerticalAlignment = VerticalAlignment.Center
        });
        top.Children.Add(sort);
        top.Children.Add(direction);
        Children.Add(top);

        var searchBar = new WrapPanel { ItemSpacing = 8, LineSpacing = 7 };
        searchBar.Children.Add(search);
        searchBar.Children.Add(apply);
        searchBar.Children.Add(clear);
        Children.Add(searchBar);

        Children.Add(new TextBlock
        {
            Text = "TYPE       SIZE   NAME",
            FontFamily = new FontFamily("Consolas"),
            Foreground = Brush.Parse("#35D9FA"), FontSize = 11
        });
        Children.Add(list);

        var pagination = new WrapPanel { ItemSpacing = 8, LineSpacing = 7 };
        pagination.Children.Add(previous);
        pagination.Children.Add(next);
        pagination.Children.Add(openFolder);
        Children.Add(pagination);
        Children.Add(summary);
        Children.Add(new TextBlock
        {
            Text = "Select once for details. Double-click a directory to enter it, " +
                   "or a file to show its containing folder. Small/zero-byte files " +
                   "are still listed even when hidden by the treemap threshold.",
            Foreground = Brush.Parse("#8AAFC4"),
            FontSize = 11, TextWrapping = TextWrapping.Wrap
        });

        scope.SelectionChanged += (_, _) =>
        {
            if (syncing) return;
            syncing = true;
            if (scope.SelectedIndex == 1)
            {
                sort.SelectedIndex = 1;
                descending = true;
            }
            else
            {
                sort.SelectedIndex = 0;
                descending = false;
            }
            direction.Content = descending ? "Z → A / HIGH → LOW" : "A → Z / LOW → HIGH";
            syncing = false;
            QueueRefresh(resetPage: true);
        };
        sort.SelectionChanged += (_, _) =>
        {
            if (!syncing) QueueRefresh(resetPage: true);
        };
        direction.Click += (_, _) =>
        {
            descending = !descending;
            direction.Content = descending ? "Z → A / HIGH → LOW" : "A → Z / LOW → HIGH";
            QueueRefresh(resetPage: true);
        };
        apply.Click += (_, _) =>
        {
            activeFilter = (search.Text ?? "").Trim();
            QueueRefresh(resetPage: true);
        };
        clear.Click += (_, _) =>
        {
            search.Text = "";
            activeFilter = "";
            QueueRefresh(resetPage: true);
        };
        previous.Click += (_, _) =>
        {
            if (pageIndex <= 0) return;
            pageIndex--;
            ShowPage();
        };
        next.Click += (_, _) =>
        {
            if ((long)(pageIndex + 1) * StorageBrowserIndex.PageSize >= ordered.Length)
                return;
            pageIndex++;
            ShowPage();
        };
        openFolder.Click += (_, _) =>
        {
            if (selected is { } node)
                NavigateRequested?.Invoke(node);
        };

        list.SelectionChanged += (_, _) =>
        {
            if (syncing || list.SelectedIndex < 0
                || list.SelectedIndex >= pageRows.Length)
                return;
            selected = pageRows[list.SelectedIndex];
            RefreshButtons();
            NodeSelected?.Invoke(selected);
        };
        list.PointerPressed += (_, e) =>
        {
            if (e.ClickCount < 2 || !e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)
                return;
            if (selected is { } node)
                NavigateRequested?.Invoke(node);
        };

        RefreshButtons();
    }

    public void SetSnapshot(StorageScanResult? value)
    {
        if (ReferenceEquals(snapshot, value)) return;
        StopWork();
        snapshot = value;
        folder = null;
        selected = null;
        largestCache = null;
        ordered = Array.Empty<StorageNode>();
        pageRows = Array.Empty<StorageNode>();
        indexing = false;
        pageIndex = 0;
        summary.Text = value is null
            ? "Scan a drive to browse files." : "Choose a folder to browse.";
        syncing = true;
        list.ItemsSource = Array.Empty<string>();
        list.SelectedIndex = -1;
        syncing = false;
        RefreshButtons();
        if (value is not null && scope.SelectedIndex == 1)
            QueueRefresh(resetPage: true);
    }

    public void SetFolder(StorageNode? directory)
    {
        if (ReferenceEquals(folder, directory)) return;
        folder = directory;
        if (scope.SelectedIndex == 0)
            QueueRefresh(resetPage: true);
    }

    public void SetSelection(StorageNode? node)
    {
        selected = node;
        SyncSelection();
        RefreshButtons();
    }

    public void Close()
    {
        closed = true;
        StopWork();
        snapshot = null;
        largestCache = null;
        folder = null;
        selected = null;
        ordered = Array.Empty<StorageNode>();
        pageRows = Array.Empty<StorageNode>();
        list.ItemsSource = null;
    }

    private void StopWork()
    {
        generation++;
        work?.Cancel(); // Worker owns disposal when it finishes.
        work = null;
        indexing = false;
    }

    private async void QueueRefresh(bool resetPage)
    {
        StopWork();
        if (resetPage) pageIndex = 0;
        if (closed || snapshot is null)
        {
            ordered = Array.Empty<StorageNode>();
            ShowPage();
            return;
        }
        if (scope.SelectedIndex == 0 && folder is null)
        {
            ordered = Array.Empty<StorageNode>();
            ShowPage();
            return;
        }

        var id = generation;
        var cts = new CancellationTokenSource();
        work = cts;
        var source = snapshot;
        var currentFolder = folder;
        var largest = largestCache;
        var sortKey = (StorageBrowserIndex.SortKey)Math.Clamp(sort.SelectedIndex, 0, 2);
        bool reverse = descending;
        string query = activeFilter;
        bool global = scope.SelectedIndex == 1;
        summary.Text = global ? "Indexing largest files from scanned data…" : "Sorting folder entries…";
        indexing = true;
        RefreshButtons();

        try
        {
            var calculated = await Task.Run(() =>
            {
                StorageNode[]? ranked = null;
                StorageNode[] rows;
                if (global)
                {
                    ranked = largest ?? StorageBrowserIndex.LargestFiles(
                        source.Roots, StorageBrowserIndex.LargestFileLimit, cts.Token);
                    rows = StorageBrowserIndex.SortLargest(ranked, sortKey, reverse, query, cts.Token);
                }
                else
                {
                    rows = StorageBrowserIndex.ListDirectory(
                        currentFolder!, sortKey, reverse, query, cts.Token);
                }
                return (rows, ranked);
            }, cts.Token);

            if (cts.IsCancellationRequested || id != generation || closed)
                return;
            if (global && largestCache is null)
                largestCache = calculated.ranked;
            ordered = calculated.rows;
            ShowPage();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (id == generation && !closed)
            {
                ordered = Array.Empty<StorageNode>();
                ShowPage();
                summary.Text = "Browser indexing failed: " + ex.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(work, cts))
                work = null;
            cts.Dispose();
            if (id == generation && !closed)
            {
                indexing = false;
                RefreshButtons();
            }
        }
    }

    private void ShowPage()
    {
        int totalPages = Math.Max(1,
            (int)Math.Ceiling(ordered.Length / (double)StorageBrowserIndex.PageSize));
        pageIndex = Math.Clamp(pageIndex, 0, totalPages - 1);
        pageRows = StorageBrowserIndex.Page(ordered, pageIndex);
        var items = pageRows.Select(FormatRow).ToArray();

        syncing = true;
        list.ItemsSource = items;
        list.SelectedIndex = -1;
        syncing = false;
        SyncSelection();

        int first = ordered.Length == 0 ? 0
            : pageIndex * StorageBrowserIndex.PageSize + 1;
        int last = first == 0 ? 0 : first + pageRows.Length - 1;
        string prefix = scope.SelectedIndex == 1
            ? $"Largest {StorageBrowserIndex.LargestFileLimit:N0} files / whole scan"
            : "Current directory (folders first)";
        summary.Text = $"{prefix} · {ordered.Length:N0} matches · " +
                       $"{first:N0}–{last:N0} · page {pageIndex + 1:N0}/{totalPages:N0}" +
                       (activeFilter.Length == 0 ? "" : $" · filter: {activeFilter}");
        RefreshButtons();
    }

    private void SyncSelection()
    {
        int index = selected is null ? -1 : Array.IndexOf(pageRows, selected);
        if (list.SelectedIndex == index) return;
        syncing = true;
        list.SelectedIndex = index;
        syncing = false;
    }

    private void RefreshButtons()
    {
        bool ready = !indexing && !closed && snapshot is not null;
        previous.IsEnabled = ready && pageIndex > 0;
        next.IsEnabled = ready && (long)(pageIndex + 1) * StorageBrowserIndex.PageSize < ordered.Length;
        openFolder.IsEnabled = ready && selected is not null;
        list.IsEnabled = ready;
    }

    private static string FormatRow(StorageNode node)
    {
        string size = FormatBytes(node.LogicalBytes).PadLeft(11);
        string name = node.Name.Length > 67 ? node.Name[..64] + "..." : node.Name;
        return $"{(node.IsDirectory ? "DIR " : "FILE")} {size}  {name}";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes:N0} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024d:N1} KiB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024):N1} MiB";
        return $"{bytes / (1024d * 1024 * 1024):N2} GiB";
    }
}
