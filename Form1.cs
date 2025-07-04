using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Concurrent;
using StackExchange.Redis;
using Newtonsoft.Json;

namespace Suntech_Alert
{
    public partial class Form1 : Form
    {
        private ConnectionMultiplexer redisConnection;
        private ISubscriber redisSubscriber;
        private string redisServer = "localhost";
        private int redisPort = 6379;
        private string channel = "sqlserver:waitstats";
        private bool isConnected = false;
        
        // Cache for metrics data
        private readonly ConcurrentDictionary<string, Dictionary<string, object>> metricsCache = 
            new ConcurrentDictionary<string, Dictionary<string, object>>();
        
        // This will hold our metrics for DataGridView
        private readonly DataTable metricsTable = new DataTable();

        // Add these fields to the Form1 class
        private int warningThreshold = 10000;
        private int alertThreshold = 40000;
        private TrackBar trkWarningThreshold;
        private TrackBar trkAlertThreshold;
        private NumericUpDown numWarningThreshold;
        private NumericUpDown numAlertThreshold;
        private GroupBox grpThresholds;
        private Label lblWarningThreshold;
        private Label lblAlertThreshold;

        public Form1()
        {
            InitializeComponent();
            LoadThresholdSettings(); // Add this line before initializing controls
            InitializeMetricsTable();
            InitializeThresholdControls();
            txtChannel.Text = channel;          
        }

        private void InitializeMetricsTable()
        {
            metricsTable.Columns.Add("MetricType", typeof(string));
            metricsTable.Columns.Add("SubMetric", typeof(string));
            metricsTable.Columns.Add("Value", typeof(string));
            metricsTable.Columns.Add("Tasks", typeof(string));
            metricsTable.Columns.Add("AvgWaitMs", typeof(double));
            metricsTable.Columns.Add("Status", typeof(string));
            metricsTable.Columns.Add("CaptureTime", typeof(string));
            
            // Configure column styles
            dgvMetrics.AutoGenerateColumns = false;
            dgvMetrics.DataSource = metricsTable;

            // Configure columns
            if (dgvMetrics.Columns.Count == 0)
            {
                dgvMetrics.Columns.Add(new DataGridViewTextBoxColumn
                {
                    DataPropertyName = "MetricType",
                    HeaderText = "Metric Type",
                    Width = 100
                });

                dgvMetrics.Columns.Add(new DataGridViewTextBoxColumn
                {
                    DataPropertyName = "SubMetric",
                    HeaderText = "Sub Metric",
                    Width = 180
                });

                dgvMetrics.Columns.Add(new DataGridViewTextBoxColumn
                {
                    DataPropertyName = "Value",
                    HeaderText = "Wait Time (ms)",
                    Width = 100
                });

                dgvMetrics.Columns.Add(new DataGridViewTextBoxColumn
                {
                    DataPropertyName = "Tasks",
                    HeaderText = "Tasks",
                    Width = 60
                });

                dgvMetrics.Columns.Add(new DataGridViewTextBoxColumn
                {
                    DataPropertyName = "AvgWaitMs",
                    HeaderText = "Avg Wait (ms)",
                    Width = 100
                });

                dgvMetrics.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "Status",
                    DataPropertyName = "Status",
                    HeaderText = "Status",
                    Width = 120
                });

                dgvMetrics.Columns.Add(new DataGridViewTextBoxColumn
                {
                    DataPropertyName = "CaptureTime",
                    HeaderText = "Capture Time",
                    Width = 140
                });
            }

