using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FlowRunFinderV2.Core.Query;

namespace FlowRunFinder
{
    internal sealed class AdvancedSearchDialog : Form
    {
        private readonly ComboBox _field;
        private readonly ComboBox _operator;
        private readonly TextBox _value;
        private readonly TextBox _startUtc;
        private readonly TextBox _endUtc;
        private readonly Label _validation;

        public AdvancedSearchDialog(IEnumerable<string> triggerFieldNames, AdvancedSearchState initialState)
        {
            Text = "Advanced Search";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(620, 250);

            var fields = triggerFieldNames
                .OrderBy(name => name, AttributeNameComparer.Instance)
                .ToArray();

            _startUtc = new TextBox { Location = new Point(130, 18), Width = 440 };
            _endUtc = new TextBox { Location = new Point(130, 52), Width = 440 };
            _field = new ComboBox { Location = new Point(130, 100), Width = 210, DropDownStyle = ComboBoxStyle.DropDown };
            _operator = new ComboBox { Location = new Point(350, 100), Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            _value = new TextBox { Location = new Point(460, 100), Width = 110 };
            _validation = new Label { Location = new Point(18, 150), Size = new Size(552, 34), ForeColor = Color.FromArgb(164, 38, 44) };

            _field.Items.AddRange(fields);
            _operator.Items.Add("Equals");
            _operator.Items.Add("Contains");
            _operator.SelectedIndex = 0;

            var now = DateTimeOffset.UtcNow;
            _startUtc.Text = (initialState != null && initialState.StartUtc.HasValue ? initialState.StartUtc.Value : now.AddHours(-1)).ToString("O", CultureInfo.InvariantCulture);
            _endUtc.Text = (initialState != null && initialState.EndUtc.HasValue ? initialState.EndUtc.Value : now).ToString("O", CultureInfo.InvariantCulture);
            ApplyInitialCondition(initialState);

            var searchButton = new Button { Text = "Search", Location = new Point(414, 202), Size = new Size(75, 28), DialogResult = DialogResult.None };
            var cancelButton = new Button { Text = "Cancel", Location = new Point(495, 202), Size = new Size(75, 28), DialogResult = DialogResult.Cancel };
            searchButton.Click += SearchButton_Click;

            Controls.AddRange(new Control[]
            {
                MakeLabel("Start UTC", 18, 20), _startUtc,
                MakeLabel("End UTC", 18, 54), _endUtc,
                MakeHeader("Filter", 18, 78),
                _field, _operator, _value, _validation,
                searchButton, cancelButton
            });

            AcceptButton = searchButton;
            CancelButton = cancelButton;
        }

        public AdvancedSearchRequest Request { get; private set; }

        private void ApplyInitialCondition(AdvancedSearchState initialState)
        {
            if (initialState == null || initialState.Filter == null)
            {
                return;
            }

            var condition = initialState.Filter.Children.OfType<AdvancedSearchCondition>().FirstOrDefault();
            if (condition == null)
            {
                return;
            }

            _field.Text = condition.FieldName;
            _operator.SelectedItem = condition.Operator.ToString();
            _value.Text = condition.Value;
        }

        private void SearchButton_Click(object sender, EventArgs e)
        {
            _validation.Text = string.Empty;

            DateTimeOffset startUtc;
            DateTimeOffset endUtc;
            if (!TryParseUtc(_startUtc.Text, out startUtc))
            {
                _validation.Text = "Enter a valid Start UTC value.";
                return;
            }

            if (!TryParseUtc(_endUtc.Text, out endUtc))
            {
                _validation.Text = "Enter a valid End UTC value.";
                return;
            }

            if (endUtc < startUtc)
            {
                _validation.Text = "End UTC must be greater than or equal to Start UTC.";
                return;
            }

            if (string.IsNullOrWhiteSpace(_field.Text))
            {
                _validation.Text = "Select a trigger input field.";
                return;
            }

            if (string.IsNullOrWhiteSpace(_value.Text))
            {
                _validation.Text = "Enter a filter value.";
                return;
            }

            var filter = new AdvancedSearchGroup();
            filter.Children.Add(new AdvancedSearchCondition
            {
                FieldName = _field.Text.Trim(),
                Operator = string.Equals(_operator.SelectedItem as string, "Contains", StringComparison.OrdinalIgnoreCase)
                    ? AdvancedSearchComparisonOperator.Contains
                    : AdvancedSearchComparisonOperator.Equals,
                Value = _value.Text.Trim()
            });

            Request = new AdvancedSearchRequest(startUtc, endUtc, filter);
            DialogResult = DialogResult.OK;
            Close();
        }

        private static bool TryParseUtc(string text, out DateTimeOffset value)
        {
            value = default(DateTimeOffset);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            DateTimeOffset parsed;
            if (!DateTimeOffset.TryParse(
                text.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsed))
            {
                return false;
            }

            value = parsed.ToUniversalTime();
            return true;
        }

        private static Label MakeLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(100, 22),
                Font = new Font("Segoe UI", 9f)
            };
        }

        private static Label MakeHeader(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(100, 20),
                ForeColor = Color.FromArgb(96, 94, 92),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
        }
    }
}
