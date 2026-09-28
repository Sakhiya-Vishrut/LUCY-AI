using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace LucyAI.UI.Controls
{
    public partial class Hologram3DControl : UserControl
    {
        private readonly AxisAngleRotation3D _sphereRotationX = new(new Vector3D(1, 0, 0), 0);
        private readonly AxisAngleRotation3D _sphereRotationY = new(new Vector3D(0, 1, 0), 0);
        private readonly AxisAngleRotation3D _ring1Rotation = new(new Vector3D(1, 1, 0), 0);
        private readonly AxisAngleRotation3D _ring2Rotation = new(new Vector3D(0, 1, 1), 0);
        private readonly AxisAngleRotation3D _ring3Rotation = new(new Vector3D(1, 0, 1), 0);
        private readonly ScaleTransform3D _pulseScale = new(1, 1, 1);

        private double _timeCounter;

        public static readonly DependencyProperty AssistantStateProperty =
            DependencyProperty.Register(nameof(AssistantState), typeof(string), typeof(Hologram3DControl),
                new PropertyMetadata("ONLINE & LISTENING", OnAssistantStateChanged));

        public string AssistantState
        {
            get => (string)GetValue(AssistantStateProperty);
            set => SetValue(AssistantStateProperty, value);
        }

        public Hologram3DControl()
        {
            InitializeComponent();
            Build3DHologramScene();
            CompositionTarget.Rendering += OnCompositionRendering;
        }

        private static void OnAssistantStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // Dynamic visual changes when state updates
        }

        private void Build3DHologramScene()
        {
            // Core Sphere Model
            var sphereMesh = CreateSphereMesh(1.2, 32, 32);
            var goldGlowMaterial = new MaterialGroup();
            goldGlowMaterial.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(180, 255, 179, 0))));
            goldGlowMaterial.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(220, 255, 160, 0))));

            var sphereModel = new GeometryModel3D(sphereMesh, goldGlowMaterial)
            {
                BackMaterial = goldGlowMaterial
            };

            var sphereTransformGroup = new Transform3DGroup();
            sphereTransformGroup.Children.Add(_pulseScale);
            sphereTransformGroup.Children.Add(new RotateTransform3D(_sphereRotationX));
            sphereTransformGroup.Children.Add(new RotateTransform3D(_sphereRotationY));
            sphereModel.Transform = sphereTransformGroup;
            SceneModelGroup.Children.Add(sphereModel);

            // Ring 1 (Gold Inner Ring)
            var ring1Mesh = CreateTorusMesh(1.8, 0.04, 48, 16);
            var cyanMaterial = new MaterialGroup();
            cyanMaterial.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(240, 0, 229, 255))));
            
            var ring1Model = new GeometryModel3D(ring1Mesh, cyanMaterial);
            var ring1Transform = new Transform3DGroup();
            ring1Transform.Children.Add(new RotateTransform3D(_ring1Rotation));
            ring1Model.Transform = ring1Transform;
            SceneModelGroup.Children.Add(ring1Model);

            // Ring 2 (Cyan Middle Ring)
            var ring2Mesh = CreateTorusMesh(2.2, 0.03, 56, 16);
            var goldMaterial = new MaterialGroup();
            goldMaterial.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(250, 255, 190, 30))));

            var ring2Model = new GeometryModel3D(ring2Mesh, goldMaterial);
            var ring2Transform = new Transform3DGroup();
            ring2Transform.Children.Add(new RotateTransform3D(_ring2Rotation));
            ring2Model.Transform = ring2Transform;
            SceneModelGroup.Children.Add(ring2Model);

            // Ring 3 (Outer Orbital Ring)
            var ring3Mesh = CreateTorusMesh(2.6, 0.02, 64, 16);
            var ring3Model = new GeometryModel3D(ring3Mesh, cyanMaterial);
            var ring3Transform = new Transform3DGroup();
            ring3Transform.Children.Add(new RotateTransform3D(_ring3Rotation));
            ring3Model.Transform = ring3Transform;
            SceneModelGroup.Children.Add(ring3Model);
        }

        private void OnCompositionRendering(object? sender, EventArgs e)
        {
            _timeCounter += 0.02;

            double speedMultiplier = 1.0;
            if (AssistantState.Contains("THINKING")) speedMultiplier = 3.5;
            else if (AssistantState.Contains("SPEAKING")) speedMultiplier = 2.5;
            else if (AssistantState.Contains("LISTENING")) speedMultiplier = 1.8;

            _sphereRotationX.Angle = (_sphereRotationX.Angle + 0.5 * speedMultiplier) % 360;
            _sphereRotationY.Angle = (_sphereRotationY.Angle + 0.8 * speedMultiplier) % 360;
            _ring1Rotation.Angle = (_ring1Rotation.Angle + 1.2 * speedMultiplier) % 360;
            _ring2Rotation.Angle = (_ring2Rotation.Angle - 1.6 * speedMultiplier) % 360;
            _ring3Rotation.Angle = (_ring3Rotation.Angle + 0.9 * speedMultiplier) % 360;

            // Audio/Voice pulse oscillation
            double pulse = Math.Sin(_timeCounter * 4) * 0.08 + 1.0;
            if (AssistantState.Contains("SPEAKING"))
            {
                pulse = Math.Sin(_timeCounter * 12) * 0.15 + 1.05;
            }

            _pulseScale.ScaleX = pulse;
            _pulseScale.ScaleY = pulse;
            _pulseScale.ScaleZ = pulse;
        }

        private static MeshGeometry3D CreateSphereMesh(double radius, int slices, int stacks)
        {
            var mesh = new MeshGeometry3D();
            for (int stack = 0; stack <= stacks; stack++)
            {
                double phi = Math.PI / 2 - stack * Math.PI / stacks;
                double y = radius * Math.Sin(phi);
                double scale = radius * Math.Cos(phi);

                for (int slice = 0; slice <= slices; slice++)
                {
                    double theta = slice * 2 * Math.PI / slices;
                    double x = scale * Math.Cos(theta);
                    double z = scale * Math.Sin(theta);

                    mesh.Positions.Add(new Point3D(x, y, z));
                    mesh.Normals.Add(new Vector3D(x, y, z));
                }
            }

            for (int stack = 0; stack < stacks; stack++)
            {
                for (int slice = 0; slice < slices; slice++)
                {
                    int first = stack * (slices + 1) + slice;
                    int second = first + slices + 1;

                    mesh.TriangleIndices.Add(first);
                    mesh.TriangleIndices.Add(second);
                    mesh.TriangleIndices.Add(first + 1);

                    mesh.TriangleIndices.Add(second);
                    mesh.TriangleIndices.Add(second + 1);
                    mesh.TriangleIndices.Add(first + 1);
                }
            }
            return mesh;
        }

        private static MeshGeometry3D CreateTorusMesh(double majorRadius, double minorRadius, int majorSlices, int minorSlices)
        {
            var mesh = new MeshGeometry3D();
            for (int i = 0; i <= majorSlices; i++)
            {
                double u = i * 2 * Math.PI / majorSlices;
                var center = new Point3D(majorRadius * Math.Cos(u), majorRadius * Math.Sin(u), 0);

                for (int j = 0; j <= minorSlices; j++)
                {
                    double v = j * 2 * Math.PI / minorSlices;
                    double x = (majorRadius + minorRadius * Math.Cos(v)) * Math.Cos(u);
                    double y = (majorRadius + minorRadius * Math.Cos(v)) * Math.Sin(u);
                    double z = minorRadius * Math.Sin(v);

                    mesh.Positions.Add(new Point3D(x, y, z));
                    mesh.Normals.Add(new Vector3D(x - center.X, y - center.Y, z));
                }
            }

            for (int i = 0; i < majorSlices; i++)
            {
                for (int j = 0; j < minorSlices; j++)
                {
                    int first = i * (minorSlices + 1) + j;
                    int second = first + minorSlices + 1;

                    mesh.TriangleIndices.Add(first);
                    mesh.TriangleIndices.Add(second);
                    mesh.TriangleIndices.Add(first + 1);

                    mesh.TriangleIndices.Add(second);
                    mesh.TriangleIndices.Add(second + 1);
                    mesh.TriangleIndices.Add(first + 1);
                }
            }
            return mesh;
        }
    }
}
