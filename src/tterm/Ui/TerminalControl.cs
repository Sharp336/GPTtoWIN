using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using tterm.Ansi;
using tterm.Extensions;
using tterm.Terminal;
using static System.Net.Mime.MediaTypeNames;
using SelectionMode = tterm.Terminal.SelectionMode;

namespace tterm.Ui
{
    internal class TerminalControl : Canvas
    {
        private const int UpdateTimerIntervalMs = 50;
        private const int UpdateTimerTriggerMs = 50;
        private const int UpdateTimerTriggerIntervalCount = 5;

        private readonly TerminalColourHelper _colourHelper = new TerminalColourHelper();
        private readonly List<TerminalControlLine> _lines = new List<TerminalControlLine>();

        private TerminalSession _session;

        private FontFamily _fontFamily;
        private double _fontSize;
        private FontStyle _fontStyle;
        private FontWeight _fontWeight;
        private FontStretch _fontStretch;

        private Size? _charSize;

        private readonly DispatcherTimer _updateTimer;
        private readonly object _updateRequestIntervalsSync = new object();
        private readonly int[] _updateRequestIntervals = new int[UpdateTimerTriggerIntervalCount];
        private int _updateRequestIntervalsIndex;
        private int _lastUpdateTick;
        private int _updateAvailable;

        private int _focusTick;

        public bool EnableDebug = true;

        public event EventHandler<string> LastResultCollected;

        private List<PromptRegexp> _promtRegexList = new List<PromptRegexp>();

        public List<PromptRegexp> PromtRegexList
        {
            get => _promtRegexList;
            set
            {
                // Фильтрация списка, исключение элементов с IsOn = false или пустым Regex
                _promtRegexList = value.Where(r => r.IsOn && !string.IsNullOrEmpty(r.Regex)).ToList();
                Debug.WriteLine($"Updated the _promtRegexList:\n{string.Join(",", _promtRegexList.Select(pr => pr.Name).ToArray())}");
            }
        }


        private bool _isCollectingNewResult = false;
        private string _lastCommandPrompt = "";
        private StringBuilder _lastCollectedResult = new StringBuilder();
        private TerminalTagArray _lastCommandLineTags = new TerminalTagArray();

        //int? cmdPid;

        public TerminalSession Session
        {
            get => _session;
            set
            {
                if (_session != value)
                {
                    if (_session != null)
                    {
                        _session.OutputReceived -= OnOutputReceived;
                        _session.BufferSizeChanged -= OnBufferSizeChanged;
                    }
                    _session = value;
                    if (value != null)
                    {
                        _session.OutputReceived += OnOutputReceived;
                        _session.BufferSizeChanged += OnBufferSizeChanged;
                    }
                    UpdateContent();
                    //cmdPid = ProcessExtensions.FindCmdProcessPidWithWinptyAgentParent();
                }
            }
        }

        public TerminalBuffer Buffer => _session?.Buffer;

        public FontFamily FontFamily
        {
            get => _fontFamily;
        }

        public double FontSize
        {
            get => _fontSize;
            set
            {
                if (_fontSize != value)
                {
                    _charSize = null;
                    _fontSize = value;
                    foreach (var textBlock in _lines)
                    {
                        textBlock.FontSize = value;
                    }
                    ClearLastCommandLineTags();
                }
            }
        }

        public FontStyle FontStyle
        {
            get => _fontStyle;
        }

        public FontWeight FontWeight
        {
            get => _fontWeight;
        }

        public FontStretch FontStretch
        {
            get => _fontStretch;
        }

        private DpiScale Dpi => VisualTreeHelper.GetDpi(this);

        private bool IsSessionAvailable => _session != null;

        public Size CharSize
        {
            get
            {
                if (!_charSize.HasValue)
                {
                    var charSize = MeasureString(" ");
                    charSize.Height = Math.Floor(charSize.Height);
                    _charSize = charSize;
                }
                return _charSize.Value;
            }
        }

