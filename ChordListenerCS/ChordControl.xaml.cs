using System;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ChordListenerCS
{
    public partial class ChordControl : UserControl
    {
        public ChordControl()
        {
            InitializeComponent();
        }

        // e.g., "x32010" for C Major
        // 6 chars corresponding to strings E A D G B e
        // x = muted, 0 = open, 1-9 = fret
        public void SetChord(string name, string pattern)
        {
            TxtChordName.Text = name;
            DrawFretboard(pattern);
        }

        private void DrawFretboard(string pattern)
        {
            FretCanvas.Children.Clear();

            double w = FretCanvas.Width;
            double h = FretCanvas.Height;
            double stringSpacing = w / 5;
            double fretSpacing = h / 5;

            // Draw Frets (Horizontal) - Top one (0) is thicker (Nut)
            for (int i = 0; i <= 5; i++)
            {
                var line = new Line
                {
                    X1 = 0, Y1 = i * fretSpacing,
                    X2 = w, Y2 = i * fretSpacing,
                    Stroke = (i == 0) ? Brushes.White : Brushes.Gray,
                    StrokeThickness = (i == 0) ? 3 : 1
                };
                FretCanvas.Children.Add(line);
            }

            // Draw Strings (Vertical)
            for (int i = 0; i < 6; i++)
            {
                var line = new Line
                {
                    X1 = i * stringSpacing, Y1 = 0,
                    X2 = i * stringSpacing, Y2 = h,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 1 + (0.2 * (5 - i)) // Thicker for low strings
                };
                FretCanvas.Children.Add(line);
            }

            // Validate pattern
            if (string.IsNullOrEmpty(pattern) || pattern.Length < 6) return;

            // Draw Notes
            for (int i = 0; i < 6; i++)
            {
                char p = pattern[i];
                double x = i * stringSpacing;

                if (p == 'x' || p == 'X')
                {
                    // Draw X at top
                    DrawX(x, -10);
                }
                else if (p == '0')
                {
                    // Draw O at top
                    DrawOpenCircle(x, -10);
                }
                else if (char.IsDigit(p))
                {
                    int fret = int.Parse(p.ToString());
                    if (fret > 0)
                    {
                        // Draw filled circle on fret
                        // Center of fret space
                        double y = (fret * fretSpacing) - (fretSpacing / 2);
                        DrawDot(x, y);
                    }
                }
            }
        }

        private void DrawDot(double x, double y)
        {
            var ellipse = new Ellipse
            {
                Width = 14, Height = 14,
                Fill = Brushes.SpringGreen,
                Stroke = Brushes.White,
                StrokeThickness = 1
            };
            Canvas.SetLeft(ellipse, x - 7);
            Canvas.SetTop(ellipse, y - 7);
            FretCanvas.Children.Add(ellipse);
        }

        private void DrawOpenCircle(double x, double y)
        {
            var ellipse = new Ellipse
            {
                Width = 8, Height = 8,
                Stroke = Brushes.White,
                StrokeThickness = 1
            };
            Canvas.SetLeft(ellipse, x - 4);
            Canvas.SetTop(ellipse, y - 4);
            FretCanvas.Children.Add(ellipse);
        }

        private void DrawX(double x, double y)
        {
            var crossGroup = new Canvas { Width = 8, Height = 8 };
            Canvas.SetLeft(crossGroup, x - 4);
            Canvas.SetTop(crossGroup, y - 4);

            crossGroup.Children.Add(new Line { X1=0, Y1=0, X2=8, Y2=8, Stroke=Brushes.Red, StrokeThickness=1.5 });
            crossGroup.Children.Add(new Line { X1=0, Y1=8, X2=8, Y2=0, Stroke=Brushes.Red, StrokeThickness=1.5 });
            
            FretCanvas.Children.Add(crossGroup);
        }
    }
}
