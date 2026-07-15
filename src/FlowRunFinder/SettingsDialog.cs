using System;
using System.Drawing;
using System.Windows.Forms;
using FlowRunFinderV2.Core.Auth;
using FlowRunFinderV2.Core.Configuration;

namespace FlowRunFinder
{
    internal sealed class SettingsDialog : Form
    {
        private readonly NumericUpDown _defaultRunCount;
        private readonly NumericUpDown _maxRunsToQuery;
        private readonly CheckBox _useFlowRunHistoryTable;
        private readonly ComboBox _authenticationFlow;
        private readonly TextBox _dataverseClientId;
        private readonly TextBox _powerAutomateClientId;
        private readonly ComboBox _logVerbosity;
        private readonly Label _validation;

        public SettingsDialog(AppSettings settings)
        {
            Text = "Settings";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 350);

            var defaultRunCountLabel = MakeLabel("Default run count", 18, 22);
            _defaultRunCount = new NumericUpDown
            {
                Location = new Point(190, 18),
                Width = 320,
                Minimum = 1,
                Maximum = 100,
                Value = Math.Min(100, Math.Max(1, settings.DefaultRunCount))
            };

            var maxRunsToQueryLabel = MakeLabel("Max runs to query", 18, 62);
            _maxRunsToQuery = new NumericUpDown
            {
                Location = new Point(190, 58),
                Width = 320,
                Minimum = 1,
                Maximum = int.MaxValue,
                Increment = 100,
                Value = Math.Max(1, settings.MaxRunsToQuery)
            };

            _useFlowRunHistoryTable = new CheckBox
            {
                Text = "Use flow run history table",
                Location = new Point(18, 98),
                Size = new Size(492, 24),
                Checked = settings.UseFlowRunHistoryTable
            };

            var authenticationFlowLabel = MakeLabel("Auth flow", 18, 138);
            _authenticationFlow = new ComboBox
            {
                Location = new Point(190, 134),
                Width = 320,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _authenticationFlow.Items.AddRange(Enum.GetNames(typeof(AuthenticationFlow)));
            _authenticationFlow.SelectedItem = Enum.IsDefined(typeof(AuthenticationFlow), settings.AuthenticationFlow)
                ? settings.AuthenticationFlow.ToString()
                : AuthenticationFlow.InteractiveBrowser.ToString();

            var dataverseClientIdLabel = MakeLabel("Dataverse client ID", 18, 178);
            _dataverseClientId = new TextBox
            {
                Location = new Point(190, 174),
                Width = 320,
                Text = string.IsNullOrWhiteSpace(settings.DataverseClientId)
                    ? AuthenticationClientIds.PowerAutomate
                    : settings.DataverseClientId
            };

            var powerAutomateClientIdLabel = MakeLabel("Power Automate client ID", 18, 218);
            _powerAutomateClientId = new TextBox
            {
                Location = new Point(190, 214),
                Width = 320,
                Text = string.IsNullOrWhiteSpace(settings.PowerAutomateClientId)
                    ? AuthenticationClientIds.PowerAutomate
                    : settings.PowerAutomateClientId
            };

            var verbosityLabel = MakeLabel("Log verbosity", 18, 258);
            _logVerbosity = new ComboBox
            {
                Location = new Point(190, 254),
                Width = 320,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _logVerbosity.Items.AddRange(Enum.GetNames(typeof(LogVerbosity)));
            _logVerbosity.SelectedItem = settings.LogVerbosity.ToString();

            _validation = MakeLabel("", 18, 288);
            _validation.ForeColor = Color.FromArgb(164, 38, 44);
            _validation.Width = 492;

            var saveButton = new Button { Text = "Save", Location = new Point(354, 310), Size = new Size(75, 28), DialogResult = DialogResult.None };
            var cancelButton = new Button { Text = "Cancel", Location = new Point(435, 310), Size = new Size(75, 28), DialogResult = DialogResult.Cancel };
            saveButton.Click += SaveButton_Click;

            Controls.AddRange(new Control[]
            {
                defaultRunCountLabel, _defaultRunCount,
                maxRunsToQueryLabel, _maxRunsToQuery,
                _useFlowRunHistoryTable,
                authenticationFlowLabel, _authenticationFlow,
                dataverseClientIdLabel, _dataverseClientId,
                powerAutomateClientIdLabel, _powerAutomateClientId,
                verbosityLabel, _logVerbosity,
                _validation, saveButton, cancelButton
            });

            AcceptButton = saveButton;
            CancelButton = cancelButton;
        }

        public int DefaultRunCount { get; private set; }
        public int MaxRunsToQuery { get; private set; }
        public bool UseFlowRunHistoryTable { get; private set; }
        public AuthenticationFlow AuthenticationFlow { get; private set; }
        public string DataverseClientId { get; private set; }
        public string PowerAutomateClientId { get; private set; }
        public LogVerbosity LogVerbosity { get; private set; }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            _validation.Text = string.Empty;

            var dataverseClientId = _dataverseClientId.Text.Trim();
            if (!Guid.TryParse(dataverseClientId, out _))
            {
                _validation.Text = "Dataverse client ID must be a valid GUID.";
                return;
            }

            var powerAutomateClientId = _powerAutomateClientId.Text.Trim();
            if (!Guid.TryParse(powerAutomateClientId, out _))
            {
                _validation.Text = "Power Automate client ID must be a valid GUID.";
                return;
            }

            LogVerbosity verbosity;
            if (!Enum.TryParse(_logVerbosity.SelectedItem as string, out verbosity))
            {
                verbosity = LogVerbosity.Info;
            }

            AuthenticationFlow authenticationFlow;
            if (!Enum.TryParse(_authenticationFlow.SelectedItem as string, out authenticationFlow))
            {
                authenticationFlow = AuthenticationFlow.InteractiveBrowser;
            }

            DefaultRunCount = (int)_defaultRunCount.Value;
            MaxRunsToQuery = (int)_maxRunsToQuery.Value;
            UseFlowRunHistoryTable = _useFlowRunHistoryTable.Checked;
            AuthenticationFlow = authenticationFlow;
            DataverseClientId = dataverseClientId;
            PowerAutomateClientId = powerAutomateClientId;
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
                Size = new Size(166, 22),
                Font = new Font("Segoe UI", 9f)
            };
        }
    }
}