        static TerminalControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(TerminalControl), new FrameworkPropertyMetadata(typeof(TerminalControl)));
        }

        public TerminalControl()
        {
            Background = new BrushConverter().ConvertFromString("#FF1E1E1E") as Brush;
            _fontFamily = new FontFamily("Consolas");
            _fontSize = 20;
            _fontStyle = FontStyles.Normal;
            _fontWeight = FontWeights.Regular;
            _fontStretch = FontStretches.Normal;
            Focusable = true;
            FocusVisualStyle = null;
            SnapsToDevicePixels = true;
            ClipToBounds = true;

            _updateTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(UpdateTimerIntervalMs),
                DispatcherPriority.Background,
                OnTimeControlledUpdate,
                Dispatcher);
            _updateTimer.Stop();
        }

        #region Layout

        private TerminalControlLine CreateLine()
        {
            var line = new TerminalControlLine()
            {
                Typeface = new Typeface(_fontFamily, _fontStyle, _fontWeight, _fontStretch),
                FontSize = _fontSize,
                ColourHelper = _colourHelper,
                SnapsToDevicePixels = true
            };
            return line;
        }

        private void SetLineCount(int lineCount)
        {
            if (_lines.Count < lineCount)
            {
                while (_lines.Count < lineCount)
                {
                    var textBlock = CreateLine();
                    _lines.Add(textBlock);
                    Children.Add(textBlock);
                }
                AlignTextBlocks();
            }
            else if (_lines.Count > lineCount)
            {
                int removeIndex = lineCount;
                int removeCount = _lines.Count - lineCount;
                _lines.RemoveRange(removeIndex, removeCount);
                Children.RemoveRange(removeIndex, removeCount);
            }
        }

        private void AlignTextBlocks()
        {
            int y = 0;
            int lineHeight = (int)CharSize.Height;
            for (int i = 0; i < _lines.Count; i++)
            {
                var textBlock = _lines[i];
                Canvas.SetTop(textBlock, y);
                Canvas.SetBottom(textBlock, y + lineHeight);
                Canvas.SetLeft(textBlock, 0);
                Canvas.SetRight(textBlock, ActualWidth);
                y += lineHeight;
            }
        }

        private Size MeasureString(string candidate)
        {
            var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
            var formattedText = new FormattedText(
                candidate,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                FontSize,
                Brushes.Black,
                Dpi.PixelsPerDip);

            var result = new Size(formattedText.WidthIncludingTrailingWhitespace, formattedText.Height);
            Debug.Assert(result.Width > 0);
            Debug.Assert(result.Height > 0);
            return result;
        }

        private TerminalControlLine GetLineAt(Point pos, out int row)
        {
            TerminalControlLine result = null;
            row = (int)(pos.Y / CharSize.Height);
            if (_lines.Count > row)
            {
                result = _lines[row];
            }
            return result;
        }

        private TerminalPoint? GetBufferCoordinates(Point pos)
        {
            TerminalPoint? result = null;
            TerminalControlLine line = GetLineAt(pos, out int row);
            if (line != null)
            {
                pos = TranslatePoint(pos, line);
                int col = line.GetColumnAt(pos);
                row += Buffer.WindowTop;
                result = new TerminalPoint(col, row);
            }
            return result;
        }

        #endregion

        #region Render

        public void UpdateContent()
        {
            Interlocked.Increment(ref _updateAvailable);

            // If we are getting a lot of update requests, off load it to our update timer for a more
            // steady refresh rate that doesn't slow the UI down
            bool heavyLoad = false;
            lock (_updateRequestIntervalsSync)
            {
                int currentTick = Environment.TickCount;
                int interval = currentTick - _lastUpdateTick;
                int[] intervals = _updateRequestIntervals;
                int measureLength = intervals.Length;
                int intervalsIndex = _updateRequestIntervalsIndex;
                intervals[intervalsIndex] = interval;
                intervalsIndex++;
                if (intervalsIndex == measureLength)
                {
                    intervalsIndex = 0;
                }
                _updateRequestIntervalsIndex = intervalsIndex;
                _lastUpdateTick = currentTick;

                int total = 0;
                for (int i = 0; i < intervals.Length; i++)
                {
                    total += intervals[i];
                }
                heavyLoad = (total < UpdateTimerTriggerMs);
            }

            if (heavyLoad && !IsSelecting)
            {
                _updateTimer.IsEnabled = true;
            }
            else
            {
                _updateTimer.IsEnabled = false;
                UpdateContentControlled();
            }
        }

        public void UpdateContentControlled()
        {
            int updateAvailable = Interlocked.Exchange(ref _updateAvailable, 0);
            if (updateAvailable == 0)
            {
                return;
            }
            UpdateContentForced();
        }

        public void UpdateContentForced()
        {
            if (IsSessionAvailable)
            {
                int lineCount = Buffer.Size.Rows;
                var lineTags = new TerminalTagArray[lineCount];
                int windowTop = Buffer.WindowTop;
                PromptRegexp promptRegexpFound = null;
                for (int y = 0; y < lineCount; y++)
                {
                    lineTags[y] = Buffer.GetFormattedLine(windowTop + y);
                }

                Dispatcher.InvokeAsync(() =>
                {
                    SetLineCount(lineCount);
                    for (int y = 0; y < lineCount; y++)
                    {
                        _lines[y].Tags = lineTags[y];
                        if (_isCollectingNewResult && y >= Buffer.CursorY - 1)
                        {
                            foreach (PromptRegexp pr in PromtRegexList)
                            {
                                if (new Regex(pr.Regex).Match(lineTags[y].ToString().Trim()).Success)
                                {
                                    Debug.WriteLine($"{pr.Name} found in UpdateContentForced");
                                    promptRegexpFound = pr;
                                }
                            }
                        }
                    }
                    _lastUpdateTick = Environment.TickCount;
                    if (promptRegexpFound != null)
                    {
                        if (promptRegexpFound.Reply != null && promptRegexpFound.Reply.Length > 0)
                        {
                            Debug.WriteLine($"Executing automatic answer: {promptRegexpFound.Reply}");
                            TypeAndMaybeExecute(promptRegexpFound.Reply, true);
                        }
                        else
                        {
                            Debug.WriteLine("Automatically collecting new result");
                            CollectLastResult();
                            _isCollectingNewResult = false;
                        }
                    }
                });
            }
            else
            {
                Dispatcher.InvokeAsync(() =>
                {
                    _lines.Clear();
                    Children.Clear();
                });
            }
        }

        public  bool IsProcessWaitExecutive(int processId)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                foreach (ProcessThread thread in process.Threads)
                {
                    if (thread.ThreadState == System.Diagnostics.ThreadState.Wait &&
                        thread.WaitReason == ThreadWaitReason.Executive)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                // Handle exceptions, such as process not found.
            }
            return false;
        }

        public void ClearLastCommandLineTags() => _lastCommandLineTags = new TerminalTagArray();

        public string CollectLastResult()
        {
            if (_lastCollectedResult.Length != 0)
                return _lastCollectedResult.ToString();

            bool isLastCommandLineFound = false;

            for (int i = Buffer.CursorY; i >= 0; i--)
            {
                var lineContent = Buffer.GetFormattedLine(i).ToString();
                _lastCollectedResult.Insert(0, lineContent.TrimEnd() + '\n');

                // Check if it's the last command line
                if (i != Buffer.CursorY)
                {
                    bool isEqual = _lastCommandLineTags.Count() > 0 ?
                        _lines[i].Tags.Equals(_lastCommandLineTags) :
                        lineContent.StartsWith(_lastCommandPrompt);

                    if (isEqual)
                    {
                        isLastCommandLineFound = true;
                        break;
                    }
                }
            }

            // If not found in the visible buffer, search in history
            if (!isLastCommandLineFound)
            {
                for (int i = Buffer.History.Count - 1; i >= 0; i--)
                {
                    var lineContent = Buffer.History[i].ToString();
                    _lastCollectedResult.Insert(0, lineContent.TrimEnd() + '\n');

                    if (lineContent.StartsWith(_lastCommandPrompt))
                        break;
                }
            }
            var result = _lastCollectedResult.ToString().Trim();

            LastResultCollected(this, result);

            Debug.WriteLine($"New result collected:\n{result}\n---");

            return result;
        }



        #endregion

        #region Selection

        private bool IsSelecting => Buffer.Selection != null;

        private void ClearSelection()
        {
            Buffer.Selection = null;
            UpdateContentForced();
        }

        private void StartSelectionAt(TerminalPoint startPoint)
        {
            var mode = SelectionMode.Stream;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                mode = SelectionMode.Block;
            }
            Buffer.Selection = new TerminalSelection(mode, startPoint, startPoint);
            UpdateContentForced();
        }

        private void EndSelectionAt(TerminalPoint endPoint)
        {
            if (Buffer.Selection != null)
            {
                var mode = SelectionMode.Stream;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                {
                    mode = SelectionMode.Block;
                }
                var startPoint = Buffer.Selection.Start;
                Buffer.Selection = new TerminalSelection(mode, startPoint, endPoint);
                UpdateContentForced();
            }
        }

        #endregion

        #region Events

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            _charSize = null;
            AlignTextBlocks();
            return base.ArrangeOverride(arrangeSize);
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            _focusTick = e.Timestamp;
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            if (!IsSessionAvailable)
            {
                return;
            }

            Focus();

            // Prevent selection if we have just gained focus of the control
            if (e.Timestamp - _focusTick < 100)
            {
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(this);
                var point = GetBufferCoordinates(pos);
                if (point.HasValue)
                {
                    StartSelectionAt(point.Value);
                }
            }
            else if (e.MiddleButton == MouseButtonState.Pressed ||
                     e.RightButton == MouseButtonState.Pressed)
            {
                if (Buffer.Selection != null)
                {
                    Buffer.CopySelection();
                    ClearSelection();
                }
                else
                {
                    _session.Paste();
                }
            }
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            if (!IsSessionAvailable)
            {
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed && IsSelecting)
            {
                var pos = e.GetPosition(this);
                var point = GetBufferCoordinates(pos);
                if (point.HasValue)
                {
                    EndSelectionAt(point.Value);
                }
            }
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.None)
            {
                double delta = -e.Delta / 40.0;
                int offset = (int)delta;
                if (offset < 0 && Buffer.WindowTop > 0 && Buffer.WindowTop + offset < 0)
                {
                    offset = -Buffer.WindowTop;
                }
                else if (offset > 0 && Buffer.WindowTop < 0 && Buffer.WindowTop + offset > 0)
                {
                    offset = -Buffer.WindowTop;
                }
                Buffer.Scroll(offset);

                // Update selection
                if (IsSelecting)
                {
                    var pos = e.GetPosition(this);
                    var point = GetBufferCoordinates(pos);
                    if (point.HasValue)
                    {
                        EndSelectionAt(point.Value);
                    }
                }

                UpdateContentForced();
                e.Handled = true;
            }
            else
            {
                base.OnPreviewMouseWheel(e);
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (!IsSessionAvailable)
            {
                return;
            }

            ModifierKeys modifiers = e.KeyboardDevice.Modifiers;
            int modCode = 0;
            if (modifiers.HasFlag(ModifierKeys.Shift)) modCode |= 1;
            if (modifiers.HasFlag(ModifierKeys.Alt)) modCode |= 2;
            if (modifiers.HasFlag(ModifierKeys.Control)) modCode |= 4;
            if (modifiers.HasFlag(ModifierKeys.Windows)) modCode |= 8;

            if (IsSelecting && !modifiers.HasFlag(ModifierKeys.Alt))
            {
                ClearSelection();
                if (e.Key == Key.Escape)
                {
                    return;
                }
            }

            string text = string.Empty;
            switch (e.Key)
            {
                case Key.Escape:
                    text = $"{C0.ESC}{C0.ESC}{C0.ESC}";
                    break;
                case Key.Back:
                    text = modifiers.HasFlag(ModifierKeys.Shift) ?
                        C0.BS.ToString() :
                        C0.DEL.ToString();
                    break;
                case Key.Delete:
                    text = (modCode == 0) ?
                        $"{C0.ESC}[3~" :
                        $"{C0.ESC}[3;{modCode + 1}~";
                    break;
                case Key.Tab:
                    text = modifiers.HasFlag(ModifierKeys.Shift) ?
                        $"{C0.ESC}[Z" :
                        C0.HT.ToString();
                    break;
                case Key.Up:
                    text = Construct(1, 'A');
                    break;
                case Key.Down:
                    text = Construct(1, 'B');
                    break;
                case Key.Right:
                    text = Construct(1, 'C');
                    break;
                case Key.Left:
                    text = Construct(1, 'D');
                    break;
                case Key.Home:
                    text = Construct(1, 'H');
                    break;
                case Key.End:
                    text = Construct(1, 'F');
                    break;
                case Key.PageUp:
                    text = $"{C0.ESC}[5~";
                    break;
                case Key.PageDown:
                    text = $"{C0.ESC}[6~";
                    break;
                case Key.Return:
                    OnEnterKeyPressed();
                    text = C0.CR.ToString();
                    break;
                case Key.Space:
                    text = " ";
                    break;
                case Key.F1:
                    text = Construct(1, 'P');
                    break;
                case Key.F2:
                    text = Construct(1, 'Q');
                    break;
                case Key.F3:
                    text = Construct(1, 'R');
                    break;
                case Key.F4:
                    text = Construct(1, 'S');
                    break;
                case Key.F5:
                    text = (modCode == 0) ?
                        $"{C0.ESC}[15~" :
                        $"{C0.ESC}[15;{modCode + 1}~";
                    break;
                case Key.F6:
                    text = (modCode == 0) ?
                        $"{C0.ESC}[17~" :
                        $"{C0.ESC}[17;{modCode + 1}~";
                    break;
                case Key.F7:
                    text = (modCode == 0) ?
                        $"{C0.ESC}[18~" :
                        $"{C0.ESC}[18;{modCode + 1}~";
                    break;
                case Key.F8:
                    text = (modCode == 0) ?
                        $"{C0.ESC}[19~" :
                        $"{C0.ESC}[19;{modCode + 1}~";
                    break;
                case Key.F9:
                    text = (modCode == 0) ?
                        $"{C0.ESC}[20~" :
                        $"{C0.ESC}[20;{modCode + 1}~";
                    break;
                case Key.F10:
                    text = (modCode == 0) ?
                        $"{C0.ESC}[21~" :
                        $"{C0.ESC}[21;{modCode + 1}~";
                    break;
                case Key.F11:
                    text = (modCode == 0) ?
                        $"{C0.ESC}[23~" :
                        $"{C0.ESC}[23;{modCode + 1}~";
                    break;
                case Key.F12:
                    text = (modCode == 0) ?
                        $"{C0.ESC}[24~" :
                        $"{C0.ESC}[24;{modCode + 1}~";
                    break;
            }
            if (text != string.Empty)
            {
                _session.Write(text);
                e.Handled = true;
            }

            string Construct(int a, char c)
            {
                return (modCode == 0) ?
                    $"{C0.ESC}O{c}" :
                    $"{C0.ESC}[{a};{modCode + 1}{c}";
            }
        }

        public void TypeAndMaybeExecute(string command, bool execute = false)
        {
            if (string.IsNullOrWhiteSpace(command)) command = "echo 'the command passed to TypeAndMaybeExecute is empty'";
            _session.Write($"{C0.ESC}{C0.ESC}{C0.ESC}");
            _session.Write(command);
            if (execute)
            {
                OnEnterKeyPressed(CollectLastCommandLineTags: false);
                _session.Write(C0.CR.ToString());
            }
        }

        private void OnEnterKeyPressed(bool CollectLastCommandLineTags = true)
        {
            Debug.WriteLine($"OnEnterKeyPressed, _isCollectingNewResult is {_isCollectingNewResult}");
            if (!_isCollectingNewResult)
            {
                int LinePromptMatchY = Buffer.CursorY;
                Match LinePromptMatch = null;
                _lastCommandPrompt = string.Empty;

                // Итерируемся по строкам в обратном порядке, начиная с текущей позиции курсора
                while (LinePromptMatchY >= 0 && string.IsNullOrEmpty(_lastCommandPrompt))
                {
                    foreach (var pr in PromtRegexList)
                    {
                        LinePromptMatch = new Regex(pr.Regex).Match(_lines[LinePromptMatchY].Tags.ToString());
                        if (LinePromptMatch.Success)
                        {
                            _lastCommandPrompt = LinePromptMatch.Value;
                            break; // Прерываем цикл регулярных выражений при первом же успешном соответствии
                        }
                    }
                    LinePromptMatchY--; // Перемещаемся на строку выше, если не найдено соответствий
                }

                if (CollectLastCommandLineTags && !string.IsNullOrEmpty(_lastCommandPrompt))
                {
                    var promptLineTags = _lines[LinePromptMatchY + 1].Tags; // Восстанавливаем индекс строки с последним найденным промптом
                    var tags = ImmutableArray.CreateBuilder<TerminalTag>(initialCapacity: 1);
                    tags.Add(new TerminalTag(promptLineTags.ToString(), new CharAttributes()));
                    _lastCommandLineTags = new TerminalTagArray(tags.ToImmutable());
                }
                //Debug.WriteLine($"_lastCollectedResult is:\n{_lastCollectedResult}\n---");
                _lastCollectedResult.Clear();
                //Debug.WriteLine($"_lastCollectedResult is:\n{_lastCollectedResult}\n---");
                _isCollectingNewResult = true;
            }
        }


        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {
            if (!IsSessionAvailable)
            {
                return;
            }

            string text = e.Text;
            if (string.IsNullOrEmpty(text))
            {
                text = e.ControlText;
            }
            _session.Write(text);
            e.Handled = true;
        }

        private void OnOutputReceived(object sender, EventArgs e)
        {
            UpdateContent();

            //if (cmdPid != null) Console.WriteLine(IsProcessWaitExecutive((int)cmdPid));
            //else Console.WriteLine("cmdPid = null");
        }

        private void OnBufferSizeChanged(object sender, EventArgs e)
        {
            UpdateContent();
        }

        private void OnTimeControlledUpdate(object sender, EventArgs e)
        {
            UpdateContentControlled();
        }

        #endregion
    }
}
