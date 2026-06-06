// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");
using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading.Tasks;
using OpenQuickHost.CSharpRuntime;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;

public class Bookmark
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class HistoryRecord
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime VisitedAt { get; set; }
}

public class CookieDto
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsHttpOnly { get; set; }
    public bool IsSecure { get; set; }
    public double Expires { get; set; }
}

public class YanziAction
{
    public static async Task<string> RunAsync(YanziActionContext context)
    {
        var tcs = new TaskCompletionSource<string>();
        Application.Current.Dispatcher.Invoke(() => 
        {
            try 
            {
                var win = new Window 
                { 
                    Title = "绉佷汉娴忚鍣?, 
                    Width = 1200, 
                    Height = 800,
                    Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                
                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                // Row 0: Toolbar
                var toolbar = new DockPanel { Background = Brushes.White, Margin = new Thickness(5) };
                
                var backBtn = new Button { Content = "鍚庨€€", Width = 40, Margin = new Thickness(0, 0, 5, 0) };
                var fwdBtn = new Button { Content = "鍓嶈繘", Width = 40, Margin = new Thickness(0, 0, 5, 0) };
                var refreshBtn = new Button { Content = "鍒锋柊", Width = 40, Margin = new Thickness(0, 0, 5, 0) };
                var goBtn = new Button { Content = "璁块棶", Width = 50, Margin = new Thickness(5, 0, 5, 0) };
                var addBmBtn = new Button { Content = "猸愭敹钘?, Width = 60, Margin = new Thickness(0, 0, 5, 0) };
                var historyBtn = new Button { Content = "馃摐鍘嗗彶", Width = 60, Margin = new Thickness(0, 0, 5, 0) };
                var syncBtn = new Button { Content = "鈽侊笍鍚屾", Width = 60, Margin = new Thickness(0, 0, 5, 0) };
                var importEdgeBtn = new Button { Content = "瀵煎叆 Edge 涔︾", Width = 110, Margin = new Thickness(0, 0, 0, 0) };
                
                var addressBar = new TextBox { VerticalContentAlignment = VerticalAlignment.Center, FontSize = 14 };

                DockPanel.SetDock(backBtn, Dock.Left);
                DockPanel.SetDock(fwdBtn, Dock.Left);
                DockPanel.SetDock(refreshBtn, Dock.Left);
                DockPanel.SetDock(importEdgeBtn, Dock.Right);
                DockPanel.SetDock(syncBtn, Dock.Right);
                DockPanel.SetDock(historyBtn, Dock.Right);
                DockPanel.SetDock(addBmBtn, Dock.Right);
                DockPanel.SetDock(goBtn, Dock.Right);
                
                toolbar.Children.Add(backBtn);
                toolbar.Children.Add(fwdBtn);
                toolbar.Children.Add(refreshBtn);
                toolbar.Children.Add(importEdgeBtn);
                toolbar.Children.Add(syncBtn);
                toolbar.Children.Add(historyBtn);
                toolbar.Children.Add(addBmBtn);
                toolbar.Children.Add(goBtn);
                toolbar.Children.Add(addressBar);
                
                Grid.SetRow(toolbar, 0);
                grid.Children.Add(toolbar);

                // Row 1: Bookmarks Bar
                var bmBar = new WrapPanel { Background = new SolidColorBrush(Color.FromRgb(220, 220, 220)) };
                var bmScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = bmBar };
                Grid.SetRow(bmScroll, 1);
                grid.Children.Add(bmScroll);

                // Row 2: TabControl
                var tabControl = new TabControl { Margin = new Thickness(0, 5, 0, 0) };
                Grid.SetRow(tabControl, 2);
                grid.Children.Add(tabControl);
                
                // Add the "+" TabItem
                var addTabItem = new TabItem 
                { 
                    Header = new TextBlock { Text = "锛?, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(4, 0, 4, 0) }, 
                    Tag = "AddTab",
                    ToolTip = "鏂板缓鏍囩椤?
                };
                tabControl.Items.Add(addTabItem);

                win.Content = grid;

                List<Bookmark> currentBookmarks = new List<Bookmark>();
                List<HistoryRecord> currentHistory = new List<HistoryRecord>();
                Dictionary<string, string> faviconCache = new Dictionary<string, string>();
                Action<List<Bookmark>> saveBookmarks = null;
                Action<HistoryRecord> addHistory = null;
                bool cookiesLoaded = false;
                
                Action renderBookmarks = () => 
                {
                    bmBar.Children.Clear();
                    foreach (var bm in currentBookmarks)
                    {
                        var title = bm.Title?.Trim() ?? "";
                        if (title == "鏈煡" || title == "鏈懡鍚?) title = "";
                        
                        var shortTitle = title.Length > 15 ? title.Substring(0, 15) + "..." : title;
                        
                        var sp = new StackPanel { Orientation = Orientation.Horizontal };
                        
                        // Try loading Favicon
                        if (Uri.TryCreate(bm.Url, UriKind.Absolute, out var uri))
                        {
                            try {
                                var img = new Image { Width = 16, Height = 16 };
                                if (!string.IsNullOrEmpty(shortTitle)) img.Margin = new Thickness(0, 0, 6, 0);
                                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                                bmp.BeginInit();
                                
                                string iconUrl = $"https://icons.duckduckgo.com/ip3/{uri.Host}.ico";
                                if (faviconCache.TryGetValue(uri.Host, out var cachedFavicon)) {
                                    iconUrl = cachedFavicon;
                                }
                                bmp.UriSource = new Uri(iconUrl);
                                bmp.EndInit();
                                img.Source = bmp;
                                sp.Children.Add(img);
                            } catch {}
                        }
                        
                        if (!string.IsNullOrEmpty(shortTitle))
                        {
                            sp.Children.Add(new TextBlock { Text = shortTitle, VerticalAlignment = VerticalAlignment.Center });
                        }

                        var btn = new Button 
                        { 
                            Content = sp, 
                            Margin = new Thickness(5),
                            Padding = new Thickness(8, 4, 8, 4),
                            ToolTip = bm.Url,
                            Background = Brushes.White,
                            BorderThickness = new Thickness(1),
                            BorderBrush = Brushes.LightGray
                        };
                        btn.Click += (s, e) => {
                            if (tabControl.SelectedItem is TabItem t && t.Content is WebView2 wv)
                            {
                                try { wv.Source = new Uri(bm.Url); } catch {}
                            }
                        };
                        
                        var cm = new ContextMenu();
                        var miRename = new MenuItem { Header = "閲嶅懡鍚? };
                        miRename.Click += (s, e) => {
                            var dialog = new Window { Title = "閲嶅懡鍚?, Width = 300, Height = 130, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = win };
                            var p = new StackPanel { Margin = new Thickness(10) };
                            var tb = new TextBox { Text = bm.Title, Margin = new Thickness(0,0,0,10) };
                            var okBtn = new Button { Content = "纭畾", Width = 80 };
                            okBtn.Click += (ss, ee) => {
                                bm.Title = tb.Text.Trim();
                                saveBookmarks(new List<Bookmark>(currentBookmarks));
                                dialog.Close();
                            };
                            p.Children.Add(tb);
                            p.Children.Add(okBtn);
                            dialog.Content = p;
                            dialog.ShowDialog();
                        };
                        
                        var miDelete = new MenuItem { Header = "鍒犻櫎" };
                        miDelete.Click += (s, e) => {
                            var list = new List<Bookmark>(currentBookmarks);
                            list.Remove(bm);
                            saveBookmarks(list);
                        };
                        
                        cm.Items.Add(miRename);
                        cm.Items.Add(miDelete);
                        btn.ContextMenu = cm;
                        
                        bmBar.Children.Add(btn);
                    }
                };

                Action loadBookmarks = () => 
                {
                    Task.Run(async () => 
                    {
                        try {
                            var cacheJson = await context.Storage.ReadTextAsync("favicon_cache.json", scope: "both");
                            if (!string.IsNullOrWhiteSpace(cacheJson)) {
                                faviconCache = JsonSerializer.Deserialize<Dictionary<string, string>>(cacheJson) ?? new Dictionary<string, string>();
                            }
                        } catch {}

                        try 
                        {
                            var json = await context.Storage.ReadTextAsync("bookmarks.json", scope: "both");
                            if (!string.IsNullOrWhiteSpace(json))
                            {
                                var bms = JsonSerializer.Deserialize<List<Bookmark>>(json) ?? new List<Bookmark>();
                                currentBookmarks = bms;
                                Application.Current.Dispatcher.Invoke(renderBookmarks);
                            }
                        }
                        catch {}
                        
                        try 
                        {
                            var histJson = await context.Storage.ReadTextAsync("history.json", scope: "both");
                            if (!string.IsNullOrWhiteSpace(histJson))
                            {
                                var hist = JsonSerializer.Deserialize<List<HistoryRecord>>(histJson) ?? new List<HistoryRecord>();
                                currentHistory = hist;
                            }
                        }
                        catch {}
                    });
                };

                saveBookmarks = (bms) =>
                {
                    currentBookmarks = bms;
                    Application.Current.Dispatcher.Invoke(renderBookmarks);
                    Task.Run(async () => 
                    {
                        try 
                        {
                            var json = JsonSerializer.Serialize(bms);
                            await context.Storage.WriteTextAsync("bookmarks.json", json, scope: "both");
                        }
                        catch {}
                    });
                };
                
                addHistory = (hr) =>
                {
                    if (string.IsNullOrWhiteSpace(hr.Url) || hr.Url == "about:blank") return;
                    currentHistory.RemoveAll(h => h.Url == hr.Url); // Keep latest
                    currentHistory.Insert(0, hr);
                    if (currentHistory.Count > 1000) currentHistory.RemoveRange(1000, currentHistory.Count - 1000); // Limit to 1000
                    
                    Task.Run(async () => 
                    {
                        try 
                        {
                            var json = JsonSerializer.Serialize(currentHistory);
                            await context.Storage.WriteTextAsync("history.json", json, scope: "local"); // Auto-save only local, sync pushed manually
                        }
                        catch {}
                    });
                };

                Func<string, Task<WebView2>> createTab = null;
                createTab = async (url) => 
                {
                    var tab = new TabItem();
                    var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
                    var titleText = new TextBlock { Text = "鏂版爣绛鹃〉", VerticalAlignment = VerticalAlignment.Center, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis };
                    var closeBtn = new Button { Content = "脳", Margin = new Thickness(5,0,0,0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
                    
                    headerPanel.Children.Add(titleText);
                    headerPanel.Children.Add(closeBtn);
                    tab.Header = headerPanel;
                    
                    var webView = new WebView2();
                    tab.Content = webView;
                    
                    int insertIndex = tabControl.Items.Count;
                    for (int i = 0; i < tabControl.Items.Count; i++) {
                        if (tabControl.Items[i] is TabItem t && t.Tag?.ToString() == "AddTab") {
                            insertIndex = i;
                            break;
                        }
                    }
                    
                    tabControl.Items.Insert(insertIndex, tab);
                    tabControl.SelectedItem = tab;

                    closeBtn.Click += (s, e) => {
                        tabControl.Items.Remove(tab);
                        webView.Dispose();
                        if (tabControl.Items.Count <= 1) win.Close(); // Only '+' tab left
                    };

                    try 
                    {
                        var env = await CoreWebView2Environment.CreateAsync(null, context.ExtensionDataDirectory);
                        await webView.EnsureCoreWebView2Async(env);
                        
                        if (!cookiesLoaded) {
                            cookiesLoaded = true;
                            try {
                                var cjson = await context.Storage.ReadTextAsync("cookies.json", scope: "both");
                                if (!string.IsNullOrWhiteSpace(cjson)) {
                                    var dtos = JsonSerializer.Deserialize<List<CookieDto>>(cjson);
                                    if (dtos != null) {
                                        foreach (var dto in dtos) {
                                            var cookie = webView.CoreWebView2.CookieManager.CreateCookie(dto.Name, dto.Value, dto.Domain, dto.Path);
                                            cookie.IsHttpOnly = dto.IsHttpOnly;
                                            cookie.IsSecure = dto.IsSecure;
                                            if (dto.Expires > 0) cookie.Expires = dto.Expires;
                                            webView.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);
                                        }
                                    }
                                }
                            } catch {}
                        }
                        
                        webView.CoreWebView2.NavigationCompleted += (s, e) => {
                            if (e.IsSuccess && !string.IsNullOrWhiteSpace(webView.CoreWebView2.DocumentTitle)) {
                                addHistory(new HistoryRecord { Title = webView.CoreWebView2.DocumentTitle, Url = webView.Source.ToString(), VisitedAt = DateTime.Now });
                            }
                        };
                        
                        webView.CoreWebView2.DocumentTitleChanged += (s, e) => {
                            titleText.Text = webView.CoreWebView2.DocumentTitle;
                            if (tabControl.SelectedItem == tab) win.Title = webView.CoreWebView2.DocumentTitle + " - 绉佷汉娴忚鍣?;
                        };

                        webView.CoreWebView2.SourceChanged += (s, e) => {
                            if (tabControl.SelectedItem == tab) addressBar.Text = webView.Source.ToString();
                        };
                        
                        webView.CoreWebView2.FaviconChanged += (s, e) => {
                            try {
                                var favUri = webView.CoreWebView2.FaviconUri;
                                if (!string.IsNullOrEmpty(favUri) && webView.Source != null && Uri.TryCreate(webView.Source.ToString(), UriKind.Absolute, out var currentUri)) {
                                    bool changed = false;
                                    if (!faviconCache.ContainsKey(currentUri.Host) || faviconCache[currentUri.Host] != favUri) {
                                        faviconCache[currentUri.Host] = favUri;
                                        changed = true;
                                    }
                                    if (changed) {
                                        Task.Run(async () => {
                                            try {
                                                var json = JsonSerializer.Serialize(faviconCache);
                                                await context.Storage.WriteTextAsync("favicon_cache.json", json, scope: "both");
                                                Application.Current.Dispatcher.Invoke(renderBookmarks);
                                            } catch {}
                                        });
                                    }
                                }
                            } catch {}
                        };

                        webView.CoreWebView2.NewWindowRequested += async (s, e) => {
                            e.Handled = true;
                            var deferral = e.GetDeferral();
                            try 
                            {
                                var newWv = await createTab(e.Uri);
                                e.NewWindow = newWv.CoreWebView2;
                            }
                            finally
                            {
                                deferral.Complete();
                            }
                        };

                        if (!string.IsNullOrEmpty(url)) {
                            webView.Source = new Uri(url);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error initializing tab: " + ex.ToString());
                    }
                    return webView;
                };

                addTabItem.PreviewMouseLeftButtonDown += async (s, e) => {
                    e.Handled = true; // Prevent selecting the + tab
                    await createTab("https://cn.bing.com/");
                };

                // Event Handlers
                win.Loaded += async (s, e) =>
                {
                    loadBookmarks();
                    await createTab("https://cn.bing.com/");
                };

                tabControl.SelectionChanged += (s, e) => {
                    if (tabControl.SelectedItem is TabItem tab && tab.Content is WebView2 wv) {
                        try { addressBar.Text = wv.Source?.ToString() ?? ""; } catch {}
                        win.Title = (wv.CoreWebView2?.DocumentTitle ?? "鏂版爣绛鹃〉") + " - 绉佷汉娴忚鍣?;
                    }
                };

                backBtn.Click += (s, e) => { if (tabControl.SelectedItem is TabItem t && t.Content is WebView2 wv && wv.CanGoBack) wv.GoBack(); };
                fwdBtn.Click += (s, e) => { if (tabControl.SelectedItem is TabItem t && t.Content is WebView2 wv && wv.CanGoForward) wv.GoForward(); };
                refreshBtn.Click += (s, e) => { if (tabControl.SelectedItem is TabItem t && t.Content is WebView2 wv) wv.Reload(); };
                
                Action navigate = () => {
                    var url = addressBar.Text.Trim();
                    if (!url.StartsWith("http")) url = "https://" + url;
                    if (tabControl.SelectedItem is TabItem t && t.Content is WebView2 wv)
                    {
                        try { wv.Source = new Uri(url); } catch {}
                    }
                };
                
                goBtn.Click += (s, e) => navigate();
                addressBar.KeyDown += (s, e) => { if (e.Key == Key.Enter) navigate(); };
                
                historyBtn.Click += (s, e) => {
                    var hw = new Window { Title = "娴忚鍘嗗彶", Width = 600, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = win };
                    var hgrid = new Grid();
                    hgrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    hgrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    var btnClear = new Button { Content = "娓呯┖鍘嗗彶", Margin = new Thickness(5), Width = 100, HorizontalAlignment = HorizontalAlignment.Right };
                    Grid.SetRow(btnClear, 0);
                    hgrid.Children.Add(btnClear);
                    
                    var lb = new ListBox { Margin = new Thickness(5) };
                    foreach(var hr in currentHistory) {
                        var sp = new StackPanel { Orientation = Orientation.Horizontal };
                        sp.Children.Add(new TextBlock { Text = hr.VisitedAt.ToString("MM-dd HH:mm") + "  ", Foreground = Brushes.Gray });
                        var displayTitle = hr.Title ?? "";
                        if (displayTitle.Length > 40) displayTitle = displayTitle.Substring(0, 40) + "...";
                        sp.Children.Add(new TextBlock { Text = displayTitle + "  " });
                        sp.Children.Add(new TextBlock { Text = hr.Url, Foreground = Brushes.Blue });
                        var lbi = new ListBoxItem { Content = sp, Tag = hr.Url };
                        lbi.MouseDoubleClick += (ss, ee) => { hw.Close(); addressBar.Text = hr.Url; navigate(); };
                        lb.Items.Add(lbi);
                    }
                    Grid.SetRow(lb, 1);
                    hgrid.Children.Add(lb);
                    
                    btnClear.Click += (ss, ee) => { 
                        currentHistory.Clear(); 
                        lb.Items.Clear();
                        Task.Run(async () => { try { await context.Storage.WriteTextAsync("history.json", "[]", scope: "local"); } catch {} });
                    };
                    
                    hw.Content = hgrid;
                    hw.ShowDialog();
                };
                
                syncBtn.Click += async (s, e) => {
                    try {
                        var wv = (tabControl.SelectedItem as TabItem)?.Content as WebView2;
                        if (wv?.CoreWebView2 != null) {
                            var cookies = await wv.CoreWebView2.CookieManager.GetCookiesAsync("");
                            var dtos = new List<CookieDto>();
                            foreach (var c in cookies) {
                                dtos.Add(new CookieDto { Name = c.Name, Value = c.Value, Domain = c.Domain, Path = c.Path, IsHttpOnly = c.IsHttpOnly, IsSecure = c.IsSecure, Expires = c.Expires });
                            }
                            var json = JsonSerializer.Serialize(dtos);
                            await context.Storage.WriteTextAsync("cookies.json", json, scope: "both");
                            
                            // 寮哄埗鍚屾鏈湴鏂囦欢鍒颁簯绔?
                            using var client = new System.Net.Http.HttpClient();
                            await client.PostAsync("http://127.0.0.1:53919/v1/storage/mini-browser/sync", null);
                            MessageBox.Show("浜戠鍚屾瀹屾垚锛乗n宸插浠戒功绛俱€佸巻鍙茶褰曚笌鐧诲綍鐘舵€?Cookie)锛?, "绉佷汉娴忚鍣?);
                        } else {
                            MessageBox.Show("鏃犳硶鑾峰彇娴忚鍣ㄥ疄渚嬶紝璇峰厛鎵撳紑涓€涓綉椤点€?, "鎻愮ず");
                        }
                    } catch (Exception ex) {
                        MessageBox.Show("鍚屾澶辫触: " + ex.Message);
                    }
                };
                
                addBmBtn.Click += (s, e) => 
                {
                    if (tabControl.SelectedItem is TabItem t && t.Content is WebView2 wv && wv.Source != null)
                    {
                        var bms = new List<Bookmark>(currentBookmarks);
                        bms.Add(new Bookmark { Title = wv.CoreWebView2.DocumentTitle, Url = wv.Source.ToString() });
                        saveBookmarks(bms);
                    }
                };

                importEdgeBtn.Click += (s, e) => 
                {
                    Task.Run(() => 
                    {
                        try 
                        {
                            var edgeBookmarksPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "User Data", "Default", "Bookmarks");
                            if (!File.Exists(edgeBookmarksPath)) 
                            {
                                Application.Current.Dispatcher.Invoke(() => MessageBox.Show("鏈壘鍒?Edge 涔︾鏂囦欢銆?));
                                return;
                            }
                            var json = File.ReadAllText(edgeBookmarksPath);
                            using var doc = JsonDocument.Parse(json);
                            var root = doc.RootElement;
                            var newBms = new List<Bookmark>(currentBookmarks);
                            
                            void ParseNode(JsonElement node)
                            {
                                if (node.ValueKind == JsonValueKind.Object)
                                {
                                    if (node.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "url")
                                    {
                                        var title = node.TryGetProperty("name", out var n) ? n.GetString() : "";
                                        var url = node.TryGetProperty("url", out var u) ? u.GetString() : "";
                                        if (!string.IsNullOrWhiteSpace(url) && !url.StartsWith("chrome-extension://")) 
                                        {
                                            newBms.Add(new Bookmark { Title = title, Url = url });
                                        }
                                    }
                                    if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var child in children.EnumerateArray()) ParseNode(child);
                                    }
                                }
                            }
                            
                            if (root.TryGetProperty("roots", out var roots))
                            {
                                if (roots.TryGetProperty("bookmark_bar", out var bar)) ParseNode(bar);
                                if (roots.TryGetProperty("other", out var other)) ParseNode(other);
                            }
                            
                            saveBookmarks(newBms);
                            Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"鎴愬姛瀵煎叆锛屽綋鍓嶅叡鏈?{newBms.Count} 涓功绛撅紒"));
                        }
                        catch (Exception ex)
                        {
                            Application.Current.Dispatcher.Invoke(() => MessageBox.Show("瀵煎叆澶辫触: " + ex.Message));
                        }
                    });
                };

                win.Closed += (s, e) => tcs.TrySetResult("Window Closed");
                win.Show();
            }
            catch (Exception ex)
            {
                tcs.TrySetResult("Error: " + ex.Message);
            }
        });
        return await tcs.Task;
    }
}
