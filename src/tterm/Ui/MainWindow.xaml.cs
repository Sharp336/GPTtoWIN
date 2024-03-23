using MahApps.Metro.IconPacks;
using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using tterm.Remote;
using tterm.Terminal;
using tterm.Ui.Models;


namespace tterm.Ui
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 

    public partial class MainWindow : EnhancedWindow, INotifyPropertyChanged
    {
        private const int MinColumns = 52;
        private const int MinRows = 4;
        private const int ReadyDelay = 1000;

        private int _tickInitialised;
        private bool _ready;
        private Size _consoleSizeDelta;

        private readonly ObservableCollection<TabDataItem> _tabs = new ObservableCollection<TabDataItem>();

        private TerminalSessionManager _sessionMgr = new TerminalSessionManager();
        private TerminalSession _currentSession;
        private TerminalSize _terminalSize;

        private RemoteManager _remoteManager;
        public ConfigurationService ConfigService { get; set; } = new ConfigurationService();

        private TabDataItem AutoSendTab = new TabDataItem() { Title = "Auto-send" };
        private TabDataItem SendOutputTab = new TabDataItem() { Title = "Send output" };
        private TabDataItem AutoTypeTab = new TabDataItem() { Title = "Auto-type" };
        private TabDataItem AutoExecuteTab = new TabDataItem() { Title = "Auto-execute", IsDisabled = true };
        private TabDataItem TypeReceivedTab = new TabDataItem() { Title = "Type received", IsDisabled = true };
        private TabDataItem ExecuteReceivedTab = new TabDataItem() { Title = "Execute received", IsDisabled = true };

        public event PropertyChangedEventHandler PropertyChanged;

        private Profile _currentProfile;


        // Текущий выбранный профиль
        public Profile CurrentProfile
        {
            get => _currentProfile;
            set
            {
                if (_currentProfile != value)
                {
                    _currentProfile = value;
                    OnPropertyChanged(nameof(CurrentProfile));
                    Debug.WriteLine($"Changed profile to {_currentProfile.ProfileName}");
                    terminalControl.PromtRegexList = _currentProfile.PromptRegexps;
                    CreateSession(_currentProfile);
                    // Вы можете добавить здесь дополнительную логику при смене профиля
                }
            }
        }

        private bool _isNotificationsOn = false;

        public bool IsNotificationsOn
        {
            get => _isNotificationsOn;
            set
            {
                if (_isNotificationsOn != value)
                {
                    _isNotificationsOn = value;
                    OnPropertyChanged(nameof(IsNotificationsOn));
                }
            }
        }

        private void WsPortTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Регулярное выражение, проверяющее, что вводится только число
            e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
        }

        private void WsPortTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(WsPortTextBox.Text, out int newPort) && _remoteManager.IsPortValid(newPort))
            {
                // Если введенное значение является числом в допустимом диапазоне, обновите порт
                _remoteManager.WsPort = newPort;
                ConfigService.Config.Port = newPort;
            }
            else
            {
                // Если введенное значение недопустимо, верните предыдущее допустимое значение
                WsPortTextBox.Text = _remoteManager.WsPort.ToString();
                MessageBox.Show("Please enter a valid port number (1-65535).", "Attention!");
            }
        }
        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

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

            var config = ConfigService.Load();
            if (config.AllowTransparancy)
            {
                AllowsTransparency = true;
            }

            this.Deactivated += MainWindow_Deactivated;
            this.PreviewMouseDown += MainWindow_PreviewMouseDown;

            resizeHint.Visibility = Visibility.Hidden;

            _remoteManager = new RemoteManager(OnMessageReceived, OnConnectionStateChanged, config.Port);
            WsPortTextBox.Text = _remoteManager.WsPort.ToString();
        }

        private void MainWindow_Deactivated(object sender, EventArgs e)
        {
            LeftPopup.IsOpen = false;
            RightPopup.IsOpen = false;
        }



        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var config = ConfigService.Config;

            int columns = Math.Max(config.Columns, MinColumns);
            int rows = Math.Max(config.Rows, MinRows);

            _terminalSize = new TerminalSize(columns, rows);
            FixWindowSize();

            CurrentProfile = ConfigService.DefaultProfile();
        }


        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            _tickInitialised = Environment.TickCount;


            LeftButton.Click += (sender, args) =>
            {
                if (LeftPopup.IsOpen)
                {
                    ConfigService.Save();
                    LeftPopup.IsOpen = false;
                    terminalControl.Focus();
                }
                else LeftPopup.IsOpen = true;
            };

            RightButton.Click += (sender, args) =>
            {
                if (RightPopup.IsOpen)
                {
                    terminalControl.PromtRegexList = CurrentProfile.PromptRegexps;
                    Debug.WriteLine($"\nPromtregexps edited:\n{string.Join("\n", CurrentProfile.PromptRegexps.Select(pr => pr.Name + ":\n" + pr.Regex + "\n" + pr.Reply))}");
                    ConfigService.Save();
                    RightPopup.IsOpen = false;
                    terminalControl.Focus();
                }
                else RightPopup.IsOpen = true;
            };

            LeftButton.Content = new PackIconMaterial { Kind = PackIconMaterialKind.CloseNetwork };
            RightButton.Content = new PackIconMaterial { Kind = PackIconMaterialKind.Settings };

            tabBar.DataContext = _tabs;
            //Debug.WriteLine($"On DataContext binding CurrentProfile is: {CurrentProfile.ProfileName}");d
            DataContext = this;

            AutoSendTab.Click += ToggleAutoSend_Click;
            SendOutputTab.Click += SendLastOutputManually_Click;
            AutoTypeTab.Click += AutoTypeToggleTab_Click;
            AutoExecuteTab.Click += AutoExecuteToggleTab_Click;
            TypeReceivedTab.Click += TypeRecievedManuallyTab_Click;
            ExecuteReceivedTab.Click += ExecuteManuallyTab_Click;
        }

        private void AddTabs()
        {
            _tabs.Add(AutoSendTab);
            _tabs.Add(SendOutputTab);
            _tabs.Add(AutoTypeTab);
            _tabs.Add(AutoExecuteTab);
            _tabs.Add(TypeReceivedTab);
            _tabs.Add(ExecuteReceivedTab);
        }

        //private void InitializeTab(string title, EventHandler clickHandler, PackIconMaterialKind icon = PackIconMaterialKind.None, bool isDisabled = false)
        //{
        //    var tab = new TabDataItem { IsDisabled = isDisabled };

        //    if (icon != PackIconMaterialKind.None) tab.Image = icon;
        //    else tab.Title = title;

        //    if (clickHandler != null) tab.Click += clickHandler;

        //    _tabs.Add(tab);
        //}

        private void Test_Click(object sender, EventArgs e)
        {
            new ToastContentBuilder()
                .AddArgument("action", "viewConversation")
                .AddArgument("conversationId", 9813)
                .AddText($"WS port is: {_remoteManager.WsPort}")
                .AddText("Check this out, The Enchantments in Washington!")
                .Show();
            terminalControl.Focus();
        }


        //public void MonitorConsoleProcess()
        //{
        //    Process[] processes = Process.GetProcessesByName("cmd");
        //    foreach (Process proc in processes)
        //    {
        //        foreach (ProcessThread thread in proc.Threads)
        //        {

        //            if (thread.ThreadState == ThreadState.Wait
        //                && (thread.WaitReason == ThreadWaitReason.UserRequest || thread.WaitReason == ThreadWaitReason.LpcReply))
        //            {
        //                Debug.WriteLine($"Cmd is waiting due to: {thread.WaitReason}");
        //            }
        //        }
        //    }
        //}


        private void NewSessionTab_Click(object sender, EventArgs e)
        {
            CreateSession(CurrentProfile);
            terminalControl.Focus();
        }

        private void ToggleAutoSend_Click(object sender, EventArgs e)
        {
            var tab = sender as TabDataItem;
            if (tab != null)
            {
                tab.IsActive = !tab.IsActive;
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
                if (!tab.IsActive) AutoExecuteTab.IsActive = false;
                AutoExecuteTab.IsDisabled = !tab.IsActive;
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
            if (tab != null && !tab.IsDisabled) terminalControl.TypeAndMaybeExecute(_remoteManager.LastRecievedMessage, false);
            terminalControl.Focus();
        }

        private void ExecuteManuallyTab_Click(object sender, EventArgs e)
        {
            var tab = sender as TabDataItem;
            if (tab != null && !tab.IsDisabled) terminalControl.TypeAndMaybeExecute(_remoteManager.LastRecievedMessage, true);
            terminalControl.Focus();
        }

        private void AddPromptRegexp_Click(object sender, RoutedEventArgs e)
        {
            // Добавление нового регулярного выражения в список
            var newRegexp = new PromptRegexp() { Name = "New prompt", Regex = @"New expression", IsOn = true };
            CurrentProfile.PromptRegexps.Add(newRegexp);
            ListViewPromptRegexps.Items.Refresh();
        }

        private void DeletePromptRegexp_Click(object sender, RoutedEventArgs e)
        {
            // Удаление выбранного регулярного выражения из списка
            var button = sender as Button;
            var regexpToDelete = button.DataContext as PromptRegexp;
            if (regexpToDelete != null)
            {
                CurrentProfile.PromptRegexps.Remove(regexpToDelete);
                ListViewPromptRegexps.Items.Refresh();
            }
        }


        private void OnConnectionStateChanged(string status, bool isConnected)
        {
            Debug.WriteLine($"Updated status:\n{status}\nisConnected - {isConnected}");
            
            Dispatcher.InvokeAsync(() =>
            {
                LeftButton.Content = new PackIconMaterial { Kind = isConnected ? PackIconMaterialKind.NetworkDownload : PackIconMaterialKind.CloseNetwork };
                StatusTextBlock.Text = $"Status: {status}";
            });
        }

        private void OnMessageReceived(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                LastReceivedTextBox.Text = message;
                TypeReceivedTab.IsDisabled = false;
                ExecuteReceivedTab.IsDisabled = false;
                if (AutoTypeTab.IsActive)
                {
                    terminalControl.TypeAndMaybeExecute(message, AutoExecuteTab.IsActive);
                }
            }
            else
            {
                TypeReceivedTab.IsDisabled = true;
                ExecuteReceivedTab.IsDisabled = true;
            }
        }

        protected override void OnForked(ForkData data)
        {
            Profile profile = CurrentProfile;
            profile.CurrentWorkingDirectory = data.CurrentWorkingDirectory;
            profile.EnvironmentVariables = data.Environment;
            CreateSession(profile);
        }

        private void CreateSession(Profile profile)
        {
            var ExpandedProfile = ExpandVariables(profile);
            var session = _sessionMgr.CreateSession(_terminalSize, ExpandedProfile);
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

                    _tabs.Clear();
                    AddTabs();
                }

                terminalControl.Session = session;
                terminalControl.LastResultCollected += ProcessCollectedResult;
                terminalControl.Focus();

            }
        }

        private async void ProcessCollectedResult(object sender, string result)
        {
            if(AutoSendTab.IsActive) await _remoteManager.TrySendingMessage(result);
            if (IsNotificationsOn) SendCommandResultNotification("Prompt has been detected", result);
        }

        // Метод для отправки уведомления
        public void SendCommandResultNotification(string title, string commandResult)
        {
            // Создание содержимого уведомления
            new ToastContentBuilder()
                .AddText(title) // Добавление заголовка уведомления
                .AddText("Last output:") // Подзаголовок
                .AddText(commandResult) // Добавление текста с результатом выполнения команды
                .Show(); // Отображение уведомления
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
            session.Dispose();
            _tabs.Clear();

            TabDataItem newSessionTab = new TabDataItem() { Title = "Start new session", Image = PackIconMaterialKind.Plus };
            newSessionTab.Click += NewSessionTab_Click;
            _tabs.Add(newSessionTab);
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
                ProfileName = profile.ProfileName,
                Command = envHelper.ExpandVariables(profile.Command, env),
                CurrentWorkingDirectory = envHelper.ExpandVariables(profile.CurrentWorkingDirectory, env),
                Arguments = profile.Arguments?.Select(x => envHelper.ExpandVariables(x, env)).ToArray(),
                EnvironmentVariables = env,
                PromptRegexps = profile.PromptRegexps
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

        private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Проверяем, открыт ли хотя бы один попап
            if (LeftPopup.IsOpen || RightPopup.IsOpen)
            {
                // Закрыть попапы, если источник события не является частью содержимого попапа или кнопками тогглами
                if (!(e.Source is Visual sourceVisual) || !IsPopupChildOrToggleButton(sourceVisual))
                {
                    // Смещаем фокус на terminalControl до закрытия попапов, чтобы снять фокус с редактируемых элементов, обновив значения
                    terminalControl.Focus();

                    terminalControl.PromtRegexList = CurrentProfile.PromptRegexps;
                    Debug.WriteLine($"\nPromtregexps edited:\n{string.Join("\n", CurrentProfile.PromptRegexps.Select(pr => pr.Name + ":\n" + pr.Regex + "\n" + pr.Reply))}");
                    ConfigService.Save();

                    LeftPopup.IsOpen = false;
                    RightPopup.IsOpen = false;

                }
            }
        }


        // Метод для проверки, является ли элемент частью содержимого попапа
        private bool IsPopupChildOrToggleButton(Visual visual)
        {
            while (visual != null && !(visual is Popup))
            {
                if (visual == LeftPopup.Child || visual == RightPopup.Child)
                {
                    return true;
                }
                // Проверяем, является ли текущий элемент одной из кнопок, отвечающих за попапы
                if (visual is Button button && (button == LeftButton || button == RightButton))
                {
                    return true;
                }
                visual = (Visual)VisualTreeHelper.GetParent(visual);
            }
            return false;
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
                    ConfigService.Config.Columns = size.Columns;
                    ConfigService.Config.Rows = size.Rows;
                    ConfigService.Save();

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
