/*
 *  Duckr
 *  Licensed under GNU GPL v3. See LICENSE.
 *
 *  Setup window for Duckr (app lists, timing, hotkey). Built in code (no designer file) so it is
 *  easy to read and change. Shows live sound levels of every app, so you can
 *  see what the app hears and set the threshold by eye.
 */

namespace Duckr
{
    public sealed class SetupForm : Form
    {
        private readonly DuckEngine m_engine;
        private readonly bool m_dark;

        private readonly CheckBox m_enabled = new() { Text = "Duckr on", AutoSize = true };
        private readonly RadioButton m_scopeAll = new() { Text = "Lower all other apps (isolate the focused app)", AutoSize = true };
        private readonly RadioButton m_scopeMusic = new() { Text = "Lower only music apps", AutoSize = true };

        private readonly TrackBar m_level = new() { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = 220 };
        private readonly Label m_levelLabel = new() { AutoSize = true };
        private readonly NumericUpDown m_duckFade = Num(0, 10000, 50);
        private readonly NumericUpDown m_restoreFade = Num(0, 10000, 50);
        private readonly NumericUpDown m_attack = Num(0, 5000, 10);
        private readonly NumericUpDown m_release = Num(0, 30000, 100);
        private readonly NumericUpDown m_threshold = Num(-90, 0, 1);
        private readonly TextBox m_hotkey = new() { Width = 160 };

        private readonly TextBox m_musicApps = MakeListBox();
        private readonly TextBox m_neverApps = MakeListBox();
        private readonly TextBox m_focusOnlyApps = MakeListBox();

        private readonly ListView m_levels = new()
        {
            View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Height = 170, Dock = DockStyle.Fill, MultiSelect = false,
        };
        private readonly Label m_status = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
        private readonly System.Windows.Forms.Timer m_refresh = new() { Interval = 120 };
        private Font? m_boldFont;
        private Font BoldFont => m_boldFont ??= new Font(m_levels.Font, FontStyle.Bold);

        public SetupForm(DuckEngine engine, bool dark)
        {
            m_engine = engine;
            m_dark = dark;

            Text = "Duckr setup";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(640, 640);
            Size = new Size(720, 760);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            BuildLayout();
            LoadFrom(engine.Settings);

            m_level.ValueChanged += (_, _) => m_levelLabel.Text = m_level.Value == 0 ? "muted" : $"{m_level.Value}%";
            m_refresh.Tick += (_, _) => RefreshLive();
            Shown += (_, _) => m_refresh.Start();
            FormClosed += (_, _) => { m_refresh.Dispose(); m_boldFont?.Dispose(); };

            if (m_dark) ApplyDark(this);
        }

