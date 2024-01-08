using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MahApps.Metro.IconPacks;
using tterm.Ansi;
using tterm.Extensions;
using tterm.Remote;
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

        private RemoteManager _remoteManager;
        private string _lastRecievedMessage = "";

        private TabDataItem _addSessionTab = new TabDataItem() { Image = PackIconMaterialKind.Plus };

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

            InitializeTabs();
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            _tickInitialised = Environment.TickCount;
            _remoteManager = new RemoteManager();
            _remoteManager.StateHasChanged += OnConnectionStateChanged;
            _remoteManager.MessageReceived += OnMessageReceived;
        }

        private void InitializeTabs()
        {
            tabBar.DataContext = _tabs;

            InitializeTab("Starting WebSocket server", Test_Click);
            InitializeTab("Auto-send output", ToggleAutoSend_Click);
            InitializeTab("Send last output", SendLastOutputManually_Click);
            InitializeTab("Auto-type received", AutoTypeToggleTab_Click);
            InitializeTab("Auto-execute received", AutoExecuteToggleTab_Click, isDisabled: true);
            InitializeTab("Type received", TypeRecievedManuallyTab_Click, isDisabled: true);
            InitializeTab("Execute received message", ExecuteManuallyTab_Click, isDisabled: true);
        }

        private void InitializeTab(string title, EventHandler clickHandler, bool isDisabled = false)
        {
            var tab = new TabDataItem
            {
                Title = title,
                IsDisabled = isDisabled
            };

            if (clickHandler != null)
            {
                tab.Click += clickHandler;
            }

            _tabs.Add(tab);
        }

        private void Test_Click(object sender, EventArgs e)
        {
            terminalControl.Focus();
        }

        private void NewSessionTab_Click(object sender, EventArgs e)
        {
            CreateSession(_defaultProfile);
            terminalControl.Focus();
        }

        private void ToggleAutoSend_Click(object sender, EventArgs e)
        {
            var tab = sender as TabDataItem;
            if (tab != null)
            {
                tab.IsActive = !tab.IsActive;
                terminalControl.isAutoSendOn = tab.IsActive;
            }
            terminalControl.Focus();
        }

        private async void SendLastOutputManually_Click(object sender, EventArgs e)
        {
            var tab = sender as TabDataItem;
            if (tab != null)
            {
                var message = terminalControl.CollectLastResult();
                await _remoteManager.TrySendingMessage(message);
            }
            terminalControl.Focus();
        }


        private void AutoTypeToggleTab_Click(object sender, EventArgs e)
        {
            var tab = sender as TabDataItem;
            if (tab != null)
            {
                tab.IsActive = !tab.IsActive;
                _tabs[4].IsDisabled = !tab.IsActive;
            }
            terminalControl.Focus();
        }

        private void AutoExecuteToggleTab_Click(object sender, EventArgs e)
        {
            var tab = sender as TabDataItem;
            if (tab != null && !tab.IsDisabled) tab.IsActive = !tab.IsActive;
            terminalControl.Focus();
        }

        private void TypeRecievedManuallyTab_Click(object sender, EventArgs e)
        {
            var tab = sender as TabDataItem;
            if (tab != null && !tab.IsDisabled) terminalControl.TypeAndMaybeExecute(_lastRecievedMessage, false);
            terminalControl.Focus();
        }

        private void ExecuteManuallyTab_Click(object sender, EventArgs e)
        {
            var tab = sender as TabDataItem;
            if (tab != null && !tab.IsDisabled) terminalControl.TypeAndMaybeExecute(_lastRecievedMessage, true);
            terminalControl.Focus();
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

        private void OnConnectionStateChanged(string status, bool isConnected)
        {
            _tabs[0].Title = status;
            _tabs[0].IsActive = isConnected;
            Console.WriteLine(status);
        }

        private void OnMessageReceived(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                _lastRecievedMessage = message;
                _tabs[5].IsDisabled = false;
                _tabs[6].IsDisabled = false;
                if (_tabs[3].IsActive)
                {
                    terminalControl.TypeAndMaybeExecute(_lastRecievedMessage, _tabs[4].IsActive);
                }
            }
            else
            {
                _tabs[5].IsDisabled = true;
                _tabs[6].IsDisabled = true;
            }
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
                terminalControl.LastResultCollected += SendAutoCollectedResult;
                terminalControl.Focus();
            }
        }

        private async void SendAutoCollectedResult(object sender, string result)
        {
            await _remoteManager.TrySendingMessage(result);
        }

        private void CloseSession(TerminalSession session)
        {
            session.Dispose();
        }

        private void OnSessionTitleChanged(object sender, EventArgs e)
        {
            var session = sender as TerminalSession;
            
            Dispatcher.InvokeAsync(() =>
            {
                this.Title = session.Title;
            });
        }

        private void OnSessionFinished(object sender, EventArgs e)
        {
            var session = sender as TerminalSession;
            ChangeSession(null);
            _tabs.Add(_addSessionTab);
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
            terminalControl.ClearLastCommandLineTags();
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
