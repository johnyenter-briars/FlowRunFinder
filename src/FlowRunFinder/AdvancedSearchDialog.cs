using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FlowRunFinderV2.Core.Query;

namespace FlowRunFinder
{
    internal sealed class AdvancedSearchDialog : Form
    {
        private static readonly AdvancedSearchComparisonOperator[] ComparisonOperators =
        {
            AdvancedSearchComparisonOperator.Equals,
            AdvancedSearchComparisonOperator.Contains
        };

        private readonly IReadOnlyList<string> _fieldNames;
        private readonly AdvancedSearchGroup _rootGroup;
        private readonly DateTimePicker _startDate;
        private readonly DateTimePicker _startTime;
        private readonly DateTimePicker _endDate;
        private readonly DateTimePicker _endTime;
        private readonly Label _validation;
        private readonly Panel _filterBuilderPanel;

        public AdvancedSearchDialog(IEnumerable<string> triggerFieldNames, AdvancedSearchState initialState)
        {
            Text = "Advanced Search";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(760, 520);
            ClientSize = new Size(860, 640);
            MaximizeBox = true;
            MinimizeBox = false;

            _fieldNames = triggerFieldNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, AttributeNameComparer.Instance)
                .ToList();

            _rootGroup = initialState != null && initialState.Filter != null
                ? initialState.Filter.Clone() as AdvancedSearchGroup
                : new AdvancedSearchGroup();
            if (_rootGroup == null)
            {
                _rootGroup = new AdvancedSearchGroup();
            }

            if (_rootGroup.Children.Count == 0)
            {
                _rootGroup.Children.Add(new AdvancedSearchCondition());
            }

            var now = DateTimeOffset.UtcNow;
            var initialStart = (initialState != null && initialState.StartUtc.HasValue ? initialState.StartUtc.Value : now.AddHours(-1)).ToLocalTime();
            var initialEnd = (initialState != null && initialState.EndUtc.HasValue ? initialState.EndUtc.Value : now).ToLocalTime();