        // ---- layout -------------------------------------------------------------

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12), AutoScroll = true };
            Controls.Add(root);

            root.Controls.Add(m_enabled);
            root.Controls.Add(Hint("While you play something in the app you're focused on (Premiere, Resolve, a DAW, a player), " +
                                   "other apps fade down. When it stops, they come back."));

            // what to lower
            var scope = Group("What to lower");
            var scopeFlow = Flow();
            scopeFlow.FlowDirection = FlowDirection.TopDown;
            scopeFlow.Controls.Add(m_scopeAll);
            scopeFlow.Controls.Add(m_scopeMusic);
            scope.Controls.Add(scopeFlow);
            root.Controls.Add(scope);

            // levels and timing
            var timing = Group("Level and timing");
            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
            var levelRow = Flow();
            levelRow.Controls.Add(m_level);
            levelRow.Controls.Add(m_levelLabel);
            AddRow(grid, "Lower other apps to", levelRow);
            AddRow(grid, "Fade down (ms)", m_duckFade);
            AddRow(grid, "Fade back up (ms)", m_restoreFade);
            AddRow(grid, "Must play for (ms) before lowering", m_attack);
            AddRow(grid, "Wait after it stops (ms)", m_release);
            AddRow(grid, "Counts as playing above (dB)", m_threshold);
            AddRow(grid, "Force-lower hotkey", m_hotkey);
            timing.Controls.Add(grid);
            root.Controls.Add(timing);

            // app lists
            var lists = Group("Apps (one per line, process name without .exe, end with * to match the start)");
            var listGrid = new TableLayoutPanel { ColumnCount = 3, Dock = DockStyle.Fill, AutoSize = true };
            for (int i = 0; i < 3; i++) listGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            listGrid.Controls.Add(Caption("Music apps"), 0, 0);
            listGrid.Controls.Add(Caption("Never touch (calls, OBS)"), 1, 0);
            listGrid.Controls.Add(Caption("Lower while focused (ASIO DAWs)"), 2, 0);
            listGrid.Controls.Add(m_musicApps, 0, 1);
            listGrid.Controls.Add(m_neverApps, 1, 1);
            listGrid.Controls.Add(m_focusOnlyApps, 2, 1);
            lists.Controls.Add(listGrid);
            root.Controls.Add(lists);
            root.Controls.Add(Hint("DAWs using ASIO drivers skip the Windows mixer, so this app can't hear them play. " +
                                   "Put those in the last list: other apps get lowered the whole time the DAW is focused."));

            // live levels
            var live = Group("Live sound levels (select an app and use the buttons to add it to a list)");
            var liveGrid = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, AutoSize = true };
            m_levels.Columns.Add("App", 220);
            m_levels.Columns.Add("Level", 200);
            m_levels.Columns.Add("dB", 70);
            liveGrid.Controls.Add(m_status);
            liveGrid.Controls.Add(m_levels);
            var addRow = Flow();
            addRow.Controls.Add(MakeButton("Add to Music apps", (_, _) => AddSelectedTo(m_musicApps)));
            addRow.Controls.Add(MakeButton("Add to Never touch", (_, _) => AddSelectedTo(m_neverApps)));
            addRow.Controls.Add(MakeButton("Add to Lower while focused", (_, _) => AddSelectedTo(m_focusOnlyApps)));
            liveGrid.Controls.Add(addRow);
            live.Controls.Add(liveGrid);
            root.Controls.Add(live);

            // buttons
            var buttons = Flow();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Dock = DockStyle.Fill;
            var save = MakeButton("Save", (_, _) => SaveAndClose());
            var cancel = MakeButton("Cancel", (_, _) => Close());
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(MakeButton("Restore defaults", (_, _) => LoadFrom(new DuckrSettings())));
            root.Controls.Add(buttons);
            AcceptButton = save;
            CancelButton = cancel;
        }

        // ---- data ---------------------------------------------------------------

        private void LoadFrom(DuckrSettings s)
        {
            m_enabled.Checked = s.Enabled;
            m_scopeAll.Checked = s.Scope == DuckScope.AllOtherApps;
            m_scopeMusic.Checked = s.Scope == DuckScope.MusicAppsOnly;
            m_level.Value = Math.Clamp(s.DuckLevelPercent, 0, 100);
            m_levelLabel.Text = m_level.Value == 0 ? "muted" : $"{m_level.Value}%";
            m_duckFade.Value = s.DuckFadeMs;
            m_restoreFade.Value = s.RestoreFadeMs;
            m_attack.Value = s.AttackMs;
            m_release.Value = s.ReleaseMs;
            m_threshold.Value = s.ThresholdDb;
            m_hotkey.Text = s.ForceDuckHotkey;
            m_musicApps.Lines = s.MusicApps.ToArray();
            m_neverApps.Lines = s.NeverTouchApps.ToArray();
            m_focusOnlyApps.Lines = s.FocusOnlyApps.ToArray();
        }

        private DuckrSettings ReadForm() => new DuckrSettings
        {
            Enabled = m_enabled.Checked,
            Scope = m_scopeMusic.Checked ? DuckScope.MusicAppsOnly : DuckScope.AllOtherApps,
            DuckLevelPercent = m_level.Value,
            DuckFadeMs = (int)m_duckFade.Value,
            RestoreFadeMs = (int)m_restoreFade.Value,
            AttackMs = (int)m_attack.Value,
            ReleaseMs = (int)m_release.Value,
            ThresholdDb = (int)m_threshold.Value,
            ForceDuckHotkey = m_hotkey.Text.Trim(),
            MusicApps = Lines(m_musicApps),
            NeverTouchApps = Lines(m_neverApps),
            FocusOnlyApps = Lines(m_focusOnlyApps),
        }.Sanitized();

        private void SaveAndClose()
        {
            var s = ReadForm();
            if (s.ForceDuckHotkey.Length > 0 && !HotkeyWindow.TryParse(s.ForceDuckHotkey, out _, out _))
            {
                MessageBox.Show(this, "The hotkey should look like Ctrl+Alt+D or Ctrl+Shift+F9 (needs at least one of Ctrl, Alt, Shift, Win).",
                    "Hotkey", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                s.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save settings: " + ex.Message, "Save", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            SettingsSaved?.Invoke(this, s);
            Close();
        }

        /// <summary>Raised with the new settings after a successful save.</summary>
        public event EventHandler<DuckrSettings>? SettingsSaved;

        private void AddSelectedTo(TextBox list)
        {
            if (m_levels.SelectedItems.Count == 0) return;
            string name = m_levels.SelectedItems[0].Text;
            var lines = Lines(list);
            if (!DuckrSettings.Matches(lines, name))
            {
                lines.Add(name);
                list.Lines = lines.ToArray();
            }
        }

        private void RefreshLive()
        {
            if (!m_engine.IsRunning)
            {
                m_status.Text = "Duckr is off. Turn it on and save to see live levels.";
                return;
            }

            string forced = m_engine.ForceDuck ? " (forced by hotkey)" : "";
            string focused = m_engine.FocusedApp.Length > 0 ? m_engine.FocusedApp : "(none)";
            m_status.Text = $"Focused: {focused}    State: {m_engine.State}{forced}";

            string? selected = m_levels.SelectedItems.Count > 0 ? m_levels.SelectedItems[0].Text : null;
            float thresholdDb = (float)m_threshold.Value;

            m_levels.BeginUpdate();
            m_levels.Items.Clear();
            foreach (var (name, peak) in m_engine.SnapshotLevels())
            {
                if (string.IsNullOrEmpty(name)) continue;
                double db = peak > 0 ? 20 * Math.Log10(peak) : -90;
                int bars = (int)Math.Round(Math.Clamp((db + 60) / 60, 0, 1) * 24);
                var item = new ListViewItem(name);
                item.SubItems.Add(new string('|', bars));
                item.SubItems.Add(peak > 0 ? $"{db:0}" : "-inf");
                if (db >= thresholdDb) item.ForeColor = Color.SeaGreen;
                if (name.Equals(m_engine.FocusedApp, StringComparison.OrdinalIgnoreCase)) item.Font = BoldFont;
                m_levels.Items.Add(item);
                if (name == selected) item.Selected = true;
            }
            m_levels.EndUpdate();
        }

        // ---- small helpers ------------------------------------------------------

        private static List<string> Lines(TextBox box) =>
            box.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private static NumericUpDown Num(int min, int max, int step) =>
            new() { Minimum = min, Maximum = max, Increment = step, Width = 90 };

        private static TextBox MakeListBox() =>
            new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 120, Dock = DockStyle.Fill, AcceptsReturn = true };

        private static GroupBox Group(string title) =>
            new() { Text = title, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };

        private static FlowLayoutPanel Flow() =>
            new() { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill };

        private static Label Caption(string text) => new() { Text = text, AutoSize = true };

        private static Label Hint(string text) =>
            new() { Text = text, AutoSize = true, MaximumSize = new Size(660, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 2, 3, 8) };

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
            b.Click += onClick;
            return b;
        }

        private static void AddRow(TableLayoutPanel grid, string label, Control control)
        {
            int row = grid.RowCount++;
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 12, 3) }, 0, row);
            grid.Controls.Add(control, 1, row);
        }

        /// <summary>Skin colors: gunmetal body, gold labels, green-on-black fields.</summary>
        private static void ApplyDark(Control parent)
        {
            var back = Color.FromArgb(40, 42, 60);
            var field = Color.FromArgb(4, 10, 6);
            var green = Color.FromArgb(0, 236, 96);
            var gold = Color.FromArgb(232, 196, 104);
            var hint = Color.FromArgb(160, 150, 120);

            switch (parent)
            {
                case TextBox or ListView or NumericUpDown:
                    parent.BackColor = field;
                    parent.ForeColor = green;
                    break;
                case Button b:
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = Color.FromArgb(128, 134, 168);
                    b.BackColor = Color.FromArgb(70, 74, 98);
                    b.ForeColor = gold;
                    break;
                case TrackBar:
                    parent.BackColor = back;
                    break;
                case Label l when l.ForeColor == SystemColors.GrayText:
                    l.BackColor = back;
                    l.ForeColor = hint;
                    break;
                default:
                    parent.BackColor = back;
                    parent.ForeColor = gold;
                    break;
            }
            foreach (Control c in parent.Controls) ApplyDark(c);
        }
    }
}
