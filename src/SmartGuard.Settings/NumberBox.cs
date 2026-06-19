using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartGuard.Settings;

public class NumberBox : Control
{
    static NumberBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(NumberBox), new FrameworkPropertyMetadata(typeof(NumberBox)));
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(NumberBox),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(int), typeof(NumberBox),
        new PropertyMetadata(0, OnMinMaxChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(int), typeof(NumberBox),
        new PropertyMetadata(100, OnMinMaxChanged));

    public static readonly DependencyProperty SmallChangeProperty = DependencyProperty.Register(
        nameof(SmallChange), typeof(int), typeof(NumberBox),
        new PropertyMetadata(1));

    public static readonly DependencyProperty TickFrequencyProperty = DependencyProperty.Register(
        nameof(TickFrequency), typeof(int), typeof(NumberBox),
        new PropertyMetadata(1, OnTickFrequencyChanged));

    public static readonly DependencyProperty IsSnapToTickEnabledProperty = DependencyProperty.Register(
        nameof(IsSnapToTickEnabled), typeof(bool), typeof(NumberBox),
        new PropertyMetadata(false));

    public static readonly RoutedEvent ValueChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(ValueChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<int>), typeof(NumberBox));

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int SmallChange
    {
        get => (int)GetValue(SmallChangeProperty);
        set => SetValue(SmallChangeProperty, value);
    }

    public int TickFrequency
    {
        get => (int)GetValue(TickFrequencyProperty);
        set => SetValue(TickFrequencyProperty, value);
    }

    public bool IsSnapToTickEnabled
    {
        get => (bool)GetValue(IsSnapToTickEnabledProperty);
        set => SetValue(IsSnapToTickEnabledProperty, value);
    }

    public event RoutedPropertyChangedEventHandler<int> ValueChanged
    {
        add => AddHandler(ValueChangedEvent, value);
        remove => RemoveHandler(ValueChangedEvent, value);
    }

    public static readonly DependencyProperty IncrementCommandProperty = DependencyProperty.Register(
        nameof(IncrementCommand), typeof(ICommand), typeof(NumberBox),
        new PropertyMetadata(null));

    public static readonly DependencyProperty DecrementCommandProperty = DependencyProperty.Register(
        nameof(DecrementCommand), typeof(ICommand), typeof(NumberBox),
        new PropertyMetadata(null));

    public ICommand IncrementCommand
    {
        get => (ICommand)GetValue(IncrementCommandProperty);
        private set => SetValue(IncrementCommandProperty, value);
    }

    public ICommand DecrementCommand
    {
        get => (ICommand)GetValue(DecrementCommandProperty);
        private set => SetValue(DecrementCommandProperty, value);
    }

    private TextBox? _textBox;

    public NumberBox()
    {
        IncrementCommand = new RelayCommand(_ => Increment(), _ => Value < Maximum);
        DecrementCommand = new RelayCommand(_ => Decrement(), _ => Value > Minimum);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_textBox != null)
        {
            _textBox.LostFocus -= OnTextBoxLostFocus;
            _textBox.KeyDown -= OnTextBoxKeyDown;
        }

        _textBox = GetTemplateChild("PART_TextBox") as TextBox;

        if (_textBox != null)
        {
            _textBox.Text = Value.ToString();
            _textBox.LostFocus += OnTextBoxLostFocus;
            _textBox.KeyDown += OnTextBoxKeyDown;
        }
    }

    private void OnTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (_textBox != null && int.TryParse(_textBox.Text, out int value))
        {
            Value = value;
        }
        else if (_textBox != null)
        {
            _textBox.Text = Value.ToString();
        }
    }

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _textBox != null && int.TryParse(_textBox.Text, out int value))
        {
            Value = value;
            _textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    private void Increment()
    {
        var newValue = IsSnapToTickEnabled
            ? SnapToTick(Value + SmallChange)
            : Value + SmallChange;
        Value = Math.Min(newValue, Maximum);
    }

    private void Decrement()
    {
        var newValue = IsSnapToTickEnabled
            ? SnapToTick(Value - SmallChange)
            : Value - SmallChange;
        Value = Math.Max(newValue, Minimum);
    }

    private int SnapToTick(int value)
    {
        if (TickFrequency <= 0) return value;
        var snapped = (int)Math.Round((double)value / TickFrequency) * TickFrequency;
        return Math.Clamp(snapped, Minimum, Maximum);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (NumberBox)d;
        var oldValue = (int)e.OldValue;
        var newValue = (int)e.NewValue;

        if (box._textBox != null)
            box._textBox.Text = newValue.ToString();

        box.RaiseEvent(new RoutedPropertyChangedEventArgs<int>(oldValue, newValue, ValueChangedEvent));
        (box.IncrementCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (box.DecrementCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private static void OnMinMaxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (NumberBox)d;
        box.Value = Math.Clamp(box.Value, box.Minimum, box.Maximum);
    }

    private static void OnTickFrequencyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (NumberBox)d;
        box.SmallChange = (int)e.NewValue;
    }

    private static object CoerceValue(DependencyObject d, object baseValue)
    {
        var box = (NumberBox)d;
        var value = (int)baseValue;
        return Math.Clamp(value, box.Minimum, box.Maximum);
    }

    private class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
