using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

using System.Drawing;
using System.Windows.Forms.VisualStyles;

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

        
        private double[] signal = Array.Empty<double>();
        private double   Fs     = 1_000.0; 

        private bool suppressEvents = false; 
        

        public UnifiedForm()
        {
            
            Text  = "Signal Viewer";
            Width = 1_200;
            Height = 600;
            
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.Sizable;
            
            var main = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2
            };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75f));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            Controls.Add(main);

            
            var charts = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
            };
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
            sampleRateBox = new NumericUpDown {
                Minimum = 1,
                Maximum = 1_000_000,
                Value   = (decimal)Fs,
                Width   = 100
            };
            panel.Controls.Add(sampleRateBox);

            
            loadButton = new Button { Text = "Load", Width = 150 , ForeColor = Color.White};
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

          
            numStart.ValueChanged   += NumStart_ValueChanged;
            numLength.ValueChanged  += (s,e) => { if (!suppressEvents) RefreshPlots(); };
            cmbWindow.SelectedIndexChanged += (s,e) => RefreshPlots();
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
            signal = File.ReadAllLines(fileBox.Text)
                         .Select(l => double.TryParse(l, out var v) ? v : (double?)null)
                         .Where(v => v.HasValue)
                         .Select(v => v.Value)
                         .ToArray();

            suppressEvents = true;
            numStart.Minimum  = 0;
            numStart.Maximum  = signal.Length - 1;
            numStart.Value    = 0;

            numLength.Minimum = 1;
            numLength.Maximum = signal.Length;
            numLength.Value   = signal.Length;   
            suppressEvents = false;

            RefreshPlots();
        }

        
        private void NumStart_ValueChanged(object? sender, EventArgs e)
        {
            if (signal.Length == 0 || suppressEvents) return;

            int start = (int)numStart.Value;
            int maxLen = signal.Length - start;

            suppressEvents = true; 
            numLength.Maximum = maxLen;
            if (numLength.Value > maxLen)
                numLength.Value = maxLen;
            suppressEvents = false;

            RefreshPlots();
        }


        private void RefreshPlots()
        {
            if (signal.Length == 0) return;

            int start = (int)numStart.Value;
            int length = (int)numLength.Value;

            if (length <= 0 || start < 0 || start + length > signal.Length)
            {
                return;
            }


            var fragment = new double[length];
            Array.Copy(signal, start, fragment, 0, length);


            var oscSeries = chartOsc.Series[0];
            oscSeries.Points.Clear();
            for (int i = 0; i < fragment.Length; i++)
            {
                oscSeries.Points.AddXY((start + i) / Fs, fragment[i]);
            }


            var windowed = ApplyWindow(fragment, cmbWindow.SelectedItem!.ToString() ?? "Rectangular");
            var fft = ComputeFFT(windowed);

            var specSeries = chartSpec.Series[0];
            specSeries.Points.Clear();
            for (int k = 0; k < fft.Length / 2; k++)
            {
                specSeries.Points.AddXY(k * Fs / fft.Length, fft[k].Magnitude);
            }
        }


        private static Chart CreateChart(string title, string x, string y)
        {
            var chart = new Chart { Dock = DockStyle.Fill };
            var area  = new ChartArea();
            area.BackColor = Color.White;
            area.AxisX.Title = x;
            area.AxisY.Title = y;
            chart.ChartAreas.Add(area);
            chart.Series.Add(new Series { ChartType = SeriesChartType.Line, BorderWidth = 2 });
            chart.Titles.Add(title);
            return chart;
        }

        private static double[] ApplyWindow(double[] data, string name)
        {
            int n = data.Length;
            var dst = new double[n];
            for (int i = 0; i < n; i++)
            {
                double w = name switch
                {
                    "Hann"    => 0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1))),
                    "Hamming" => 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (n - 1)),
                    _          => 1.0
                };
                dst[i] = data[i] * w;
            }
            return dst;
        }

        
        private static Complex[] ComputeFFT(double[] data)
        {
            var buf = data.Select(v => new Complex(v, 0)).ToArray();
            FFTRecursive(buf);
            return buf;
        }

        private static void FFTRecursive(Complex[] buf)
        {
            int n = buf.Length;
            if (n <= 1) return;

            var even = new Complex[n / 2];
            var odd  = new Complex[n / 2];
            for (int i = 0; i < n / 2; i++)
            {
                even[i] = buf[i * 2];
                odd[i]  = buf[i * 2 + 1];
            }
            FFTRecursive(even);
            FFTRecursive(odd);
            for (int k = 0; k < n / 2; k++)
            {
                var t = Complex.Exp(-2 * Math.PI * Complex.ImaginaryOne * k / n) * odd[k];
                buf[k]         = even[k] + t;
                buf[k + n / 2] = even[k] - t;
            }
        }
    }
}