            // Row-level formatting
            dgvMetrics.CellFormatting += DgvMetrics_CellFormatting;
            dgvMetrics.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        }

        private void DgvMetrics_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;

            var row = dgvMetrics.Rows[e.RowIndex];
            string status = Convert.ToString(row.Cells["Status"].Value);

            if (status == "Alert")
            {
                e.CellStyle.BackColor = Color.LightCoral;
                e.CellStyle.ForeColor = Color.Black;
                e.CellStyle.Font = new Font(e.CellStyle.Font, FontStyle.Bold);
            }
            else if (status == "Warning")
            {
                e.CellStyle.BackColor = Color.LightYellow;
                e.CellStyle.ForeColor = Color.Black;
            }
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            if (!isConnected)
            {
                Connect();
            }
            else
            {
                Disconnect();
            }
        }
        
        private void Connect()
        {
            try
            {
                // Parse server and port
                string server = txtRedisServer.Text.Trim();
                string[] parts = server.Split(':');
                redisServer = parts[0];
                redisPort = parts.Length > 1 ? Convert.ToInt32(parts[1]) : 6379;
                channel = txtChannel.Text.Trim();

                // Connect to Redis using StackExchange.Redis with connection string
                string connectionString = $"{redisServer}:{redisPort}";
                ConfigurationOptions config = ConfigurationOptions.Parse(connectionString);
                redisConnection = ConnectionMultiplexer.Connect(config);
                redisSubscriber = redisConnection.GetSubscriber();

                // Subscribe to channel
                redisSubscriber.Subscribe(channel, (ch, message) =>
                {
                    BeginInvoke(new Action(() =>
                    {
                        string messageValue = message.ToString();
                        LogMessage($"Received message: {messageValue}");
                        
                        // If the message is a JSON array, process it as wait stats
                        if (messageValue.TrimStart().StartsWith("["))
                        {
                            ProcessWaitStatsArray(messageValue);
                        }
                        else
                        {
                            ProcessRedisMessage(messageValue);
                        }
                    }));
                });

                // Update UI
                isConnected = true;
                btnConnect.Text = "Disconnect";
                lblStatus.Text = "Connected";
                lblStatus.ForeColor = Color.Green;

                LogMessage($"Connected to {redisServer}:{redisPort} and subscribed to {channel}");
                timer1.Start();
            }
            catch (Exception ex)
            {
                LogMessage($"Error connecting: {ex.Message}");
                Disconnect();
            }
        }

        private void Disconnect()
        {
            try
            {
                isConnected = false;
                btnConnect.Text = "Connect";
                lblStatus.Text = "Not Connected";
                lblStatus.ForeColor = Color.Black;
                timer1.Stop();

                // Unsubscribe and cleanup
                if (redisSubscriber != null)
                {
                    redisSubscriber.UnsubscribeAll();
                    redisSubscriber = null;
                }
                if (redisConnection != null)
                {
                    redisConnection.Close();
                    redisConnection.Dispose();
                    redisConnection = null;
                }

                LogMessage("Disconnected from Redis");
            }
            catch (Exception ex)
            {
                LogMessage($"Error disconnecting: {ex.Message}");
            }
        }

        // Handles logging messages to the UI
        private void LogMessage(string message)
        {
            // Ensure we update the UI on the right thread
            if (rtbMessages.InvokeRequired)
            {
                BeginInvoke(new Action<string>(LogMessage), message);
                return;
            }

            // Add timestamp to the message
            string timestampedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
            
            // Append to RichTextBox
            rtbMessages.AppendText(timestampedMessage + Environment.NewLine);
            
            // Auto-scroll to bottom
            rtbMessages.SelectionStart = rtbMessages.Text.Length;
            rtbMessages.ScrollToCaret();
        }

        // Handles processing of Redis protocol messages
        private void ProcessRedisMessage(string data)
        {
            // Implement Redis protocol message handling here
            LogMessage("Received Redis protocol message: " + data);
        }

        // Class for JSON deserialization
        public class Metric
        {
            [JsonProperty("metric_type")]
            public string MetricType { get; set; }
            
            [JsonProperty("sub_metric")]
            public string SubMetric { get; set; }
            
            [JsonProperty("value")]
            public string Value { get; set; }
            
            [JsonProperty("extra_info")]
            public string ExtraInfo { get; set; }
            
            [JsonProperty("capture_time")]
            public string CaptureTime { get; set; }
        }

        // Handles processing of JSON array wait stats
        private void ProcessWaitStatsArray(string jsonMessage)
        {
            try
            {
                // Use Newtonsoft.Json to parse the JSON
                var metrics = JsonConvert.DeserializeObject<List<Metric>>(jsonMessage);
                
                if (metrics == null || metrics.Count == 0)
                {
                    LogMessage("No metrics found in JSON message");
                    return;
                }

                // Clear the current table data
                metricsTable.Clear();
                
                foreach (var metric in metrics)
                {
                    string tasks = string.Empty;
                    double avgWaitMs = 0;
                    
                    if (!string.IsNullOrEmpty(metric.ExtraInfo))
                    {
                        // Parse extra_info for tasks and avg_wait
                        string[] parts = metric.ExtraInfo.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string part in parts)
                        {
                            if (part.Contains("tasks="))
                            {
                                tasks = part.Split('=')[1].Trim();
                            }
                            else if (part.Contains("avg_wait="))
                            {
                                string avgWaitStr = part.Split('=')[1].Trim();
                                double.TryParse(avgWaitStr, out avgWaitMs);
                            }
                        }
                    }
                    
                    // Determine status (this is a simple example - you may want to customize thresholds)
                    string status = "Normal";
                    if (metric.MetricType == "WaitStats")
                    {
                        // If it's a wait stat, check the value
                        double waitValue = 0;
                        double.TryParse(metric.Value, out waitValue);
                        
                        if (waitValue > alertThreshold) // threshold
                        {
                            status = "Alert";
                        }
                        else if (waitValue > warningThreshold) // threshold
                        {
                            status = "Warning";
                        }
                    }

                    // Add to DataTable
                    DataRow row = metricsTable.NewRow();
                    row["MetricType"] = metric.MetricType;
                    row["SubMetric"] = metric.SubMetric;
                    row["Value"] = metric.Value;
                    row["Tasks"] = tasks;
                    row["AvgWaitMs"] = avgWaitMs;
                    row["Status"] = status;
                    row["CaptureTime"] = metric.CaptureTime;
                    
                    metricsTable.Rows.Add(row);
                    
                    // Cache the metric
                    string key = $"{metric.MetricType}:{metric.SubMetric}";
                    var metricDict = new Dictionary<string, object>
                    {
                        { "metric_type", metric.MetricType },
                        { "sub_metric", metric.SubMetric },
                        { "value", metric.Value },
                        { "extra_info", metric.ExtraInfo },
                        { "capture_time", metric.CaptureTime }
                    };
                    metricsCache[key] = metricDict;
                }
                
                // Refresh UI
                dgvMetrics.Refresh();
                LogMessage($"Updated grid with {metrics.Count} metrics");
            }
            catch (Exception ex)
            {
                LogMessage($"Error processing wait stats: {ex.Message}");
            }
        }

        // Handles timer tick for refreshing the grid
        private void timer1_Tick(object sender, EventArgs e)
        {
            // Just refresh the grid display
            dgvMetrics.Refresh();
        }

        // InitializeThresholdControls method to create the threshold controls
        private void InitializeThresholdControls()
        {
            // Create a group box with proper sizing and positioning
            // Position it below the DataGridView with adequate spacing
            grpThresholds = new GroupBox();
            grpThresholds.Text = "Wait Time Thresholds";
            grpThresholds.Dock = DockStyle.Bottom;
            grpThresholds.Height = 110; // Make it compact
            grpThresholds.Padding = new Padding(5);
            
            // Create a flow layout panel for warning threshold
            FlowLayoutPanel pnlWarning = new FlowLayoutPanel();
            pnlWarning.FlowDirection = FlowDirection.LeftToRight;
            pnlWarning.AutoSize = true;
            pnlWarning.WrapContents = false;
            pnlWarning.Dock = DockStyle.Top;
            pnlWarning.Padding = new Padding(0, 5, 0, 0);
            
            // Warning threshold label
            lblWarningThreshold = new Label();
            lblWarningThreshold.Text = "Warning:";
            lblWarningThreshold.AutoSize = true;
            lblWarningThreshold.TextAlign = ContentAlignment.MiddleLeft;
            lblWarningThreshold.Width = 60;
            
            // Warning threshold slider - use a shorter, more compact slider
            trkWarningThreshold = new TrackBar();
            trkWarningThreshold.Minimum = 0;
            trkWarningThreshold.Maximum = 50000;
            trkWarningThreshold.SmallChange = 1000;
            trkWarningThreshold.LargeChange = 5000;
            trkWarningThreshold.TickFrequency = 1000000;
            trkWarningThreshold.Value = warningThreshold;
            trkWarningThreshold.Width = 200; // Make it smaller
            trkWarningThreshold.Height = 30; // Make it smaller vertically
            trkWarningThreshold.AutoSize = false; // Don't allow it to automatically resize
            trkWarningThreshold.Scroll += TrkWarningThreshold_Scroll;
            
            // Warning threshold numeric input
            numWarningThreshold = new NumericUpDown();
            numWarningThreshold.Minimum = 0;
            numWarningThreshold.Maximum = 5000000;
            numWarningThreshold.Increment = 1000;
            numWarningThreshold.ThousandsSeparator = true;
            numWarningThreshold.Value = warningThreshold;
            numWarningThreshold.Width = 70;
            numWarningThreshold.ValueChanged += NumWarningThreshold_ValueChanged;
            
            // Color indicator for warning
            Panel pnlWarningColor = new Panel();
            pnlWarningColor.BackColor = Color.LightYellow;
            pnlWarningColor.Width = 16;
            pnlWarningColor.Height = 16;
            pnlWarningColor.BorderStyle = BorderStyle.FixedSingle;
            pnlWarningColor.Margin = new Padding(5, 3, 0, 0);
            
            // Add warning controls to panel
            pnlWarning.Controls.Add(lblWarningThreshold);
            pnlWarning.Controls.Add(trkWarningThreshold);
            pnlWarning.Controls.Add(numWarningThreshold);
            pnlWarning.Controls.Add(pnlWarningColor);
            
            // Create a flow layout panel for alert threshold
            FlowLayoutPanel pnlAlert = new FlowLayoutPanel();
            pnlAlert.FlowDirection = FlowDirection.LeftToRight;
            pnlAlert.AutoSize = true;
            pnlAlert.WrapContents = false;
            pnlAlert.Dock = DockStyle.Top;
            pnlAlert.Padding = new Padding(0, 10, 0, 0);
            
            // Alert threshold label
            lblAlertThreshold = new Label();
            lblAlertThreshold.Text = "Alert:";
            lblAlertThreshold.AutoSize = true;
            lblAlertThreshold.TextAlign = ContentAlignment.MiddleLeft;
            lblAlertThreshold.Width = 60;
            
            // Alert threshold slider - more compact
            trkAlertThreshold = new TrackBar();
            trkAlertThreshold.Minimum = 0;
            trkAlertThreshold.Maximum = 5000000;
            trkAlertThreshold.SmallChange = 500;
            trkAlertThreshold.LargeChange = 1000;
            trkAlertThreshold.TickFrequency = 500;
            trkAlertThreshold.Value = alertThreshold;
            trkAlertThreshold.Width = 200; // Make it smaller
            trkAlertThreshold.Height = 30; // Make it smaller vertically
            trkAlertThreshold.AutoSize = false; // Don't allow it to automatically resize
            trkAlertThreshold.Scroll += TrkAlertThreshold_Scroll;
            
            // Alert threshold numeric input
            numAlertThreshold = new NumericUpDown();
            numAlertThreshold.Minimum = 0;
            numAlertThreshold.Maximum = 5000000;
            numAlertThreshold.Increment = 500;  
            numAlertThreshold.ThousandsSeparator = true;
            numAlertThreshold.Value = alertThreshold;
            numAlertThreshold.Width = 70;
            numAlertThreshold.ValueChanged += NumAlertThreshold_ValueChanged;
            
            // Color indicator for alert
            Panel pnlAlertColor = new Panel();
            pnlAlertColor.BackColor = Color.LightCoral;
            pnlAlertColor.Width = 16;
            pnlAlertColor.Height = 16;
            pnlAlertColor.BorderStyle = BorderStyle.FixedSingle;
            pnlAlertColor.Margin = new Padding(5, 3, 0, 0);
            
            // Add alert controls to panel
            pnlAlert.Controls.Add(lblAlertThreshold);
            pnlAlert.Controls.Add(trkAlertThreshold);
            pnlAlert.Controls.Add(numAlertThreshold);
            pnlAlert.Controls.Add(pnlAlertColor);
            
            // Reset button positioned in the bottom right
            Button btnResetThresholds = new Button();
            btnResetThresholds.Text = "Reset Defaults";
            btnResetThresholds.Dock = DockStyle.Bottom;
            btnResetThresholds.Height = 23;
            btnResetThresholds.Click += BtnResetThresholds_Click;
            
            // Add tooltips
            ToolTip toolTip = new ToolTip();
            toolTip.SetToolTip(trkWarningThreshold, "Drag to adjust warning threshold");
            toolTip.SetToolTip(numWarningThreshold, "Warning threshold in milliseconds");
            toolTip.SetToolTip(trkAlertThreshold, "Drag to adjust alert threshold");
            toolTip.SetToolTip(numAlertThreshold, "Alert threshold in milliseconds");
            toolTip.SetToolTip(pnlWarningColor, "Rows with this background color indicate a warning");
            toolTip.SetToolTip(pnlAlertColor, "Rows with this background color indicate an alert");
            
            // Add all panels to the group box
            grpThresholds.Controls.Add(pnlAlert);
            grpThresholds.Controls.Add(pnlWarning);
            grpThresholds.Controls.Add(btnResetThresholds);
            
            // Adjust the form to make space for the threshold controls
            // We'll place the threshold group box at the bottom of the form
            
            // Remember the original size of the DataGridView
            int originalGridHeight = dgvMetrics.Height;
            
            // Reduce the height of the DataGridView to make room for thresholds
            dgvMetrics.Height = originalGridHeight - grpThresholds.Height - 10;
            
            // Add the threshold group box below the DataGridView
            this.Controls.Add(grpThresholds);
            
            // Position the group box below the DataGridView
            grpThresholds.Location = new Point(dgvMetrics.Left, dgvMetrics.Bottom + 5);
            grpThresholds.Width = dgvMetrics.Width;
            
            // Ensure warning threshold is always less than alert threshold
            ValidateThresholds();
        }

        // Add a Form_Load event handler to make sure the layout is correct
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            
            // Ensure the controls are positioned correctly after the form loads
            if (grpThresholds != null)
            {
                grpThresholds.Location = new Point(dgvMetrics.Left, dgvMetrics.Bottom + 5);
                grpThresholds.Width = dgvMetrics.Width;
            }
        }

        // Add a Form_Resize event handler to adjust the layout when resized
        private void Form1_Resize(object sender, EventArgs e)
        {
            if (grpThresholds != null)
            {
                // Make sure the group box stays at the bottom of the DataGridView
                // and matches its width when the form is resized
                grpThresholds.Location = new Point(dgvMetrics.Left, dgvMetrics.Bottom + 5);
                grpThresholds.Width = dgvMetrics.Width;
            }
        }

        // Event handlers for keeping the NumericUpDown and TrackBar in sync
        private void TrkWarningThreshold_Scroll(object sender, EventArgs e)
        {
            numWarningThreshold.Value = trkWarningThreshold.Value;
            warningThreshold = trkWarningThreshold.Value;
            ValidateThresholds();
            
            // Refresh the grid to update the colors based on new thresholds
            dgvMetrics.Refresh();
        }

        private void NumWarningThreshold_ValueChanged(object sender, EventArgs e)
        {
            trkWarningThreshold.Value = (int)numWarningThreshold.Value;
            warningThreshold = (int)numWarningThreshold.Value;
            ValidateThresholds();
            
            // Refresh the grid to update the colors based on new thresholds
            dgvMetrics.Refresh();
        }

        private void TrkAlertThreshold_Scroll(object sender, EventArgs e)
        {
            numAlertThreshold.Value = trkAlertThreshold.Value;
            alertThreshold = trkAlertThreshold.Value;
            ValidateThresholds();
            
            // Refresh the grid to update the colors based on new thresholds
            dgvMetrics.Refresh();
        }

        private void NumAlertThreshold_ValueChanged(object sender, EventArgs e)
        {
            trkAlertThreshold.Value = (int)numAlertThreshold.Value;
            alertThreshold = (int)numAlertThreshold.Value;
            ValidateThresholds();
            
            // Refresh the grid to update the colors based on new thresholds
            dgvMetrics.Refresh();
        }

        // Ensure warning threshold is always less than alert threshold
        private void ValidateThresholds()
        {
            // If warning threshold becomes greater than alert threshold
            if (warningThreshold >= alertThreshold)
            {
                // Set warning to be slightly less than alert
                warningThreshold = alertThreshold - 1000;
                if (warningThreshold < 0) warningThreshold = 0;
                
                // Update controls
                numWarningThreshold.Value = warningThreshold;
                trkWarningThreshold.Value = warningThreshold;
            }
        }

        // Add a method to reset thresholds to default values
        private void BtnResetThresholds_Click(object sender, EventArgs e)
        {
            // Reset to default values
            warningThreshold = 10000;
            alertThreshold = 40000;
            
            // Update UI controls
            trkWarningThreshold.Value = warningThreshold;
            numWarningThreshold.Value = warningThreshold;
            trkAlertThreshold.Value = alertThreshold;
            numAlertThreshold.Value = alertThreshold;
            
            // Refresh the grid
            dgvMetrics.Refresh();
            
            LogMessage("Thresholds reset to defaults: Warning=10,000 ms, Alert=40,000 ms");
        }

        // Add these methods to save and load settings
        private void SaveThresholdSettings()
        {
            // Simple settings storage in app settings
            Properties.Settings.Default.WarningThreshold = warningThreshold;
            Properties.Settings.Default.AlertThreshold = alertThreshold;
            Properties.Settings.Default.Save();
        }

        private void LoadThresholdSettings()
        {
            // Load settings if they exist
            if (Properties.Settings.Default.WarningThreshold > 0)
            {
                warningThreshold = Properties.Settings.Default.WarningThreshold;
            }
            
            if (Properties.Settings.Default.AlertThreshold > 0)
            {
                alertThreshold = Properties.Settings.Default.AlertThreshold;
            }
            
            // Make sure alert is always higher than warning
            if (alertThreshold <= warningThreshold)
            {
                alertThreshold = warningThreshold + 10000;
            }
        }

        // Add this to Form1_FormClosing event
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveThresholdSettings();
        }
    }
}