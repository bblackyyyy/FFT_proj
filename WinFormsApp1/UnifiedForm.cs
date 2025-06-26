
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using FftSharp;

namespace SignalViewerWinForms
{
    public class UnifiedForm : Form
    {
        private readonly TextBox fileBox;
        private readonly Button browseButton;
        private readonly NumericUpDown sampleRateBox;
        private readonly Button loadButton;
        private readonly Chart chartOsc;
        private readonly Chart chartSpec;
        private readonly NumericUpDown numStart;
        private readonly NumericUpDown numLength;
        private readonly ComboBox cmbWindow;
        private readonly GroupBox grpChannels;
        private readonly RadioButton radio1;
        private readonly RadioButton radio2;
        private readonly RadioButton radio3;

        private double[][] allSignals = Array.Empty<double[]>();
        private double Fs = 1000.0;
        private bool suppressEvents = false;

        public UnifiedForm()
        {
            Text = "Signal Viewer";
            Width = 1200;
            Height = 600;

            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.Sizable;

            var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75f));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            Controls.Add(main);

            var charts = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
            charts.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            charts.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            main.Controls.Add(charts, 0, 0);

            chartOsc = CreateChart("Oscillogram", "Time [s]", "Amplitude");
            chartSpec = CreateChart("Spectrum", "Frequency [Hz]", "Magnitude");
            charts.Controls.Add(chartOsc, 0, 0);
            charts.Controls.Add(chartSpec, 0, 1);

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                AutoScroll = true,
                BackColor = Color.LightGray
            };
            main.Controls.Add(panel, 1, 0);

            
            panel.Controls.Add(new Label { Text = ".CSV file:" });
            fileBox = new TextBox { ReadOnly = true, Width = 180 };
            browseButton = new Button { Text = "Browse", Width = 80 };
            browseButton.Click += BrowseButton_Click;
            panel.Controls.Add(fileBox);
            panel.Controls.Add(browseButton);

            
            panel.Controls.Add(new Label { Text = "Sampling rate [Hz]:" });
            sampleRateBox = new NumericUpDown { Minimum = 1, Maximum = 1_000_000, Value = (decimal)Fs, Width = 100 };
            panel.Controls.Add(sampleRateBox);

            
            loadButton = new Button { Text = "Load", Width = 150, ForeColor = Color.White };
            loadButton.Click += LoadAndAnalyze;
            loadButton.BackColor = Color.DarkBlue;
            loadButton.FlatStyle = FlatStyle.Flat;
            loadButton.FlatAppearance.BorderSize = 0;
            panel.Controls.Add(loadButton);

         
            panel.Controls.Add(new Label { Text = "Start:" });
            numStart = new NumericUpDown { Minimum = 0, Maximum = 100_000, Width = 100 };
            panel.Controls.Add(numStart);
            panel.Controls.Add(new Label { Text = "Length:" });
            numLength = new NumericUpDown { Minimum = 1, Maximum = 100_000, Width = 100 };
            panel.Controls.Add(numLength);

           
            panel.Controls.Add(new Label { Text = "Window type:" });
            cmbWindow = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            cmbWindow.Items.AddRange(new[] { "Rectangular", "Hann", "Hamming" });
            cmbWindow.SelectedIndex = 0;
            panel.Controls.Add(cmbWindow);

          
            grpChannels = new GroupBox { Text = "Channels", Width = 150, Height = 120 };
            grpChannels.Location = new Point(0, 0);
            radio1 = new RadioButton { Text = "1", Location = new Point(10, 20), AutoSize = true, Checked = true };
            radio2 = new RadioButton { Text = "2", Location = new Point(10, 45), AutoSize = true, Visible = false };
            radio3 = new RadioButton { Text = "3", Location = new Point(10, 70), AutoSize = true, Visible = false };
            radio1.CheckedChanged += (s, e) => { if (!suppressEvents) RefreshPlots(); };
            radio2.CheckedChanged += (s, e) => { if (!suppressEvents) RefreshPlots(); };
            radio3.CheckedChanged += (s, e) => { if (!suppressEvents) RefreshPlots(); };
            grpChannels.Controls.Add(radio1);
            grpChannels.Controls.Add(radio2);
            grpChannels.Controls.Add(radio3);
            panel.Controls.Add(grpChannels);

           
            numStart.ValueChanged += NumStart_ValueChanged;
            numLength.ValueChanged += (s, e) => { if (!suppressEvents) RefreshPlots(); };
            cmbWindow.SelectedIndexChanged += (s, e) => RefreshPlots();
        }

        private void BrowseButton_Click(object? sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv" };
            if (dlg.ShowDialog() == DialogResult.OK)
                fileBox.Text = dlg.FileName;
        }

        private void LoadAndAnalyze(object? sender, EventArgs e)
        {
            if (!File.Exists(fileBox.Text)) return;

            Fs = (double)sampleRateBox.Value;
            var lines = File.ReadAllLines(fileBox.Text)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();

            var parsedRows = lines
                .Select(line => line
                    .Split(new[] { ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var v) ? (double?)v : null)
                    .ToArray())
                .Where(r => r.All(v => v.HasValue))
                .Select(r => r.Select(v => v!.Value).ToArray())
                .ToArray();

            if (parsedRows.Length == 0)
            {
                MessageBox.Show("Brak poprawnych danych w pliku lub nieprawidłowy format.");
                return;
            }

            bool skipFirstColumn = parsedRows[0].Length > 1 && parsedRows.Take(10).Select(r => r[0]).Distinct().Count() > 1;
            int offset = 0;
            int numChannels = parsedRows[0].Length;

            if (numChannels < 1 || numChannels > 3)
            {
                MessageBox.Show("Only 1 to 3 channel files are supported.");
                return;
            }

         
            allSignals = Enumerable.Range(0, numChannels)
                .Select(ch => parsedRows.Select(row => row[ch + offset]).ToArray())
                .ToArray();

           
            suppressEvents = true;
            radio1.Visible = numChannels >= 1;
            radio2.Visible = numChannels >= 2;
            radio3.Visible = numChannels >= 3;
            radio1.Checked = true;

            
            numStart.Minimum = 0;
            numStart.Maximum = allSignals[0].Length - 1;
            numStart.Value = 0;
            numLength.Minimum = 1;
            numLength.Maximum = allSignals[0].Length;
            numLength.Value = allSignals[0].Length;
            suppressEvents = false;

            RefreshPlots();
        }

        private void NumStart_ValueChanged(object? sender, EventArgs e)
        {
            if (allSignals.Length == 0 || suppressEvents) return;

            int start = (int)numStart.Value;
            int maxLen = allSignals[0].Length - start;

            suppressEvents = true;
            numLength.Maximum = maxLen;
            if (numLength.Value > maxLen)
                numLength.Value = maxLen;
            suppressEvents = false;

            RefreshPlots();
        }

        private void RefreshPlots()
        {
            if (allSignals.Length == 0) return;

            int start = (int)numStart.Value;
            int length = (int)numLength.Value;
            if (length <= 0 || start < 0 || start + length > allSignals[0].Length) return;

            
            int channelsToShow = radio3.Checked ? 3 : radio2.Checked ? 2 : 1;
            chartOsc.Series.Clear();
            chartSpec.Series.Clear();

            Color[] colors = { Color.Blue, Color.Red, Color.Green };
            for (int ch = 0; ch < channelsToShow; ch++)
            {
                var segment = new double[length];
                Array.Copy(allSignals[ch], start, segment, 0, length);

                
                var oscSeries = new Series($"Osc_{ch + 1}")
                {
                    ChartType = SeriesChartType.Line,
                    BorderWidth = 2,
                    Color = colors[ch]
                };
                for (int i = 0; i < segment.Length; i++)
                    oscSeries.Points.AddXY((start + i) / Fs, segment[i]);
                chartOsc.Series.Add(oscSeries);

                
                var windowed = ApplyWindow(segment, cmbWindow.SelectedItem!.ToString() ?? "Rectangular");
                int pow2 = 1; while (pow2 < windowed.Length) pow2 <<= 1;
                var padded = new double[pow2]; windowed.CopyTo(padded, 0);
                var fft = FFT.Forward(padded);
                var mag = FFT.Magnitude(fft);

                var specSeries = new Series($"Spec_{ch + 1}")
                {
                    ChartType = SeriesChartType.Line,
                    BorderWidth = 2,
                    Color = colors[ch]
                };
                for (int k = 0; k < mag.Length / 2; k++)
                    specSeries.Points.AddXY((k * Fs / mag.Length), mag[k]);
                chartSpec.Series.Add(specSeries);
            }
        }

        private static double[] ApplyWindow(double[] data, string name)
        {
            int n = data.Length;
            var dst = new double[n];
            for (int i = 0; i < n; i++)
            {
                double w = name switch
                {
                    "Hann" => 0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1))),
                    "Hamming" => 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (n - 1)),
                    _ => 1.0
                };
                dst[i] = data[i] * w;
            }
            return dst;
        }

        private static Chart CreateChart(string title, string x, string y)
        {
            var chart = new Chart { Dock = DockStyle.Fill };
            var area = new ChartArea();
            area.BackColor = Color.White;
            area.AxisX.Title = x;
            area.AxisY.Title = y;
            chart.ChartAreas.Add(area);
            chart.Titles.Add(title);
            return chart;
        }
    }
}