            _startDate = new DateTimePicker
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Location = new Point(130, 18),
                Width = 220,
                Format = DateTimePickerFormat.Short,
                Value = initialStart.LocalDateTime
            };
            _startTime = new DateTimePicker
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Location = new Point(_startDate.Right + 10, 18),
                Width = 110,
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "HH:mm",
                ShowUpDown = true,
                Value = initialStart.LocalDateTime
            };
            _endDate = new DateTimePicker
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Location = new Point(130, 52),
                Width = 220,
                Format = DateTimePickerFormat.Short,
                Value = initialEnd.LocalDateTime
            };
            _endTime = new DateTimePicker
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Location = new Point(_endDate.Right + 10, 52),
                Width = 110,
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "HH:mm",
                ShowUpDown = true,
                Value = initialEnd.LocalDateTime
            };

            _filterBuilderPanel = new Panel
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(18, 112),
                Size = new Size(ClientSize.Width - 36, ClientSize.Height - 205),
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };

            _validation = new Label
            {
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(18, ClientSize.Height - 82),
                Size = new Size(ClientSize.Width - 36, 34),
                ForeColor = Color.FromArgb(164, 38, 44)
            };

            var searchButton = new Button
            {
                Text = "Search",
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(ClientSize.Width - 180, ClientSize.Height - 42),
                Size = new Size(75, 28),
                DialogResult = DialogResult.None
            };
            var cancelButton = new Button
            {
                Text = "Cancel",
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(ClientSize.Width - 99, ClientSize.Height - 42),
                Size = new Size(75, 28),
                DialogResult = DialogResult.Cancel
            };
            searchButton.Click += SearchButton_Click;

            Controls.AddRange(new Control[]
            {
                MakeLabel("Start", 18, 20),
                _startDate,
                _startTime,
                MakeLabel("End", 18, 54),
                _endDate,
                _endTime,
                MakeHeader("Filters", 18, 86, 220),
                _filterBuilderPanel,
                _validation,
                searchButton,
                cancelButton
            });

            RenderFilterBuilder();

            AcceptButton = searchButton;
            CancelButton = cancelButton;
        }

        public AdvancedSearchRequest Request { get; private set; }

        private void RenderFilterBuilder()
        {
            _filterBuilderPanel.SuspendLayout();
            _filterBuilderPanel.Controls.Clear();

            var groupControl = CreateGroupControl(_rootGroup, null, 0);
            groupControl.Location = new Point(8, 8);
            groupControl.Width = Math.Max(680, _filterBuilderPanel.ClientSize.Width - 24);
            groupControl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _filterBuilderPanel.Controls.Add(groupControl);

            _filterBuilderPanel.ResumeLayout();
        }

        private Panel CreateGroupControl(AdvancedSearchGroup group, AdvancedSearchGroup parent, int depth)
        {
            var panel = new Panel
            {
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = depth == 0 ? Color.FromArgb(250, 250, 250) : Color.FromArgb(245, 247, 250),
                Padding = new Padding(10)
            };

            var y = 10;
            var logicalOperator = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(10, y),
                Size = new Size(86, 24)
            };
            logicalOperator.Items.Add(AdvancedSearchLogicalOperator.And);
            logicalOperator.Items.Add(AdvancedSearchLogicalOperator.Or);
            logicalOperator.SelectedItem = group.LogicalOperator;
            logicalOperator.SelectedIndexChanged += (sender, args) =>
            {
                if (logicalOperator.SelectedItem is AdvancedSearchLogicalOperator selected)
                {
                    group.LogicalOperator = selected;
                }
            };

            var title = new Label
            {
                Text = depth == 0 ? "Root group" : "Nested group",
                Location = new Point(106, y + 3),
                Size = new Size(140, 22),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            panel.Controls.Add(logicalOperator);
            panel.Controls.Add(title);

            if (parent != null)
            {
                var removeGroup = new Button
                {
                    Text = "Remove group",
                    Location = new Point(252, y - 1),
                    Size = new Size(105, 26)
                };
                removeGroup.Click += (sender, args) =>
                {
                    parent.Children.Remove(group);
                    EnsureRootHasChild();
                    RenderFilterBuilder();
                };
                panel.Controls.Add(removeGroup);
            }

            y += 38;

            foreach (var child in group.Children.ToList())
            {
                Panel childControl = null;
                var condition = child as AdvancedSearchCondition;
                if (condition != null)
                {
                    childControl = CreateConditionControl(group, condition);
                }
                else
                {
                    var childGroup = child as AdvancedSearchGroup;
                    if (childGroup != null)
                    {
                        childControl = CreateGroupControl(childGroup, group, depth + 1);
                    }
                }

                if (childControl == null)
                {
                    continue;
                }

                childControl.Location = new Point(depth == 0 ? 10 : 18, y);
                childControl.Width = Math.Max(610, _filterBuilderPanel.ClientSize.Width - 60 - (depth * 18));
                childControl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                panel.Controls.Add(childControl);
                y += childControl.Height + 8;
            }

            var addFilter = new Button
            {
                Text = "Add filter",
                Location = new Point(10, y),
                Size = new Size(82, 28)
            };
            addFilter.Click += (sender, args) =>
            {
                group.Children.Add(new AdvancedSearchCondition());
                RenderFilterBuilder();
            };

            var addGroup = new Button
            {
                Text = "Add group",
                Location = new Point(100, y),
                Size = new Size(82, 28)
            };
            addGroup.Click += (sender, args) =>
            {
                var childGroup = new AdvancedSearchGroup();
                childGroup.Children.Add(new AdvancedSearchCondition());
                group.Children.Add(childGroup);
                RenderFilterBuilder();
            };

            panel.Controls.Add(addFilter);
            panel.Controls.Add(addGroup);

            panel.Height = y + 40;
            return panel;
        }

        private Panel CreateConditionControl(AdvancedSearchGroup parent, AdvancedSearchCondition condition)
        {
            var panel = new Panel
            {
                Height = 34,
                BackColor = Color.Transparent
            };

            var field = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Location = new Point(0, 4),
                Size = new Size(260, 24),
                Text = condition.FieldName ?? string.Empty,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems
            };
            field.Items.AddRange(_fieldNames.Cast<object>().ToArray());
            field.TextChanged += (sender, args) => condition.FieldName = field.Text.Trim();
            field.SelectedIndexChanged += (sender, args) => condition.FieldName = field.Text.Trim();

            var comparison = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(270, 4),
                Size = new Size(110, 24)
            };
            comparison.Items.AddRange(ComparisonOperators.Cast<object>().ToArray());
            comparison.SelectedItem = condition.Operator;
            comparison.SelectedIndexChanged += (sender, args) =>
            {
                if (comparison.SelectedItem is AdvancedSearchComparisonOperator selected)
                {
                    condition.Operator = selected;
                }
            };

            var value = new TextBox
            {
                Location = new Point(390, 4),
                Size = new Size(210, 24),
                Text = condition.Value ?? string.Empty
            };
            value.TextChanged += (sender, args) => condition.Value = value.Text.Trim();

            var remove = new Button
            {
                Text = "Remove",
                Location = new Point(610, 3),
                Size = new Size(75, 26)
            };
            remove.Click += (sender, args) =>
            {
                parent.Children.Remove(condition);
                EnsureRootHasChild();
                RenderFilterBuilder();
            };

            panel.Controls.Add(field);
            panel.Controls.Add(comparison);
            panel.Controls.Add(value);
            panel.Controls.Add(remove);
            return panel;
        }

        private void SearchButton_Click(object sender, EventArgs e)
        {
            _validation.Text = string.Empty;

            var startUtc = GetSelectedUtc(_startDate, _startTime);
            var endUtc = GetSelectedUtc(_endDate, _endTime);

            if (endUtc < startUtc)
            {
                _validation.Text = "End date/time must be greater than or equal to Start date/time.";
                return;
            }

            var filter = (_rootGroup.Clone() as AdvancedSearchGroup) ?? new AdvancedSearchGroup();
            NormalizeGroup(filter);
            if (!TryValidateGroup(filter, out var validationMessage))
            {
                _validation.Text = validationMessage;
                return;
            }

            Request = new AdvancedSearchRequest(startUtc, endUtc, filter);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void EnsureRootHasChild()
        {
            if (_rootGroup.Children.Count == 0)
            {
                _rootGroup.Children.Add(new AdvancedSearchCondition());
            }
        }

        private static void NormalizeGroup(AdvancedSearchGroup group)
        {
            foreach (var childGroup in group.Children.OfType<AdvancedSearchGroup>().ToList())
            {
                NormalizeGroup(childGroup);
            }

            var emptyConditions = group.Children
                .OfType<AdvancedSearchCondition>()
                .Where(condition =>
                    string.IsNullOrWhiteSpace(condition.FieldName) &&
                    string.IsNullOrWhiteSpace(condition.Value))
                .Cast<AdvancedSearchFilterNode>()
                .ToList();

            foreach (var emptyCondition in emptyConditions)
            {
                group.Children.Remove(emptyCondition);
            }

            var emptyGroups = group.Children
                .OfType<AdvancedSearchGroup>()
                .Where(childGroup => childGroup.Children.Count == 0)
                .Cast<AdvancedSearchFilterNode>()
                .ToList();

            foreach (var emptyGroup in emptyGroups)
            {
                group.Children.Remove(emptyGroup);
            }
        }

        private static bool TryValidateGroup(AdvancedSearchGroup group, out string validationMessage)
        {
            if (group.Children.Count == 0)
            {
                validationMessage = "Add at least one filter.";
                return false;
            }

            foreach (var condition in group.Children.OfType<AdvancedSearchCondition>())
            {
                if (string.IsNullOrWhiteSpace(condition.FieldName))
                {
                    validationMessage = "Every filter needs a field.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(condition.Value))
                {
                    validationMessage = "Every filter needs a value.";
                    return false;
                }
            }

            foreach (var childGroup in group.Children.OfType<AdvancedSearchGroup>())
            {
                if (!TryValidateGroup(childGroup, out validationMessage))
                {
                    return false;
                }
            }

            validationMessage = string.Empty;
            return true;
        }

        private static DateTimeOffset GetSelectedUtc(DateTimePicker datePicker, DateTimePicker timePicker)
        {
            var selectedDate = datePicker.Value.Date;
            var selectedTime = timePicker.Value.TimeOfDay;
            var localDateTime = selectedDate.AddHours(selectedTime.Hours).AddMinutes(selectedTime.Minutes);
            var localValue = new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime));
            return localValue.ToUniversalTime();
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

        private static Label MakeHeader(string text, int x, int y, int width)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 20),
                ForeColor = Color.FromArgb(96, 94, 92),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
        }
    }
}
