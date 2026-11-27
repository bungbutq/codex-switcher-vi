using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexAccountSwitcher.Core;

namespace CodexAccountSwitcher;

public sealed class MainWindow : Window
{
    private const string AccountDragFormat = "CodexAccountSwitcher.AccountID";
    private readonly IAccountClient client;
    private readonly NativeSettings native;
    private readonly string dataPath;
    private readonly Func<DateTimeOffset> clock;
    private readonly DispatcherTimer stalenessTimer;
    private readonly DispatcherTimer countdownTimer;
    private readonly List<(TextBlock Value, TextBlock Caption, Usage? Usage)> countdowns = [];
    private int accountColumns = 1;
    private readonly List<ComboBox> activeComboBoxes = [];
    private ScrollViewer? scrollViewer;
    private string page = "accounts";
    private AccountRow? target;
    private string? localError;
    private bool closingForExit;
    private bool isDragging;
    private bool pendingBackgroundRender;
    public bool IsBusy => client.State.IsMutating;
    public string CurrentPage => page;
    internal string DataPath => dataPath;
    private AccountSnapshot State => client.State;
    private string T(string key) => VietnameseText.Get(key);
    private Brush B(string key) => (Brush)FindResource(key);

    public MainWindow(
        IAccountClient client,
        NativeSettings? native = null,
        UpdateRestartHooks? restartHooks = null,
        string? dataPath = null,
        string? updatesRoot = null,
        bool launchedInBackground = false,
        Func<DateTimeOffset>? clock = null)
    {
        this.client = client;
        this.native = native ?? new NativeSettings();
        this.dataPath = !string.IsNullOrWhiteSpace(dataPath)
            ? dataPath
            : Environment.GetEnvironmentVariable("CODEX_SWITCHER_DATA_HOME")
                ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Codex Account Switcher");
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        stalenessTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        stalenessTimer.Tick += (_, _) => RefreshIfVisible();
        countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        countdownTimer.Tick += (_, _) => UpdateCountdowns();
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(App.IconUri);
        Title = "Codex Account Switcher – Tiếng Việt"; Width = Math.Min(570, SystemParameters.WorkArea.Width - 24); SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanMinimize;
        Background = B("Surface"); ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        MaxHeight = SystemParameters.WorkArea.Height - 24;
        client.Changed += OnExternalChanged; this.native.Changed += OnExternalChanged;
        IsVisibleChanged += (_, _) => {
            if (IsVisible) { stalenessTimer.Start(); countdownTimer.Start(); UpdateCountdowns(); }
            else { stalenessTimer.Stop(); countdownTimer.Stop(); }
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !IsBusy && page != "accounts") { Navigate(page is "remove" or "note" ? "manage" : "accounts"); e.Handled = true; } };
        Closing += (_, e) => {
            if (closingForExit) return;
            e.Cancel = true;
            Hide();
        };
        Closed += (_, _) => { stalenessTimer.Stop(); countdownTimer.Stop(); client.Changed -= OnExternalChanged; this.native.Changed -= OnExternalChanged; };
        Render();
    }

    internal void RefreshIfVisible() { if (IsVisible) OnExternalChanged(); }

    private void OnExternalChanged()
    {
        if (page == "note") return;
        if (isDragging || activeComboBoxes.Any(box => box.IsDropDownOpen)) {
            pendingBackgroundRender = true;
            return;
        }
        pendingBackgroundRender = false;
        Render(preserveScroll: true);
    }

    public void CloseForExit() { closingForExit = true; Close(); }

    public void OpenWindow()
    {
        if (State.IsAddingAccount) Navigate("manage");
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        RefreshUsage();
    }
    internal void RefreshUsage() => _ = Run("refresh");
    public void Navigate(string destination) { page = destination; target = null; localError = null; Render(preserveScroll: false); }
    private async Task Run(string command, Guid? id = null, bool? value = null, string? language = null,
        Guid? targetAccountID = null)
    {
        try { await client.CommandAsync(command, id, value, language, targetAccountID); }
        catch (Exception ex) { localError = ex.Message; Render(preserveScroll: true); }
    }
    private TextBlock Text(string text, double size = 13, bool muted = false, bool bold = false) => new() {
        Text = text, FontSize = size, Foreground = B(muted ? "Muted" : "Ink"),
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
    };
    private Button Button(string label, Action action, string? glyph = null, bool left = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Center };
        if (glyph != null) content.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), Margin = new Thickness(0, 0, label.Length > 0 ? 7 : 0, 0), VerticalAlignment = VerticalAlignment.Center });
        if (label.Length > 0) content.Children.Add(new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, IsEnabled = !IsBusy, Padding = new Thickness(12, 6, 12, 6), MinHeight = 32, MinWidth = 32 };
        AutomationProperties.SetName(button, label); button.Click += (_, _) => action(); return button;
    }
    private Border Rule() => new() { Height = 1, Background = B("Line") };
    private Grid Pair(UIElement left, UIElement right)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(right, 1); grid.Children.Add(left); grid.Children.Add(right); return grid;
    }

    private void Render(bool preserveScroll = false)
    {
        var previousOffset = preserveScroll ? scrollViewer?.VerticalOffset ?? 0 : 0;
        activeComboBoxes.Clear();
        countdowns.Clear();
        var body = new StackPanel();
        UIElement? pinnedHeader = null;
        if (State.Error != null || localError != null) {
            var message = Text(localError ?? State.Error!, 11, muted: true); message.TextWrapping = TextWrapping.Wrap;
            var box = Pair(message, Button("×", () => { localError = null; _ = Run("dismissError"); Render(preserveScroll: true); }));
            box.Margin = new Thickness(12, 8, 12, 10); body.Children.Add(box); body.Children.Add(Rule());
        }
        if (page == "accounts") {
            var commands = new StackPanel { Orientation = Orientation.Horizontal };
            commands.Children.Add(Button(T("manage"), () => Navigate("manage"), "\uE716"));
            for (var i = 1; i <= 3; i++) {
                var columns = i;
                var layoutButton = Button(i.ToString(CultureInfo.InvariantCulture), () => SetAccountColumns(columns));
                layoutButton.Padding = new Thickness(6, 5, 6, 5);
                layoutButton.MinWidth = 28;
                layoutButton.Margin = new Thickness(i == 1 ? 8 : 2, 0, 0, 0);
                layoutButton.ToolTip = $"Hiển thị {i} cột";
                AutomationProperties.SetName(layoutButton, $"Hiển thị {i} cột");
                if (i == accountColumns) {
                    layoutButton.Background = B("ActiveSurface");
                    layoutButton.BorderBrush = B("ActiveGreen");
                }
                commands.Children.Add(layoutButton);
            }
            var refresh = Button("", RefreshUsage, "\uE72C");
            refresh.Margin = new Thickness(8, 0, 0, 0);
            refresh.ToolTip = T("refresh");
            AutomationProperties.SetName(refresh, T("refresh"));
            refresh.IsEnabled &= !State.IsAddingAccount;
            commands.Children.Add(refresh);
            var settings = Button("", () => Navigate("settings"), "\uE713");
            settings.Margin = new Thickness(8, 0, 0, 0); settings.ToolTip = T("settings");
            AutomationProperties.SetName(settings, T("settings"));
            commands.Children.Add(settings);
            var heading = Pair(Text(T("accounts"), 20, bold: true), commands);
            heading.Margin = new Thickness(20, 16, 20, 16); pinnedHeader = heading;
        } else {
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 16, 20, 16) };
            header.Children.Add(Button("", () => Navigate(page is "remove" or "note" ? "manage" : "accounts"), "\uE72B"));
            AutomationProperties.SetName(header.Children[0], T("back"));
            var title = target != null && page is "switch" or "remove"
                ? T(page + "_title").Replace("%@", target.Profile.ConfirmationTitle)
                : T(page switch { "settings" => "settings", "note" => "edit_account_note", _ => "accounts" });
            var heading = Text(title, 20, bold: true); heading.Margin = new Thickness(12, 0, 0, 0);
            heading.MaxWidth = 380; heading.ToolTip = title; header.Children.Add(heading);
            body.Children.Add(header);
        }
        if (page is "switch" or "remove") Confirmation(body);
        else if (page == "settings") Settings(body);
        else if (page == "note" && target is { } edited) EditNote(body, edited.Profile);
        else Accounts(body, page == "manage");
        var viewer = new ScrollViewer {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        if (preserveScroll && previousOffset > 0) {
            viewer.Loaded += (_, _) => viewer.ScrollToVerticalOffset(previousOffset);
        }
        scrollViewer = viewer;
        var root = new DockPanel { LastChildFill = true };
        if (pinnedHeader != null) {
            DockPanel.SetDock(pinnedHeader, Dock.Top);
            root.Children.Add(pinnedHeader);
        }
        root.Children.Add(viewer);
        Content = new Border { Background = B("Surface"), Child = root };
        UpdateCountdowns();
    }

    private void SetAccountColumns(int columns)
    {
        if (columns == accountColumns) return;
        accountColumns = Math.Clamp(columns, 1, 3);
        var work = SystemParameters.WorkArea;
        Width = Math.Min(570 + 530 * (accountColumns - 1), work.Width - 24);
        if (IsVisible) {
            Left = Math.Max(work.Left, Math.Min(Left, work.Right - Width));
        }
        Render(preserveScroll: false);
    }

    private void UpdateCountdowns()
    {
        var now = clock();
        foreach (var (value, caption, usage) in countdowns) {
            value.ToolTip = null;
            if (usage == null) {
                value.Text = "—";
                value.Foreground = B("Muted");
                caption.Text = "Chờ đồng bộ";
                continue;
            }
            // The weekly limit blocks use even when the five-hour limit remains.
            DateTimeOffset? reset;
            string window;
            bool blocked = usage.RemainingPercent <= 0 || usage.FiveHourRemainingPercent is <= 0;
            if (usage.RemainingPercent <= 0) {
                reset = usage.ResetsAt; window = "7 ngày";
            } else if (usage.FiveHourRemainingPercent is <= 0) {
                reset = usage.FiveHourResetsAt; window = "5 giờ";
            } else if (usage.FiveHourResetsAt is { } five &&
                       five > now && (usage.ResetsAt <= now || five <= usage.ResetsAt)) {
                reset = five; window = "5 giờ";
            } else if (usage.ResetsAt > now) {
                reset = usage.ResetsAt; window = "7 ngày";
            } else {
                reset = usage.FiveHourResetsAt ?? usage.ResetsAt;
                window = usage.FiveHourResetsAt.HasValue ? "5 giờ" : "7 ngày";
            }
            caption.Text = "Reset " + window;
            value.Foreground = B(blocked ? "Warning" : "ActiveGreen");
            if (reset == null) { value.Text = "—"; caption.Text = "Chưa có mốc"; continue; }
            var seconds = (long)Math.Ceiling((reset.Value - now).TotalSeconds);
            if (seconds <= 0) {
                value.Text = "0p00s";
                caption.Text = "Chờ xác nhận";
                value.Foreground = B("Muted");
                continue;
            }
            var time = TimeSpan.FromSeconds(seconds);
            var shortTime = $"{time.Minutes}p{time.Seconds:00}s";
            value.Text = time.Days > 0 ? $"{time.Days}n {time.Hours:00}g\n{shortTime}" :
                         time.Hours > 0 ? $"{time.Hours}g\n{shortTime}" : shortTime;
            value.ToolTip = $"Reset {window}: {reset.Value.ToLocalTime():dd/MM/yyyy HH:mm:ss}";
        }
    }

    private UIElement CountdownAvatar(Border avatar, Usage? usage)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
        panel.Children.Add(avatar);
        var value = Text("—", 24, bold: true);
        value.FontWeight = FontWeights.Bold;
        value.FontFamily = new FontFamily("Consolas, Segoe UI");
        value.TextAlignment = TextAlignment.Center;
        value.TextTrimming = TextTrimming.None;
        value.Margin = new Thickness(0, 5, 0, 2);
        value.MinHeight = 58;
        AutomationProperties.SetName(value, "reset-countdown");
        var caption = Text("Chờ đồng bộ", 10, muted: true);
        caption.TextAlignment = TextAlignment.Center;
        AutomationProperties.SetName(caption, "reset-countdown-window");
        panel.Children.Add(value);
        panel.Children.Add(caption);
        countdowns.Add((value, caption, usage));
        return panel;
    }

    private Guid? DraggedAccount(IDataObject data, Guid targetID)
    {
        if (IsBusy || State.IsAddingAccount || !data.GetDataPresent(AccountDragFormat, autoConvert: false)
            || !Guid.TryParse(data.GetData(AccountDragFormat, autoConvert: false) as string, out var sourceID))
            return null;
        return sourceID != targetID && State.Accounts.Any(account => account.Profile.Id == sourceID)
            ? sourceID : null;
    }

    private TextBlock BuildSubscriptionLine(AccountRow row)
    {
        var presentation = row.SubscriptionView ?? new SubscriptionPresentation("pending");
        var now = clock();
        var block = Text(presentation.FormatPrimary(now, T), 10.5, muted: true);
        if (presentation.NeedsUpdate(now)) block.Foreground = B("Warning");
        block.ToolTip = presentation.FormatDetail(now, T, CultureInfo.CurrentCulture);
        return block;
    }

    private void Accounts(StackPanel body, bool manage)
    {
        if (!manage && !State.ActiveIdentityConfirmed) {
            var warning = new StackPanel { Margin = new Thickness(12, 8, 12, 4) };
            warning.Children.Add(Text(T("active_unconfirmed"), 11, muted: true));
            warning.Children.Add(Button(T("manage"), () => Navigate("manage"), left: true)); body.Children.Add(warning);
        }
        Panel list = manage ? new StackPanel() : new UniformGrid { Columns = accountColumns };
        var canReorder = manage && State.Accounts.Length > 1;
        foreach (var row in State.Accounts) {
            var avatar = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(4), Background = B("Hover"),
                Child = Text(row.Initials, 11, bold: true) };
            ((TextBlock)avatar.Child).HorizontalAlignment = HorizontalAlignment.Center;
            var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            UIElement? accountHeader = null;
            var active = State.ActiveIdentityConfirmed && row.Profile.Id == State.ActiveAccountID;
            var title = row.DisplayTitle;
            var name = Text(title, 14, bold: true);
            name.ToolTip = title;
            if (manage) {
                details.Children.Add(name);
            } else {
                var switchButton = Button(active ? T("active") : T("switch"), () => {
                    target = row; page = "switch"; Render();
                });
                switchButton.IsEnabled &= !active && !State.IsAddingAccount;
                switchButton.Margin = new Thickness(8, 0, 0, 0);
                AutomationProperties.SetName(switchButton, (active ? T("active") : T("switch")) + ": " + title);
                if (active && switchButton.Content is StackPanel activeContent) {
                    foreach (var label in activeContent.Children.OfType<TextBlock>()) label.Foreground = B("ActiveGreen");
                }
                accountHeader = Pair(name, switchButton);
                var usage = row.Usage;
                details.Children.Add(UsageLine(T("five_hour"), usage?.FiveHourRemainingPercent, usage?.FiveHourResetsAt));
                details.Children.Add(UsageLine(T("weekly"), usage?.RemainingPercent, usage?.ResetsAt));
                if (!string.IsNullOrWhiteSpace(row.UsageError)) {
                    var status = Text(usage == null ? T("usage_unavailable") : T("usage_stale"), 11, muted: true);
                    status.Foreground = B("Warning"); status.ToolTip = row.UsageError;
                    details.Children.Add(status);
                }
                var resetCredits = Text(T("reset_credits") + ": " + (usage?.ResetCreditsRemaining?.ToString(CultureInfo.InvariantCulture) ?? T("unknown")), 12, bold: true);
                resetCredits.Margin = new Thickness(0, 7, 0, 0);
                details.Children.Add(resetCredits);
                var accountInfo = BuildSubscriptionLine(row);
                accountInfo.Margin = new Thickness(0, 3, 0, 0);
                details.Children.Add(accountInfo);
            }
            if (row.Profile.LabeledEmail is { } email) {
                var address = Text(email, 11, muted: true);
                address.ToolTip = email;
                if (manage) details.Children.Insert(1, address);
                else if (accountHeader != null) {
                    var fullHeader = new StackPanel();
                    fullHeader.Children.Add(accountHeader);
                    fullHeader.Children.Add(address);
                    accountHeader = fullHeader;
                }
            }
            var grid = new Grid { MinHeight = manage ? 32 : 50 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(manage ? 44 : 120) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            if (manage) grid.Children.Add(avatar);
            else grid.Children.Add(CountdownAvatar(avatar, row.Usage));
            Grid.SetColumn(details, 1); grid.Children.Add(details);
            if (!manage && accountHeader != null) {
                var content = new StackPanel();
                content.Children.Add(accountHeader);
                content.Children.Add(grid);
                grid = new Grid();
                grid.Children.Add(content);
            }
            if (manage) {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                UIElement action = row.Profile.Id == State.ActiveAccountID ? Text(active ? T("active") : T("account_unconfirmed"), 11, muted: true) : Button("", () => { target = row; page = "remove"; Render(); }, "\uE74D");
                AutomationProperties.SetName(action, row.Profile.Id == State.ActiveAccountID ? (active ? T("active") : T("account_unconfirmed")) : T("remove"));
                var edit = Button("", () => { target = row; page = "note"; Render(); }, "\uE70F");
                edit.IsEnabled &= !State.IsAddingAccount;
                edit.ToolTip = T("edit_account_note");
                AutomationProperties.SetName(edit, T("edit_account_note"));
                edit.Margin = new Thickness(0, 0, 6, 0);
                var rowActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                rowActions.Children.Add(edit);
                rowActions.Children.Add(action);
                Grid.SetColumn(rowActions, 2);
                grid.Children.Add(rowActions);
                var item = new Border { Padding = new Thickness(16, 12, 16, 12), Child = grid };
                if (canReorder) {
                    var handle = Text("☰", 17, muted: true);
                    handle.Cursor = Cursors.SizeAll;
                    handle.ToolTip = T("drag_to_reorder");
                    AutomationProperties.SetName(handle, T("drag_to_reorder"));
                    handle.MouseMove += (_, e) => {
                        if (!IsBusy && !State.IsAddingAccount && e.LeftButton == MouseButtonState.Pressed) {
                            isDragging = true;
                            try {
                                DragDrop.DoDragDrop(handle, new DataObject(AccountDragFormat, row.Profile.Id.ToString()), DragDropEffects.Move);
                            } finally {
                                isDragging = false;
                                if (pendingBackgroundRender) { pendingBackgroundRender = false; Render(preserveScroll: true); }
                            }
                        }
                    };
                    grid.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = new GridLength(26) });
                    Grid.SetColumn(avatar, 1);
                    Grid.SetColumn(details, 2);
                    Grid.SetColumn(rowActions, 3);
                    grid.Children.Add(handle);
                    item.AllowDrop = true;
                    item.Background = Brushes.Transparent;
                    item.DragOver += (_, e) => {
                        e.Effects = DraggedAccount(e.Data, row.Profile.Id) != null ? DragDropEffects.Move : DragDropEffects.None;
                        e.Handled = true;
                    };
                    item.Drop += (_, e) => {
                        e.Handled = true;
                        if (DraggedAccount(e.Data, row.Profile.Id) is { } sourceID)
                            _ = Run("move", sourceID, targetAccountID: row.Profile.Id);
                    };
                }
                list.Children.Add(item);
            } else {
                var card = new Border { Child = grid, Padding = new Thickness(13, 12, 13, 12),
                    Background = active ? B("ActiveSurface") : B("ListSurface"),
                    BorderBrush = active ? B("ActiveGreen") : B("Line"), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(7), ClipToBounds = true };
                var selection = new Border { Width = 5, Background = active ? B("ActiveGreen") : Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch, IsHitTestVisible = false };
                AutomationProperties.SetName(selection, active ? "active-indicator" : "inactive-indicator");
                var rowContainer = new Grid { Margin = new Thickness(0, 0, accountColumns > 1 ? 8 : 0, 8) };
                rowContainer.Children.Add(card); rowContainer.Children.Add(selection);
                list.Children.Add(rowContainer);
            }
        }
        if (State.Accounts.Length == 0) { var empty = Text(T("no_accounts"), 12, muted: true); empty.Margin = new Thickness(12, 24, 12, 24); list.Children.Add(empty); }
        body.Children.Add(new Border { Margin = new Thickness(20, 0, 20, 12), BorderBrush = B("Line"), BorderThickness = new Thickness(manage ? 1 : 0),
            Background = manage ? B("ListSurface") : Brushes.Transparent, CornerRadius = new CornerRadius(4), Child = list });
        if (manage) {
            var actions = new StackPanel { Margin = new Thickness(20, 0, 20, 20) };
            var add = Button(T(State.IsAddingAccount ? "cancel_add_account" : "add_account"), () => _ = Run(State.IsAddingAccount ? "cancelAdd" : "add"), State.IsAddingAccount ? "\uE711" : "\uE710");
            var hint = Text(T(State.IsAddingAccount ? "sign_in_pending_hint" : "sign_in_hint"), 12, muted: true);
            hint.ToolTip = hint.Text;
            hint.Margin = new Thickness(8, 0, 0, 0);
            var addRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            addRow.ColumnDefinitions.Add(new ColumnDefinition());
            addRow.Children.Add(add); Grid.SetColumn(hint, 1); addRow.Children.Add(hint);
            actions.Children.Add(addRow);
            var register = Button(T("register_current_account"), () => _ = Run("register"));
            register.IsEnabled &= !State.IsAddingAccount; register.HorizontalAlignment = HorizontalAlignment.Left;
            actions.Children.Add(register); body.Children.Add(actions);
        }
    }

    private void EditNote(StackPanel body, Profile profile)
    {
        var form = new StackPanel { Margin = new Thickness(20, 0, 20, 20) };
        var input = new TextBox { Text = profile.Note ?? "", Margin = new Thickness(0, 4, 0, 6), MinHeight = 30 };
        AutomationProperties.SetName(input, T("account_note"));
        form.Children.Add(Text(T("account_note")));
        form.Children.Add(input);
        form.Children.Add(Text(T("account_note_hint"), 11, muted: true));
        var error = Text("", 11);
        error.TextWrapping = TextWrapping.Wrap;
        form.Children.Add(error);
        var actions = new StackPanel {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var cancel = Button(T("cancel"), () => Navigate("manage"));
        var save = Button(T("save_account_note"), () => _ = SaveNoteAsync());
        save.IsDefault = true;
        save.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        form.Children.Add(actions);
        body.Children.Add(form);
        input.Loaded += (_, _) => input.Focus();

        async Task SaveNoteAsync()
        {
            form.IsEnabled = false;
            try {
                await client.SetAccountNoteAsync(profile.Id, input.Text);
                Navigate("manage");
            } catch (Exception ex) {
                error.Text = ex.Message;
                form.IsEnabled = true;
            }
        }
    }

    private Grid UsageLine(string title, int? percent, DateTimeOffset? reset)
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var value = percent is int raw ? Math.Clamp(raw, 0, 100) : (int?)null;
        var color = value switch { null => B("Muted"), <= 15 => B("UsageLow"), <= 40 => B("Warning"), _ => B("ActiveGreen") };
        var label = Text(value is int v ? v + T("left") : T("unknown"), 12, bold: true);
        label.Foreground = color;
        grid.Children.Add(Pair(Text(title, 12, bold: true), label));
        var track = new Grid { Height = 6, Margin = new Thickness(0, 4, 0, 2) };
        track.Children.Add(new Border { Background = B("Line"), CornerRadius = new CornerRadius(3) });
        if (value is int pct) {
            var fill = new Border { Background = color, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
            track.SizeChanged += (_, _) => fill.Width = track.ActualWidth * pct / 100.0;
            track.Children.Add(fill);
        }
        AutomationProperties.SetName(track, title + " " + (value is int n ? n + "%" : T("unknown")));
        Grid.SetRow(track, 1); grid.Children.Add(track);
        var resetLabel = Text(T("resets") + ": " + (reset?.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture) ?? T("unknown")), 10.5, muted: true);
        resetLabel.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(resetLabel, 2); grid.Children.Add(resetLabel);
        return grid;
    }
    private void Confirmation(StackPanel body)
    {
        if (target == null) return;
        var content = new StackPanel { Margin = new Thickness(20, 0, 20, 20) };
        if (target.Profile.LabeledEmail is { } email) {
            var address = Text(email, 12, muted: true);
            address.ToolTip = email;
            address.Margin = new Thickness(0, 0, 0, 8);
            content.Children.Add(address);
        }
        var copy = T(page == "switch" ? "switch_body_windows" : "remove_body_windows");
        var text = Text(copy, 13, muted: true); text.TextWrapping = TextWrapping.Wrap; text.Margin = new Thickness(0, 0, 0, 14); content.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Button(T("cancel"), () => Navigate(page == "remove" ? "manage" : "accounts")));
        var command = page; var id = target.Profile.Id;
        var confirm = Button(T(command), () => { Navigate(command == "remove" ? "manage" : "accounts"); _ = Run(command, id); });
        confirm.Margin = new Thickness(8, 0, 0, 0); confirm.BorderBrush = B("Accent"); confirm.IsDefault = true; buttons.Children.Add(confirm); content.Children.Add(buttons); body.Children.Add(content);
    }
    private ComboBox TrackedComboBox(string[] items, int selectedIndex)
    {
        var box = new ComboBox { Width = 160, VerticalAlignment = VerticalAlignment.Center, ItemsSource = items, SelectedIndex = selectedIndex };
        box.DropDownClosed += (_, _) => {
            if (pendingBackgroundRender) { pendingBackgroundRender = false; Render(preserveScroll: true); }
        };
        activeComboBoxes.Add(box);
        return box;
    }
    private void Settings(StackPanel body)
    {
        var settings = new StackPanel { Margin = new Thickness(20, 0, 20, 20) };
        settings.Children.Add(Text(T("settings_general"), 14, bold: true));
        void Toggle(string title, bool current, Action<bool> action) {
            var toggle = new CheckBox { IsChecked = current, Content = title, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 9, 0, 9) };
            AutomationProperties.SetName(toggle, title); toggle.Click += (_, _) => { try { action(toggle.IsChecked == true); } catch (Exception ex) { localError = ex.Message; Render(preserveScroll: true); } };
            settings.Children.Add(toggle);
        }
        Toggle(T("launch_at_login"), native.LaunchAtLogin, value => native.LaunchAtLogin = value);
        Toggle(T("show_tray_percentage"), State.Settings.ShowsMenuBarPercentage, value => _ = Run("percentage", value: value));
        var usageWindows = new[] { "fiveHour", "weekly" };
        var usageWindow = TrackedComboBox(new[] { T("five_hour"), T("weekly") }, State.Settings.StatusBarUsageWindow == "fiveHour" ? 0 : 1);
        usageWindow.SelectionChanged += (_, _) => { if (usageWindow.SelectedIndex >= 0) _ = Run("statusBarUsageWindow", language: usageWindows[usageWindow.SelectedIndex]); };
        var usageWindowRow = Pair(Text(T("status_bar_usage_window")), usageWindow);
        usageWindowRow.Height = 48; settings.Children.Add(usageWindowRow);
        settings.Children.Add(Text(T("both_windows"), 11, muted: true));
        settings.Children.Add(Pair(Text(T("language")), Text(T("language_fixed"), 12, bold: true)));
        var experimentHeading = Text(T("settings_automation"), 14, bold: true); experimentHeading.Margin = new Thickness(0, 20, 0, 0); settings.Children.Add(experimentHeading);
        Toggle(T("auto_reset"), State.Settings.AutoResetEnabled, value => _ = Run("autoReset", value: value));
        var experimentHint = Text(T("auto_reset_hint"), 10.5, muted: true); experimentHint.TextWrapping = TextWrapping.Wrap; settings.Children.Add(experimentHint);
        settings.Children.Add(Button(T("open_reset_log"), () => {
            try {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dataPath) { UseShellExecute = true });
            } catch (Exception ex) { localError = ex.Message; Render(preserveScroll: true); }
        }));
        var lowUsageItems = Enumerable.Range(0, 100).Select(percent => percent == 0 ? T("low_usage_off") : $"{percent}%").ToArray();
        var lowUsage = TrackedComboBox(lowUsageItems, State.Settings.LowUsageThreshold is int threshold and >= 1 and <= 99 ? threshold : 0);
        AutomationProperties.SetName(lowUsage, T("low_usage_reminder"));
        lowUsage.SelectionChanged += async (_, _) => {
            if (lowUsage.SelectedIndex < 0) return;
            try { await client.SetLowUsageThresholdAsync(lowUsage.SelectedIndex == 0 ? (int?)null : lowUsage.SelectedIndex); }
            catch (Exception ex) { localError = ex.Message; Render(preserveScroll: true); }
        };
        var reminderRow = Pair(Text(T("low_usage_reminder")), lowUsage);
        reminderRow.Height = 48;
        settings.Children.Add(reminderRow);
        var heading = Text(T("settings_updates"), 14, bold: true); heading.Margin = new Thickness(0, 20, 0, 0); settings.Children.Add(heading);
        var hint = Text(T("update_custom"), 11, muted: true); hint.TextWrapping = TextWrapping.Wrap; settings.Children.Add(hint);
        settings.Children.Add(Text(T("current_version").Replace("%@", native.Version) + " · VI", 12));
        body.Children.Add(settings);
    }
}
