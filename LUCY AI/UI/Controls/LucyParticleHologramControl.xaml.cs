using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LucyAI.UI.Controls
{
    public partial class LucyParticleHologramControl : UserControl
    {
        private struct Particle3D
        {
            public double X, Y, Z;
            public double BaseRadius;
            public double Phase;
        }

        private readonly List<Particle3D> _particles = new();
        private readonly Random _rand = new();

        private double _rotX;
        private double _rotY;
        private double _rotZ;
        private double _timeCounter;
        private double _radarAngle;

        public static readonly DependencyProperty AssistantStateProperty =
            DependencyProperty.Register(nameof(AssistantState), typeof(string), typeof(LucyParticleHologramControl),
                new PropertyMetadata("Listening"));

        public string AssistantState
        {
            get => (string)GetValue(AssistantStateProperty);
            set => SetValue(AssistantStateProperty, value);
        }

        public static readonly DependencyProperty AudioVolumeProperty =
            DependencyProperty.Register(nameof(AudioVolume), typeof(double), typeof(LucyParticleHologramControl),
                new PropertyMetadata(0.0));

        public double AudioVolume
        {
            get => (double)GetValue(AudioVolumeProperty);
            set => SetValue(AudioVolumeProperty, value);
        }

        public LucyParticleHologramControl()
        {
            InitializeComponent();
            GenerateParticleSphere(550, 130);
            CompositionTarget.Rendering += OnRenderLoop;
        }

        private void GenerateParticleSphere(int count, double radius)
        {
            _particles.Clear();
            double goldenRatio = (1 + Math.Sqrt(5)) / 2;

            for (int i = 0; i < count; i++)
            {
                double theta = 2 * Math.PI * i / goldenRatio;
                double phi = Math.Acos(1 - 2.0 * (i + 0.5) / count);

                double x = radius * Math.Sin(phi) * Math.Cos(theta);
                double y = radius * Math.Sin(phi) * Math.Sin(theta);
                double z = radius * Math.Cos(phi);

                _particles.Add(new Particle3D
                {
                    X = x,
                    Y = y,
                    Z = z,
                    BaseRadius = radius,
                    Phase = _rand.NextDouble() * Math.PI * 2
                });
            }
        }

        private void OnRenderLoop(object? sender, EventArgs e)
        {
            _timeCounter += 0.025;
            _radarAngle = (_radarAngle + 2.5) % 360;

            double speed = 0.018;
            if (AssistantState.Contains("Thinking")) speed = 0.065;
            else if (AssistantState.Contains("Speaking")) speed = 0.040;
            else if (AssistantState.Contains("Listening")) speed = 0.025 + (AudioVolume / 1500.0);

            _rotX += speed * 0.8;
            _rotY += speed * 1.2;
            _rotZ += speed * 0.5;

            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double width = ActualWidth > 0 ? ActualWidth : 800;
            double height = ActualHeight > 0 ? ActualHeight : 600;
            double cx = width / 2;
            double cy = height / 2;
            double fov = 420;

            // Emerald & Neon Green Brushes
            var neonPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 255, 102)), 1.2);
            var darkEmeraldPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 180, 80)), 1.0);
            var neonBrush = new SolidColorBrush(Color.FromArgb(240, 0, 255, 102));
            var brightBrush = new SolidColorBrush(Color.FromArgb(255, 180, 255, 210));

            // Background Deep Black & Dark Emerald Gradient
            var bgBrush = new RadialGradientBrush(
                Color.FromArgb(255, 2, 16, 8),
                Color.FromArgb(255, 0, 0, 0)
            )
            { Center = new Point(0.5, 0.5), RadiusX = 0.8, RadiusY = 0.8 };
            dc.DrawRectangle(bgBrush, null, new Rect(0, 0, width, height));

            // Mic Volume Particle Explosion Bonus
            double volumeBonus = (AudioVolume / 100.0) * 35.0;
            double pulse = Math.Sin(_timeCounter * 3) * 8 + volumeBonus;
            if (AssistantState.Contains("Speaking")) pulse = Math.Sin(_timeCounter * 12) * 22;
            else if (AssistantState.Contains("Thinking")) pulse = -Math.Sin(_timeCounter * 10) * 12;

            // 1. Draw Radar Sweep Line in Listening Mode
            if (AssistantState.Contains("Listening"))
            {
                DrawRadarSweep(dc, cx, cy, 220 + pulse * 0.5, _radarAngle, neonPen);
            }

            // 2. Draw Orbiting Sci-Fi Tech Rings
            DrawTechRing(dc, cx, cy, 175 + pulse * 0.4, _rotZ * 45, neonPen);
            DrawTechRing(dc, cx, cy, 195 + pulse * 0.6, -_rotZ * 30, darkEmeraldPen);
            DrawRadialSpectrumBars(dc, cx, cy, 150, 60);

            // Project and sort 3D particles by Z-depth
            var projected = new List<(double x, double y, double z, double size, double opacity)>();

            double cosX = Math.Cos(_rotX), sinX = Math.Sin(_rotX);
            double cosY = Math.Cos(_rotY), sinY = Math.Sin(_rotY);
            double cosZ = Math.Cos(_rotZ), sinZ = Math.Sin(_rotZ);

            foreach (var p in _particles)
            {
                double rScale = 1.0 + Math.Sin(_timeCounter * 2 + p.Phase) * 0.06 + (AudioVolume / 350.0);
                double px = p.X * rScale;
                double py = p.Y * rScale;
                double pz = p.Z * rScale;

                // Rotation Y
                double x1 = px * cosY + pz * sinY;
                double y1 = py;
                double z1 = -px * sinY + pz * cosY;

                // Rotation X
                double x2 = x1;
                double y2 = y1 * cosX - z1 * sinX;
                double z2 = y1 * sinX + z1 * cosX;

                // Rotation Z
                double x3 = x2 * cosZ - y2 * sinZ;
                double y3 = x2 * sinZ + y2 * cosZ;
                double z3 = z2;

                // Perspective Projection
                double distance = 320;
                double scale = fov / (distance + z3);
                double sx = cx + x3 * scale;
                double sy = cy + y3 * scale;

                double dotSize = Math.Max(1.0, (z3 + 140) / 60.0 * scale);
                double opacity = Math.Clamp((z3 + 140) / 260.0, 0.20, 1.0);

                projected.Add((sx, sy, z3, dotSize, opacity));
            }

            projected.Sort((a, b) => a.z.CompareTo(b.z));

            // Connecting Neural Network Lines
            int pCount = projected.Count;
            var linePen = new Pen(new SolidColorBrush(Color.FromArgb(45, 0, 255, 102)), 0.6);

            for (int i = 0; i < pCount; i += 7)
            {
                var p1 = projected[i];
                for (int j = i + 1; j < Math.Min(i + 4, pCount); j++)
                {
                    var p2 = projected[j];
                    double dx = p1.x - p2.x;
                    double dy = p1.y - p2.y;
                    if (dx * dx + dy * dy < 1800)
                    {
                        dc.DrawLine(linePen, new Point(p1.x, p1.y), new Point(p2.x, p2.y));
                    }
                }
            }

            // Draw glowing 3D particle dots
            foreach (var pt in projected)
            {
                Brush pBrush = pt.z > 0 ? brightBrush : neonBrush;
                pBrush.Opacity = pt.opacity;
                dc.DrawEllipse(pBrush, null, new Point(pt.x, pt.y), pt.size, pt.size);
            }

            // 3. Draw State Label in Center Core
            DrawCenterStateLabel(dc, cx, cy);
        }

        private static void DrawRadarSweep(DrawingContext dc, double cx, double cy, double radius, double angleDeg, Pen pen)
        {
            dc.DrawEllipse(null, pen, new Point(cx, cy), radius, radius);
            double rad = angleDeg * Math.PI / 180;
            double x2 = cx + radius * Math.Cos(rad);
            double y2 = cy + radius * Math.Sin(rad);

            var sweepPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 0, 255, 102)), 1.5);
            dc.DrawLine(sweepPen, new Point(cx, cy), new Point(x2, y2));
        }

        private static void DrawTechRing(DrawingContext dc, double cx, double cy, double radius, double angleDeg, Pen pen)
        {
            dc.PushTransform(new RotateTransform(angleDeg, cx, cy));
            dc.DrawEllipse(null, pen, new Point(cx, cy), radius, radius);

            for (int a = 0; a < 360; a += 45)
            {
                double rad = a * Math.PI / 180;
                double x1 = cx + (radius - 6) * Math.Cos(rad);
                double y1 = cy + (radius - 6) * Math.Sin(rad);
                double x2 = cx + (radius + 6) * Math.Cos(rad);
                double y2 = cy + (radius + 6) * Math.Sin(rad);

                dc.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
            }

            dc.Pop();
        }

        private void DrawRadialSpectrumBars(DrawingContext dc, double cx, double cy, double radius, int barCount)
        {
            var spectrumBrush = new SolidColorBrush(Color.FromArgb(220, 0, 255, 102));

            for (int i = 0; i < barCount; i++)
            {
                double angle = (i * 360.0 / barCount) * Math.PI / 180;
                double h = 6 + Math.Sin(_timeCounter * 6 + i * 0.4) * 14 + (AudioVolume * 0.35);
                if (AssistantState.Contains("Speaking"))
                {
                    h = 10 + Math.Abs(Math.Sin(_timeCounter * 16 + i * 0.8)) * 30;
                }

                double x1 = cx + radius * Math.Cos(angle);
                double y1 = cy + radius * Math.Sin(angle);
                double x2 = cx + (radius + h) * Math.Cos(angle);
                double y2 = cy + (radius + h) * Math.Sin(angle);

                var pen = new Pen(spectrumBrush, 1.8);
                dc.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
            }
        }

        private void DrawCenterStateLabel(DrawingContext dc, double cx, double cy)
        {
            string label = AssistantState.ToUpperInvariant();
            if (label.Contains("LISTENING")) label = "LISTENING...";
            else if (label.Contains("THINKING")) label = "THINKING...";
            else if (label.Contains("SPEAKING")) label = "SPEAKING...";

            var formattedText = new FormattedText(
                label,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                12,
                new SolidColorBrush(Color.FromArgb(220, 0, 255, 102)),
                1.0
            );

            dc.DrawText(formattedText, new Point(cx - formattedText.Width / 2, cy - formattedText.Height / 2));
        }
    }
}
