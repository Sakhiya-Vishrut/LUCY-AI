using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LucyAI.UI.Controls
{
    public partial class ModuleViews : UserControl
    {
        public static readonly DependencyProperty ActivePageProperty =
            DependencyProperty.Register(nameof(ActivePage), typeof(string), typeof(ModuleViews),
                new PropertyMetadata("Home", OnActivePageChanged));

        public string ActivePage
        {
            get => (string)GetValue(ActivePageProperty);
            set => SetValue(ActivePageProperty, value);
        }

        public ModuleViews()
        {
            InitializeComponent();
            RenderModulePage(ActivePage);
        }

        private static void OnActivePageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ModuleViews control && e.NewValue is string newPage)
            {
                control.RenderModulePage(newPage);
            }
        }

        public void RenderModulePage(string pageName)
        {
            if (ModuleContainer == null) return;

            if (pageName == "Home")
            {
                ModuleContainer.Content = null; // Home shows 3D sphere core view
                return;
            }

            var card = new Border
            {
                Style = (Style)Resources["GlassCard"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 700,
                MaxHeight = 450
            };

            var stack = new StackPanel();

            var title = new TextBlock
            {
                Style = (Style)Resources["SciFiHeader"],
                Text = $"[+] MODULE: {pageName.ToUpperInvariant()}"
            };
            stack.Children.Add(title);

            string desc = pageName switch
            {
                "Voice" => "Voice Assistant Mode: Continuous offline wake-word listener ('Lucy') active. Speak commands naturally in English, Gujarati, Hindi, or mixed language.",
                "Memory" => "Long-Term AI Memory: Storing user preferences, project paths, frequently opened applications, and custom workflow context for Vishrut.",
                "Files" => "Files AI Indexer: Real-time search across Desktop, Documents, Downloads, and source code folders. Speak 'Lucy search invoice PDF' to locate files.",
                "Vision" => "Vision & OCR Engine: Active desktop analyzer. Supports full-screen OCR, window detection, element highlighting, and mouse control.",
                "Browser" => "Voice Browser AI: Web search automation, YouTube playback, page summaries, and form filling triggered entirely by natural speech.",
                "Automation" => "Automation Engine: Scheduled tasks, morning workflows, automated desktop backups, and custom event triggers.",
                "Control" => "Windows Control Center: App launcher, task manager, system shutdown/restart safety triggers, audio volume adjustment, and screen lock.",
                "Apps" => "Apps & Music Hub: Instant launcher for Chrome, Visual Studio, Spotify, WhatsApp, Calculator, Notepad, and media controls.",
                "Settings" => "Settings & API Configuration: Switch local Ollama AI models (phi4, llama3.1, qwen2.5, deepseek, mistral), configure microphone, TTS voice settings.",
                "Developer" => "Developer Assistant Mode: .NET 10, C#, Angular, SQL, Python code generation, error explanation, and solution workspace integration.",
                _ => "JARVIS AI Subsystem active and operating at peak performance."
            };

            var body = new TextBlock
            {
                Style = (Style)Resources["SciFiBody"],
                Text = desc
            };
            stack.Children.Add(body);

            // Sub-status indicator
            var statusBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 16, 0, 0)
            };
            var statusText = new TextBlock
            {
                Text = "⚡ STATUS: ONLINE | VOICE LISTENER: READY",
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            statusBorder.Child = statusText;
            stack.Children.Add(statusBorder);

            card.Child = stack;
            ModuleContainer.Content = card;
        }
    }
}
