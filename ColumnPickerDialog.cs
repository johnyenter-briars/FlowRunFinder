using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FlowRunFinder
{
    /// <summary>
    /// Modal dialog that lets the user pick which trigger / input fields
    /// to show as extra columns in the results grid.
    /// </summary>
    internal class ColumnPickerDialog : Form
    {
        private CheckedListBox clbColumns;
        private Button         btnApply;
        private Button         btnCancel;
        private TextBox        txtSearch;

        private readonly List<string> _allColumns;

        public List<string> SelectedColumns { get; private set; } = new List<string>();

        public ColumnPickerDialog(List<string> available, List<string> currentlySelected)
        {
            _allColumns = available;

            Text            = "Add / Remove Columns";
            Width           = 360;
            Height          = 500;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            StartPosition   = FormStartPosition.CenterParent;
            Font            = new Font("Segoe UI", 9f);

            // ── Header label ──────────────────────────────────────────────────
            var lblHdr = new Label
            {
                Text     = "Select trigger / input fields to add as columns:",
                Location = new Point(12, 12),
                Size     = new Size(320, 32),
                Font     = new Font("Segoe UI", 9f)
            };

            // ── Search box ────────────────────────────────────────────────────
            var lblSearch = new Label
            {
                Text     = "Filter:",
                Location = new Point(12, 50),
                AutoSize = true,
                Font     = new Font("Segoe UI", 8.5f)
            };

            txtSearch = new TextBox
            {
                Location = new Point(50, 47),
                Width    = 280,
                Font     = new Font("Segoe UI", 9f)
            };
            txtSearch.TextChanged += TxtSearch_TextChanged;

            // ── Checked list ──────────────────────────────────────────────────
            clbColumns = new CheckedListBox
            {
                Location     = new Point(12, 76),
                Size         = new Size(318, 340),
                CheckOnClick = true,
                Font         = new Font("Segoe UI", 9f),
                ScrollAlwaysVisible = true
            };

            PopulateList(available, currentlySelected);

            // ── Buttons ───────────────────────────────────────────────────────
            btnApply = new Button
            {
                Text      = "Apply",
                Location  = new Point(166, 426),
                Width     = 80,
                Height    = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 120, 212),
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
                DialogResult = DialogResult.OK
            };
            btnApply.FlatAppearance.BorderColor = Color.FromArgb(0, 100, 180);

            btnCancel = new Button
            {
                Text      = "Cancel",
                Location  = new Point(254, 426),
                Width     = 76,
                Height    = 26,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 9f),
                DialogResult = DialogResult.Cancel
            };
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(200, 198, 196);

            AcceptButton = btnApply;
            CancelButton = btnCancel;

            btnApply.Click += (s, e) =>
            {
                SelectedColumns = clbColumns.CheckedItems.Cast<string>().ToList();
            };

            Controls.AddRange(new Control[] { lblHdr, lblSearch, txtSearch, clbColumns, btnApply, btnCancel });
        }

        private void PopulateList(IEnumerable<string> columns, ICollection<string> selected)
        {
            clbColumns.Items.Clear();
            foreach (var col in columns)
                clbColumns.Items.Add(col, selected.Contains(col, StringComparer.OrdinalIgnoreCase));
        }

        private void TxtSearch_TextChanged(object sender, EventArgs e)
        {
            var q = txtSearch.Text.Trim();

            // Snapshot current checks before rebuilding
            var currentlyChecked = clbColumns.CheckedItems.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

            var filtered = string.IsNullOrEmpty(q)
                ? _allColumns
                : _allColumns.Where(c => c.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            PopulateList(filtered, currentlyChecked);
        }
    }
}
