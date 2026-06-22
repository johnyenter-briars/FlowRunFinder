using System;
using System.Drawing;
using System.Windows.Forms;
using FlowRunFinderV2.Core.Configuration;

namespace FlowRunFinder
{
    internal sealed class SettingsDialog : Form
    {
        private readonly NumericUpDown _defaultRunCount;
        private readonly ComboBox _logVerbosity;
        private readonly Label _validation;

        public SettingsDialog(AppSettings settings)
        {
            Text = "Settings";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(360, 170);

            var defaultRunCountLabel = MakeLabel("Default run count", 18, 22);
            _defaultRunCount = new NumericUpDown
            {
                Location = new Point(170, 18),
                Width = 150,
                Minimum = 1,
                Maximum = 100,
                Value = Math.Min(100, Math.Max(1, settings.DefaultRunCount))
            };

            var verbosityLabel = MakeLabel("Log verbosity", 18, 62);
            _logVerbosity = new ComboBox
            {
                Location = new Point(170, 58),
                Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _logVerbosity.Items.AddRange(Enum.GetNames(typeof(LogVerbosity)));
            _logVerbosity.SelectedItem = settings.LogVerbosity.ToString();

            _validation = MakeLabel("", 18, 100);
            _validation.ForeColor = Color.FromArgb(164, 38, 44);
            _validation.Width = 320;

            var saveButton = new Button { Text = "Save", Location = new Point(164, 128), Size = new Size(75, 28), DialogResult = DialogResult.None };
            var cancelButton = new Button { Text = "Cancel", Location = new Point(245, 128), Size = new Size(75, 28), DialogResult = DialogResult.Cancel };
            saveButton.Click += SaveButton_Click;

            Controls.AddRange(new Control[]
            {
                defaultRunCountLabel, _defaultRunCount,
                verbosityLabel, _logVerbosity,
                _validation, saveButton, cancelButton
            });

            AcceptButton = saveButton;
            CancelButton = cancelButton;
        }

        public int DefaultRunCount { get; private set; }
        public LogVerbosity LogVerbosity { get; private set; }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            LogVerbosity verbosity;
            if (!Enum.TryParse(_logVerbosity.SelectedItem as string, out verbosity))
            {
                verbosity = LogVerbosity.Info;
            }

            DefaultRunCount = (int)_defaultRunCount.Value;
            LogVerbosity = verbosity;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static Label MakeLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(140, 22),
                Font = new Font("Segoe UI", 9f)
            };
        }
    }
}
