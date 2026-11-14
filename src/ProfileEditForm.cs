using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GammaLift
{
    /// <summary>预设编辑：在副本上调整顺序、名称与删除项，只有确认后主窗体才统一提交。</summary>
    public class ProfileEditForm : Form
    {
        private const int DesignWidth = 520;
        private const int DesignHeight = 370;
        private const string DragFormat = "GammaLift.ProfileEdit";

        private class Entry
        {
            public Profile Profile;
            public string Name;

            public Entry(Profile profile, string name)
            {
                Profile = profile;
                Name = name ?? "";
            }
        }

        private readonly float _uiScale;
        private readonly float _dpiScale;
        private readonly List<Entry> _entries;
        private readonly Profile _activeProfile;
        private readonly bool _activeHasUnsavedChanges;
        private int _dragIndex = -1;
        private Point _dragStart;
        private bool _refreshing;
        private bool _updatingName;

        private Label lblHint;
        private ListBox lstProfiles;
        private Label lblName;
        private TextBox txtName;
        private Label lblError;
        private Button btnUp;
        private Button btnDown;
        private Button btnDelete;
        private Button btnOk;
        private Button btnCancel;

        /// <summary>对话框中的新顺序；元素仍是原 Profile 对象，不改变预设身份。</summary>
        public List<Profile> OrderedProfiles
        {
            get
            {
                List<Profile> result = new List<Profile>();
                foreach (Entry e in _entries) result.Add(e.Profile);
                return result;
            }
        }

        /// <summary>取指定 Profile 在对话框中的草稿名称；没有草稿项时返回其当前名称。</summary>
        public string NameFor(Profile profile)
        {
            foreach (Entry e in _entries)
                if (e.Profile == profile) return e.Name;
            return profile != null ? profile.Name : "";
        }

        public ProfileEditForm(IList<Profile> profiles, Profile selected, float uiScale, float dpiScale)
            : this(profiles, selected, false, uiScale, dpiScale)
        {
        }

        public ProfileEditForm(IList<Profile> profiles, Profile selected, bool activeHasUnsavedChanges,
            float uiScale, float dpiScale)
        {
            _entries = new List<Entry>();
            if (profiles != null)
                foreach (Profile p in profiles)
                    if (p != null) _entries.Add(new Entry(p, p.Name));
            _activeProfile = selected;
            _activeHasUnsavedChanges = activeHasUnsavedChanges;
            _uiScale = uiScale > 0f ? uiScale : 1f;
            _dpiScale = dpiScale > 0f ? dpiScale : 1f;

            Text = Loc.T("预设编辑");
            Font = Loc.UiFont(9f * _uiScale);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(S(DesignWidth), S(DesignHeight));

            BuildUi();
            LayoutForm();

            int index = selected != null ? IndexOf(selected) : -1;
            lstProfiles.SelectedIndex = index >= 0 ? index : (_entries.Count > 0 ? 0 : -1);
            UpdateSelectionUi();
            Shown += delegate { lstProfiles.Focus(); };
        }

        /// <summary>96 dpi 设计值 × 界面缩放 × 系统 DPI。</summary>
        private int S(int v)
        {
            return (int)Math.Round(v * _uiScale * _dpiScale);
        }

        private void BuildUi()
        {
            lblHint = new Label();
            lblHint.Text = Loc.T("拖动或使用「上移」「下移」调整顺序；可在下方修改名称，删除也在点「确定」后生效。「取消」会放弃全部修改。");
            lblHint.AutoSize = false;
            lblHint.UseMnemonic = false;
            Controls.Add(lblHint);

            lstProfiles = new ListBox();
            lstProfiles.SelectionMode = SelectionMode.One;
            lstProfiles.IntegralHeight = false;
            lstProfiles.HorizontalScrollbar = true;
            lstProfiles.AllowDrop = true;
            lstProfiles.AccessibleName = Loc.T("预设编辑");
            lstProfiles.SelectedIndexChanged += delegate { UpdateSelectionUi(); };
            lstProfiles.KeyDown += ListKeyDown;
            lstProfiles.MouseDown += ListMouseDown;
            lstProfiles.MouseMove += ListMouseMove;
            lstProfiles.DragEnter += ListDragOver;
            lstProfiles.DragOver += ListDragOver;
            lstProfiles.DragDrop += ListDragDrop;
            Controls.Add(lstProfiles);

            btnUp = NewButton("↑ " + Loc.T("上移"), delegate { MoveSelected(-1); });
            btnDown = NewButton("↓ " + Loc.T("下移"), delegate { MoveSelected(1); });
            btnDelete = NewButton(Loc.T("删除"), delegate { DeleteSelected(); });
            btnOk = NewButton(Loc.T("确定"), delegate { CommitAndClose(); });
            btnCancel = NewButton(Loc.T("取消"), null);
            btnCancel.DialogResult = DialogResult.Cancel;
            Controls.Add(btnUp);
            Controls.Add(btnDown);
            Controls.Add(btnDelete);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            lblName = new Label();
            lblName.Text = Loc.T("名称：");
            lblName.AutoSize = true;
            lblName.UseMnemonic = false;
            Controls.Add(lblName);

            txtName = new TextBox();
            txtName.TextChanged += delegate { NameChanged(); };
            Controls.Add(txtName);

            lblError = new Label();
            lblError.Text = "";
            lblError.AutoSize = false;
            lblError.UseMnemonic = false;
            lblError.ForeColor = Color.Firebrick;
            Controls.Add(lblError);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
            RefreshItems();
        }

        private Button NewButton(string text, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            if (onClick != null) b.Click += onClick;
            return b;
        }

        private void LayoutForm()
        {
            int margin = S(14);
            int gap = S(8);
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            int y = margin;
            int contentW = w - 2 * margin;

            Size hintSize = TextRenderer.MeasureText(lblHint.Text, lblHint.Font,
                new Size(contentW, int.MaxValue), TextFormatFlags.WordBreak);
            int hintH = Math.Max(Font.Height, hintSize.Height);
            lblHint.SetBounds(margin, y, contentW, hintH);
            y += hintH + S(10);

            int buttonH = Math.Max(S(26), Font.Height + S(10));
            int okW = ButtonWidth(btnOk, 64, 18);
            int cancelW = ButtonWidth(btnCancel, 64, 18);
            int footerY = h - margin - buttonH;
            btnOk.SetBounds(w - margin - cancelW - gap - okW, footerY, okW, buttonH);
            btnCancel.SetBounds(w - margin - cancelW, footerY, cancelW, buttonH);

            int errorH = Math.Max(S(17), Font.Height);
            int errorY = footerY - gap - errorH;
            lblError.SetBounds(margin, errorY, contentW, errorH);

            int nameH = Math.Max(S(24), Font.Height + S(8));
            int nameY = errorY - S(5) - nameH;
            int labelW = Math.Max(S(42), TextRenderer.MeasureText(lblName.Text, lblName.Font).Width);
            lblName.Location = new Point(margin, nameY + Math.Max(0, (nameH - lblName.PreferredSize.Height) / 2));
            txtName.SetBounds(margin + labelW, nameY, Math.Max(S(80), contentW - labelW), nameH);

            int moveW = Math.Max(ButtonWidth(btnUp, 76, 18), ButtonWidth(btnDown, 76, 18));
            moveW = Math.Max(moveW, ButtonWidth(btnDelete, 76, 18));
            int listW = Math.Max(S(120), contentW - gap - moveW);
            int listH = Math.Max(S(80), nameY - gap - y);
            lstProfiles.SetBounds(margin, y, listW, listH);

            int moveX = margin + listW + gap;
            btnUp.SetBounds(moveX, y, moveW, buttonH);
            btnDown.SetBounds(moveX, y + buttonH + gap, moveW, buttonH);
            btnDelete.SetBounds(moveX, y + (buttonH + gap) * 2, moveW, buttonH);
        }

        private int ButtonWidth(Button b, int minWidth, int padding)
        {
            Size sz = TextRenderer.MeasureText(b.Text, b.Font);
            int w = sz.Width + S(padding);
            int min = S(minWidth);
            return w < min ? min : w;
        }

        private int IndexOf(Profile profile)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Profile == profile) return i;
            return -1;
        }

        /// <summary>显示序号并刷新列表；顺序只存 Profile 引用，不按名称反查。</summary>
        private void RefreshItems()
        {
            _refreshing = true;
            int selected = lstProfiles.SelectedIndex;
            try
            {
                lstProfiles.BeginUpdate();
                lstProfiles.Items.Clear();
                int extent = 0;
                for (int i = 0; i < _entries.Count; i++)
                {
                    string text = FormatEntry(i);
                    lstProfiles.Items.Add(text);
                    extent = Math.Max(extent, TextRenderer.MeasureText(text, lstProfiles.Font).Width + S(6));
                }
                lstProfiles.HorizontalExtent = extent;
                if (_entries.Count == 0) lstProfiles.SelectedIndex = -1;
                else lstProfiles.SelectedIndex = Math.Max(0, Math.Min(selected, _entries.Count - 1));
            }
            finally
            {
                lstProfiles.EndUpdate();
                _refreshing = false;
            }
            UpdateSelectionUi();
        }

        private string FormatEntry(int index)
        {
            string suffix = _activeProfile != null && _entries[index].Profile == _activeProfile
                ? "  [" + Loc.T("当前") + "]" : "";
            return (index + 1) + ". " + _entries[index].Name + suffix;
        }

        private void UpdateListItem(int index)
        {
            if (index < 0 || index >= _entries.Count || index >= lstProfiles.Items.Count) return;
            _refreshing = true;
            try
            {
                string text = FormatEntry(index);
                lstProfiles.Items[index] = text;
                int extent = TextRenderer.MeasureText(text, lstProfiles.Font).Width + S(6);
                if (extent > lstProfiles.HorizontalExtent) lstProfiles.HorizontalExtent = extent;
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void UpdateSelectionUi()
        {
            if (lstProfiles == null || txtName == null) return;
            int index = lstProfiles.SelectedIndex;
            if (lblError != null) lblError.Text = "";
            _updatingName = true;
            txtName.Text = index >= 0 && index < _entries.Count ? _entries[index].Name : "";
            txtName.Enabled = index >= 0;
            _updatingName = false;
            btnUp.Enabled = index > 0;
            btnDown.Enabled = index >= 0 && index + 1 < _entries.Count;
            btnDelete.Enabled = index >= 0;
        }

        private void NameChanged()
        {
            if (_updatingName || _refreshing) return;
            int index = lstProfiles.SelectedIndex;
            if (index < 0 || index >= _entries.Count) return;
            _entries[index].Name = txtName.Text;
            lblError.Text = "";
            UpdateListItem(index);
        }

        private void MoveSelected(int delta)
        {
            int index = lstProfiles.SelectedIndex;
            int target = index + delta;
            if (index < 0 || target < 0 || target >= _entries.Count) return;

            Entry item = _entries[index];
            _entries.RemoveAt(index);
            _entries.Insert(target, item);
            RefreshItems();
            lstProfiles.SelectedIndex = target;
            lstProfiles.Focus();
        }

        private void DeleteSelected()
        {
            int index = lstProfiles.SelectedIndex;
            if (index < 0 || index >= _entries.Count) return;

            Entry entry = _entries[index];
            if (entry.Profile == _activeProfile && _activeHasUnsavedChanges)
            {
                using (ConfirmForm confirm = new ConfirmForm(
                    Loc.F("当前预设「{0}」有未保存的修改，删除后这些修改将丢失。仍要删除吗？", entry.Name),
                    Loc.Code, _uiScale, _dpiScale))
                {
                    if (confirm.ShowDialog(this) != DialogResult.Yes) return;
                }
            }

            _entries.RemoveAt(index);
            lblError.Text = "";
            RefreshItems();
            if (_entries.Count > 0) lstProfiles.SelectedIndex = Math.Min(index, _entries.Count - 1);
            lstProfiles.Focus();
        }

        private void CommitAndClose()
        {
            if (!ValidateNames()) return;
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>确定前统一去空格、拒绝空名与重名；错误留在窗口内修正。</summary>
        private bool ValidateNames()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _entries.Count; i++)
            {
                string name = (_entries[i].Name ?? "").Trim();
                if (name.Length == 0)
                {
                    lstProfiles.SelectedIndex = i;
                    EnsureItemVisible(i);
                    lblError.Text = Loc.T("预设名称不能为空。");
                    txtName.Focus();
                    txtName.SelectAll();
                    return false;
                }
                if (!seen.Add(name))
                {
                    lstProfiles.SelectedIndex = i;
                    EnsureItemVisible(i);
                    lblError.Text = Loc.F("已存在同名配置「{0}」, 请换一个名称。", name);
                    txtName.Focus();
                    txtName.SelectAll();
                    return false;
                }
                _entries[i].Name = name;
            }
            lblError.Text = "";
            return true;
        }

        /// <summary>ListBox 没有 EnsureVisible；按行高把目标项滚动到可视区。</summary>
        private void EnsureItemVisible(int index)
        {
            if (index < 0 || index >= lstProfiles.Items.Count) return;
            int rowH = Math.Max(1, lstProfiles.ItemHeight);
            int visibleRows = Math.Max(1, lstProfiles.ClientSize.Height / rowH);
            if (index < lstProfiles.TopIndex) lstProfiles.TopIndex = index;
            else if (index >= lstProfiles.TopIndex + visibleRows)
                lstProfiles.TopIndex = Math.Max(0, index - visibleRows + 1);
        }

        private void ListKeyDown(object sender, KeyEventArgs e)
        {
            if (!e.Alt || (e.KeyCode != Keys.Up && e.KeyCode != Keys.Down)) return;
            MoveSelected(e.KeyCode == Keys.Up ? -1 : 1);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void ListMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragIndex = lstProfiles.IndexFromPoint(e.Location);
            _dragStart = e.Location;
        }

        private void ListMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragIndex < 0 || (e.Button & MouseButtons.Left) == 0) return;
            Size drag = SystemInformation.DragSize;
            if (new Rectangle(_dragStart.X - drag.Width / 2, _dragStart.Y - drag.Height / 2,
                drag.Width, drag.Height).Contains(e.X, e.Y)) return;

            int index = _dragIndex;
            _dragIndex = -1;
            lstProfiles.SelectedIndex = index;
            DoDragDrop(new DataObject(DragFormat, index), DragDropEffects.Move);
        }

        private void ListDragOver(object sender, DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DragFormat))
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            e.Effect = DragDropEffects.Move;

            // 接近上下边缘时滚动，长列表也可以直接拖到当前可视区域之外的项目之间。
            Point p = lstProfiles.PointToClient(new Point(e.X, e.Y));
            int rowH = Math.Max(1, lstProfiles.ItemHeight);
            if (p.Y < rowH && lstProfiles.TopIndex > 0)
                lstProfiles.TopIndex--;
            else if (p.Y > lstProfiles.ClientSize.Height - rowH &&
                lstProfiles.TopIndex + lstProfiles.ClientSize.Height / rowH < lstProfiles.Items.Count)
                lstProfiles.TopIndex++;
        }

        private void ListDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data == null) return;
            object value = e.Data.GetData(DragFormat);
            if (!(value is int)) return;
            int source = (int)value;
            if (source < 0 || source >= _entries.Count) return;

            Point p = lstProfiles.PointToClient(new Point(e.X, e.Y));
            int target = lstProfiles.IndexFromPoint(p);
            if (target < 0) target = _entries.Count - 1;
            if (source == target) return;

            Entry item = _entries[source];
            _entries.RemoveAt(source);
            _entries.Insert(target, item);
            RefreshItems();
            lstProfiles.SelectedIndex = target;
        }
    }
}
