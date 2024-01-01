using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MahApps.Metro.IconPacks;
using tterm.Extensions;
using tterm.Terminal;
using tterm.Ui.Models;

namespace tterm.Ui
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : EnhancedWindow
    {
        private const int MinColumns = 52;
        private const int MinRows = 4;
        private const int ReadyDelay = 1000;

        private int _tickInitialised;
        private bool _ready;
        private Size _consoleSizeDelta;

        private ConfigurationService _configService = new ConfigurationService();
        private readonly ObservableCollection<TabDataItem> _tabs = new ObservableCollection<TabDataItem>();

        private TerminalSessionManager _sessionMgr = new TerminalSessionManager();
        private TerminalSession _currentSession;
        private TerminalSize _terminalSize;
        private Profile _defaultProfile;
        private TabDataItem _addSession = new TabDataItem() { Image = PackIconMaterialKind.Plus };

        public bool Ready
        {
            get
            {
                // HACK Try and find a more reliable way to check if we are ready.
                //      This is to prevent the resize hint from showing at startup.
                if (!_ready)
                {
                    _ready = Environment.TickCount > _tickInitialised + ReadyDelay;
                }
                return _ready;
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            var config = _configService.Load();
            if (config.AllowTransparancy)
            {
                AllowsTransparency = true;
            }

            resizeHint.Visibility = Visibility.Hidden;

            tabBar.DataContext = _tabs;

            _addSession.Click += NewSessionTab_Click;
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            _tickInitialised = Environment.TickCount;
        }

        private void NewSessionTab_Click(object sender, EventArgs e)
        {
            CreateSession(_defaultProfile);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var config = _configService.Config;

            int columns = Math.Max(config.Columns, MinColumns);
            int rows = Math.Max(config.Rows, MinRows);
            _terminalSize = new TerminalSize(columns, rows);
            FixWindowSize();

            Profile profile = config.Profile;
            if (profile == null)
            {
                profile = DefaultProfile.Get();
            }
            _defaultProfile = ExpandVariables(profile);
            CreateSession(_defaultProfile);
        }

        protected override void OnForked(ForkData data)
        {
            Profile profile = _defaultProfile;
            profile.CurrentWorkingDirectory = data.CurrentWorkingDirectory;
            profile.EnvironmentVariables = data.Environment;
            CreateSession(profile);
        }

        private void CreateSession(Profile profile)
        {
            var session = _sessionMgr.CreateSession(_terminalSize, profile);
            session.TitleChanged += OnSessionTitleChanged;
            session.Finished += OnSessionFinished;

            ChangeSession(session);
        }

        private void ChangeSession(TerminalSession session)
        {
            if (session != _currentSession)
            {
                if (_currentSession != null)
                {
                    _currentSession.Active = false;
                }

                _currentSession = session;

                if (session != null)
                {
                    session.Active = true;
                    session.Size = _terminalSize;
                }

                terminalControl.Session = session;
                terminalControl.Focus();
            }
        }

        private void CloseSession(TerminalSession session)
        {
            session.Dispose();
        }

        private void OnSessionTitleChanged(object sender, EventArgs e)
        {
            var session = sender as TerminalSession;
            this.Title = session.Title;
        }

        private void OnSessionFinished(object sender, EventArgs e)
        {
            var session = sender as TerminalSession;
            ChangeSession(null);
            _tabs.Add(_addSession);
        }

        private static Profile ExpandVariables(Profile profile)
        {
            var envHelper = new EnvironmentVariableHelper();
            var env = envHelper.GetUser();

            var profileEnv = profile.EnvironmentVariables;
            if (profileEnv != null)
            {
                envHelper.ExpandVariables(env, profileEnv);
            }

            return new Profile()
            {
                Command = envHelper.ExpandVariables(profile.Command, env),
                CurrentWorkingDirectory = envHelper.ExpandVariables(profile.CurrentWorkingDirectory, env),
                Arguments = profile.Arguments?.Select(x => envHelper.ExpandVariables(x, env)).ToArray(),
                EnvironmentVariables = env
            };
        }

        private void terminalControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    if (AllowsTransparency)
                    {
                        var terminal = terminalControl;
                        const double OpacityDelta = 1 / 32.0;
                        if (e.Delta > 0)
                        {
                            Opacity = Math.Min(Opacity + OpacityDelta, 1);
                        }
                        else
                        {
                            Opacity = Math.Max(Opacity - OpacityDelta, 0.25);
                        }
                        e.Handled = true;
                    }
                }
                else
                {
                    var terminal = terminalControl;
                    const double FontSizeDelta = 2;
                    double fontSize = terminal.FontSize;
                    if (e.Delta > 0)
                    {
                        if (fontSize < 54)
                        {
                            fontSize += FontSizeDelta;
                        }
                    }
                    else
                    {
                        if (fontSize > 8)
                        {
                            fontSize -= FontSizeDelta;
                        }
                    }
                    if (terminal.FontSize != fontSize)
                    {
                        terminal.FontSize = fontSize;
                        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                        {
                            FixWindowSize();
                        }
                        else
                        {
                            FixTerminalSize();
                        }
                    }
                    e.Handled = true;
                }
            }
        }

        private void FixTerminalSize()
        {
            var size = GetBufferSizeForWindowSize(Size);
            SetTermialSize(size);
        }

        private void FixWindowSize()
        {
            Size = GetWindowSizeForBufferSize(_terminalSize);
        }

        private TerminalSize GetBufferSizeForWindowSize(Size size)
        {
            Size charSize = terminalControl.CharSize;
            Size newConsoleSize = new Size(Math.Max(size.Width - _consoleSizeDelta.Width, 0),
                                           Math.Max(size.Height - _consoleSizeDelta.Height, 0));

            int columns = (int)Math.Floor(newConsoleSize.Width / charSize.Width);
            int rows = (int)Math.Floor(newConsoleSize.Height / charSize.Height);

            columns = Math.Max(columns, MinColumns);
            rows = Math.Max(rows, MinRows);

            return new TerminalSize(columns, rows);
        }

        private Size GetWindowSizeForBufferSize(TerminalSize size)
        {
            Size charSize = terminalControl.CharSize;
            Size snappedConsoleSize = new Size(size.Columns * charSize.Width,
                                               size.Rows * charSize.Height);

            Size result = new Size(Math.Ceiling(snappedConsoleSize.Width + _consoleSizeDelta.Width) + 2,
                                   Math.Ceiling(snappedConsoleSize.Height + _consoleSizeDelta.Height));
            return result;
        }

        protected override Size GetPreferedSize(Size size)
        {
            var tsize = GetBufferSizeForWindowSize(size);
            return GetWindowSizeForBufferSize(tsize);
        }

        protected override Size ArrangeOverride(Size arrangeBounds)
        {
            Size result = base.ArrangeOverride(arrangeBounds);
            _consoleSizeDelta = new Size(Math.Max(arrangeBounds.Width - terminalControl.ActualWidth, 0),
                                         Math.Max(arrangeBounds.Height - terminalControl.ActualHeight, 0));
            return result;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            FixTerminalSize();
        }

        protected override void OnResizeEnded()
        {
            resizeHint.IsShowing = false;
        }

        private void SetTermialSize(TerminalSize size)
        {
            if (_terminalSize != size)
            {
                _terminalSize = size;
                if (_currentSession != null)
                {
                    _currentSession.Size = size;
                }

                if (Ready)
                {
                    // Save configuration
                    _configService.Config.Columns = size.Columns;
                    _configService.Config.Rows = size.Rows;
                    _configService.Save();

                    // Update hint overlay
                    resizeHint.Hint = size;
                    resizeHint.IsShowing = true;
                    resizeHint.IsShowing = IsResizing;
                }
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
        }

        
    }
}
