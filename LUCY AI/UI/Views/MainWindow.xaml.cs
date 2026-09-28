using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using LucyAI.UI.ViewModels;

namespace LucyAI.UI.Views
{
    /// <summary>
    /// Code-behind for the main LUCY window.
    ///
    /// RESPONSIBILITIES:
    ///   1. Wire DataContext to MainViewModel (injected via DI).
    ///   2. Keep nav buttons visually in sync with ViewModel.ActivePage.
    ///      We do this here (not in XAML DataTriggers) because we need to
    ///      reference named button elements — simpler and more readable.
    ///   3. Handle the Exit button click.
    ///
    /// EVERYTHING else (voice, AI, automation, metrics) lives in services
    /// or the ViewModel — the code-behind stays thin.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        // All nav buttons grouped for easy active-state management
        private List<Button> _navButtons = new();

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();

            _viewModel = viewModel;
            DataContext = viewModel;

            // Watch for page changes so we can update nav highlight
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.ActivePage))
                    UpdateNavHighlight(viewModel.ActivePage);
            };
        }

        protected override void OnContentRendered(System.EventArgs e)
        {
            base.OnContentRendered(e);

            // Build the nav button list after XAML elements are instantiated
            // (named elements are only available after InitializeComponent)
            _navButtons = new List<Button>
            {
                NavHome, NavAssistant, NavMemory, NavFiles,
                NavBrowser, NavVision, NavAutomation, NavApps,
                NavControl, NavSettings, NavDeveloper
            };

            // Highlight the initial page (Home)
            UpdateNavHighlight(_viewModel.ActivePage);
        }

        // ── Nav highlight ─────────────────────────────────────────────────────

        /// <summary>
        /// Applies the "active" style to whichever nav button matches pageName,
        /// and resets all others to the default style.
        ///
        /// Button.Tag holds the page name that NavigateCommand receives.
        /// We compare Tag to pageName (case-insensitive) to find the active button.
        /// </summary>
        private void UpdateNavHighlight(string pageName)
        {
            // Both styles must exist in Window.Resources
            var defaultStyle = (Style)Resources["NavButtonStyle"];
            var activeStyle  = (Style)Resources["NavButtonActiveStyle"];

            foreach (var btn in _navButtons)
            {
                // Tag holds the CommandParameter value set in XAML (e.g. "Home", "Memory")
                bool isActive = btn.Tag is string tag &&
                                tag.Equals(pageName, System.StringComparison.OrdinalIgnoreCase);

                btn.Style = isActive ? activeStyle : defaultStyle;
            }
        }

        // ── Exit ──────────────────────────────────────────────────────────────

        private void OnExitAppClick(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
