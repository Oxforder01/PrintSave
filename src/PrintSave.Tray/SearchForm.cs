using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using PrintSave.Index;

namespace PrintSave.Tray
{
    /// <summary>Searches the metadata index and opens results. Read-only against the live DB.</summary>
    internal sealed class SearchForm : Form
    {
        private readonly string _root;
        private readonly TextBox _query;
        private readonly ListView _list;

        public SearchForm(string root)
        {
            _root = root;
            Text = "PrintSave — Search";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 480);
            MinimumSize = new Size(520, 320);
            Icon = TrayIcon.Build();

            _query = new TextBox
            {
                Left = 12, Top = 12, Width = 620,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            var btn = new Button
            {
                Text = "Search", Left = 640, Top = 10, Width = 108,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _list = new ListView
            {
                Left = 12, Top = 48, Width = 736, Height = 400,
                View = View.Details, FullRowSelect = true, GridLines = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            _list.Columns.Add("When", 130);
            _list.Columns.Add("Document", 270);
            _list.Columns.Add("Printer", 160);
            _list.Columns.Add("Pages", 50);
            _list.Columns.Add("Format", 60);

            var hint = new Label
            {
                Text = "Type words to match; blank lists everything. Double-click a row to open it.",
                Left = 12, Top = 456, AutoSize = true, ForeColor = Color.Gray,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            btn.Click += (s, e) => DoSearch();
            _query.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { DoSearch(); e.SuppressKeyPress = true; } };
            _list.DoubleClick += (s, e) => OpenSelected();

            Controls.AddRange(new Control[] { _query, btn, _list, hint });
            Shown += (s, e) => { _query.Focus(); DoSearch(); };
        }

        private void DoSearch()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            try
            {
                string db = Path.Combine(_root, ".state", "index.db");
                if (!File.Exists(db))
                {
                    Text = "PrintSave — Search (no documents yet)";
                    return;
                }
                using (var ix = new PrintIndex(_root, null, true))
                {
                    var hits = ix.Search(new SearchQuery { Text = _query.Text, Limit = 500 });
                    foreach (IndexedDocument d in hits)
                    {
                        var it = new ListViewItem(d.PrintedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                        it.SubItems.Add(d.Document ?? "");
                        it.SubItems.Add(d.Printer ?? "");
                        it.SubItems.Add(d.Pages.HasValue ? d.Pages.Value.ToString() : "");
                        it.SubItems.Add(d.SavedFormat ?? "");
                        it.Tag = d.SavedPath;
                        _list.Items.Add(it);
                    }
                    Text = "PrintSave — Search (" + hits.Count + " result" + (hits.Count == 1 ? "" : "s") + ")";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Search error");
            }
            finally { _list.EndUpdate(); }
        }

        private void OpenSelected()
        {
            if (_list.SelectedItems.Count == 0) return;
            string path = _list.SelectedItems[0].Tag as string;
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path))
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                else
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Open error"); }
        }
    }
}
