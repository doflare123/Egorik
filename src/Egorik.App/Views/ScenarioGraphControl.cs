using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Egorik.App.ViewModels;
using Egorik.Core.Scenarios;

namespace Egorik.App.Views;

// Scratch-style presentation. Link editing is delegated to ScenarioStructure in Core.
public sealed class ScenarioGraphControl : Control
{
    public static readonly StyledProperty<ScenarioEditorViewModel?> EditorProperty =
        AvaloniaProperty.Register<ScenarioGraphControl, ScenarioEditorViewModel?>(nameof(Editor));
    private const double PaletteWidth = 154;
    private const double BlockWidth = 320;
    private const double Indent = 28;
    private readonly List<BlockBox> _boxes = [];
    private readonly List<DropSlot> _slots = [];
    private readonly List<INotifyPropertyChanged> _observed = [];
    private INotifyCollectionChanged? _collection;
    private ScenarioEditorViewModel? _observedEditor;
    private ScenarioProgram? _program;
    private string? _error;
    private Guid _startId;
    private double _zoom = 1;
    private bool _viewCustomized;
    private Vector _pan = new(PaletteWidth + 48, 36);
    private Point _lastPointer;
    private Point _pressedAt;
    private Guid? _movingId;
    private ScenarioNodeKind? _newKind;
    private bool _dragging;
    private bool _panning;
    private DropSlot? _hoverSlot;
    private Size _contentSize;
    private static readonly ScenarioNodeKind[] PaletteKinds = [ScenarioNodeKind.Action, ScenarioNodeKind.Delay, ScenarioNodeKind.Condition, ScenarioNodeKind.LegacyCommand];

    public ScenarioGraphControl() { Focusable = true; ClipToBounds = true; }
    public ScenarioEditorViewModel? Editor { get => GetValue(EditorProperty); set => SetValue(EditorProperty, value); }
    public string ZoomText => $"{_zoom * 100:0}%";

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorProperty) { Observe(); }
        if (change.Property == BoundsProperty && !_viewCustomized) { FitCore(false); }
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs) { base.OnAttachedToVisualTree(eventArgs); Observe(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArgs) { Unsubscribe(); base.OnDetachedFromVisualTree(eventArgs); }
    private void Unsubscribe()
    {
        if (_collection is not null) { _collection.CollectionChanged -= OnCollectionChanged; }
        if (_observedEditor is not null) { _observedEditor.PropertyChanged -= OnEditorChanged; }
        foreach (var item in _observed) { item.PropertyChanged -= OnBlockChanged; }
        _observed.Clear(); _collection = null; _observedEditor = null;
    }
    private void Observe()
    {
        Unsubscribe();
        if (Editor is not null)
        {
            _observedEditor = Editor;
            Editor.PropertyChanged += OnEditorChanged;
            _collection = Editor.Blocks;
            _collection.CollectionChanged += OnCollectionChanged;
            foreach (var block in Editor.Blocks)
            {
                _observed.Add(block); block.PropertyChanged += OnBlockChanged;
                foreach (var argument in block.Arguments) { _observed.Add(argument); argument.PropertyChanged += OnBlockChanged; }
            }
        }
        LayoutProgram();
    }
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs) => Observe();
    private void OnEditorChanged(object? sender, PropertyChangedEventArgs eventArgs) => InvalidateVisual();
    private void OnBlockChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(ScenarioBlockViewModel.SelectedAction)) { Observe(); }
        else { LayoutProgram(); }
    }
    private void LayoutProgram()
    {
        _boxes.Clear(); _slots.Clear(); _program = null;
        if (Editor is null) { InvalidateVisual(); return; }
        if (!ScenarioStructure.TryRead(Editor.BuildFlow(), out _program, out _error)) { InvalidateVisual(); return; }
        if (_program!.Start.Id != _startId) { _startId = _program.Start.Id; _viewCustomized = false; _zoom = 1; _pan = new(PaletteWidth + 48, 36); }
        var width = StackWidth(_program.Body);
        _boxes.Add(new(_program.Start, new(0, 0, width, 48), new(0, 0, width, 48)));
        var height = PlaceStack(_program.Body, 0, 52, null, false);
        _boxes.Add(new(_program.End, new(0, 56 + height, width, 38), new(0, 56 + height, width, 38)));
        _contentSize = new(width, height + 100);
        if (!_viewCustomized) { FitCore(false); }
        InvalidateVisual();
    }
    private static double StackWidth(IReadOnlyList<ScenarioStackBlock> stack) => Math.Max(BlockWidth,
        stack.Select(block => block.Node.Kind == ScenarioNodeKind.Condition ? Indent + Math.Max(StackWidth(block.Then), StackWidth(block.Else)) : BlockWidth).DefaultIfEmpty(BlockWidth).Max());
    private static double StackHeight(IReadOnlyList<ScenarioStackBlock> stack) => stack.Count == 0 ? 42 : stack.Sum(block =>
        block.Node.Kind == ScenarioNodeKind.Condition ? 48 + StackHeight(block.Then) + 28 + StackHeight(block.Else) + 18 + 4 : 52);
    private double PlaceStack(IReadOnlyList<ScenarioStackBlock> stack, double x, double y, Guid? owner, bool otherwise)
    {
        var initial = y;
        var width = StackWidth(stack);
        _slots.Add(new(owner, otherwise, 0, new(x, y - 10, width, stack.Count == 0 ? 42 : 20)));
        for (var index = 0; index < stack.Count; index++)
        {
            var block = stack[index];
            if (block.Node.Kind == ScenarioNodeKind.Condition)
            {
                var yesHeight = StackHeight(block.Then);
                var noHeight = StackHeight(block.Else);
                var height = 48 + yesHeight + 28 + noHeight + 18;
                _boxes.Add(new(block.Node, new(x, y, width, 48), new(x, y, width, height)));
                PlaceStack(block.Then, x + Indent, y + 48, block.Node.Id, false);
                PlaceStack(block.Else, x + Indent, y + 48 + yesHeight + 28, block.Node.Id, true);
                y += height + 4;
            }
            else
            {
                _boxes.Add(new(block.Node, new(x, y, BlockWidth, 48), new(x, y, BlockWidth, 48)));
                y += 52;
            }
            _slots.Add(new(owner, otherwise, index + 1, new(x, y - 10, width, 20)));
        }
        return stack.Count == 0 ? 42 : y - initial;
    }
    public void ZoomBy(double factor) => ZoomAt(factor, new(PaletteWidth + (Bounds.Width - PaletteWidth) / 2, Bounds.Height / 2));
    private void ZoomAt(double factor, Point anchor)
    {
        _viewCustomized = true;
        var zoom = Math.Clamp(_zoom * factor, 0.05, 1.6);
        var ratio = zoom / _zoom;
        _pan = new(anchor.X - (anchor.X - _pan.X) * ratio, anchor.Y - (anchor.Y - _pan.Y) * ratio);
        _zoom = zoom; InvalidateVisual();
    }
    public void Fit()
    {
        _viewCustomized = true;
        FitCore(true);
    }
    private void FitCore(bool overview)
    {
        if (Bounds.Width <= PaletteWidth || Bounds.Height <= 0 || _contentSize.Width <= 0) { return; }
        _zoom = Math.Clamp(Math.Min((Math.Max(200, Bounds.Width - PaletteWidth - 64)) / Math.Max(1, _contentSize.Width),
            Math.Max(80, Bounds.Height - 48) / Math.Max(1, _contentSize.Height)), overview ? 0.05 : 0.65, 1);
        _pan = new(PaletteWidth + 32, 24); InvalidateVisual();
    }
    public void ResetView() { _viewCustomized = true; _zoom = 1; _pan = new(PaletteWidth + 48, 36); InvalidateVisual(); }
    private Point World(Point point) => new((point.X - _pan.X) / _zoom, (point.Y - _pan.Y) / _zoom);
    private static string Color(ScenarioNodeKind kind) => kind switch
    {
        ScenarioNodeKind.Start or ScenarioNodeKind.End => "#D89B22",
        ScenarioNodeKind.Delay => "#A16AD3",
        ScenarioNodeKind.Condition => "#D98629",
        ScenarioNodeKind.LegacyCommand => "#4B9D82",
        _ => "#487BD5"
    };
    private static string KindTitle(ScenarioNodeKind kind) => kind switch
    {
        ScenarioNodeKind.Action => "Действие",
        ScenarioNodeKind.Delay => "Подождать",
        ScenarioNodeKind.Condition => "Если / иначе",
        _ => "Текстовая команда"
    };
    private static Rect PaletteRect(int index) => new(14, 68 + index * 60, PaletteWidth - 28, 44);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brush.Parse("#11151F"), null, new Rect(0, 0, Bounds.Width, Bounds.Height));
        using (context.PushClip(new Rect(PaletteWidth, 0, Math.Max(0, Bounds.Width - PaletteWidth), Bounds.Height)))
        {
            var step = 24 * _zoom;
            for (var x = PaletteWidth + ((_pan.X - PaletteWidth) % step + step) % step; x < Bounds.Width; x += step)
            {
                for (var y = (_pan.Y % step + step) % step; y < Bounds.Height; y += step)
                { context.DrawEllipse(Brush.Parse("#2A3040"), null, new Point(x, y), 1, 1); }
            }
            if (_program is null)
            {
                Text(context, _error ?? "Откройте сценарий", new(PaletteWidth + 30, 36), 14, "#DDC8A5", Math.Max(100, Bounds.Width - PaletteWidth - 60), 150);
            }
            else
            {
                using (context.PushTransform(Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_pan)))
                {
                    foreach (var box in _boxes) { DrawBlock(context, box); }
                    if (_dragging && _hoverSlot is not null)
                    {
                        var rect = _hoverSlot.Bounds;
                        context.DrawRectangle(Brush.Parse("#A9F0CA"), null, new Rect(rect.X, rect.Center.Y - 3, rect.Width, 6), 3, 3);
                    }
                }
            }
        }
        context.DrawRectangle(Brush.Parse("#191E2B"), new Pen(Brush.Parse("#2D3446")), new Rect(0, 0, PaletteWidth, Bounds.Height));
        Text(context, "БЛОКИ", new(16, 18), 11, "#B9C3DC", 120, 20);
        Text(context, "Перетащи в сценарий", new(16, 39), 10, "#8693AE", 125, 20);
        for (var index = 0; index < PaletteKinds.Length; index++)
        {
            var kind = PaletteKinds[index];
            Puzzle(context, PaletteRect(index), Brush.Parse(Color(kind)), null, false, false);
            Text(context, KindTitle(kind), PaletteRect(index).TopLeft + new Vector(10, 17), 11, "#FFFFFF", 110, 22);
        }
        if (Bounds.Height >= 480)
        {
            Text(context, "Колесо — прокрутка\nCtrl + колесо — масштаб\nФон — перетаскивание\nДвойной щелчок — поля", new(14, Bounds.Height - 112), 10, "#8693AE", 130, 100);
        }
        Text(context, ZoomText, new(Bounds.Width - 66, Bounds.Height - 28), 11, "#9AA9C2", 60, 20);
        if (_dragging)
        {
            var kind = _newKind ?? _boxes.FirstOrDefault(box => box.Node.Id == _movingId)?.Node.Kind ?? ScenarioNodeKind.Action;
            var ghost = new Rect(_lastPointer.X + 16, _lastPointer.Y + 12, 190, 42);
            Puzzle(context, ghost, Brush.Parse(Color(kind)), new Pen(Brushes.White), false, false);
            Text(context, KindTitle(kind), ghost.TopLeft + new Vector(12, 13), 12, "#FFFFFF", 165, 24);
        }
    }
    private void DrawBlock(DrawingContext context, BlockBox box)
    {
        var node = box.Node;
        var block = Editor?.Blocks.FirstOrDefault(block => block.Id == node.Id);
        var stroke = block?.IsExecuting == true ? new Pen(Brush.Parse("#9BF6BA"), 3) : Editor?.SelectedBlock?.Id == node.Id ? new Pen(Brush.Parse("#EDF3FF"), 2) : null;
        var brush = Brush.Parse(Color(node.Kind));
        if (node.Kind == ScenarioNodeKind.Condition)
        {
            context.DrawRectangle(brush, stroke, box.Full, 6, 6);
            var statement = ScenarioStructure.Flatten(_program!.Body).First(item => item.Node.Id == node.Id);
            var yesHeight = StackHeight(statement.Then);
            context.DrawRectangle(Brush.Parse("#11151F"), null, new(box.Full.X + Indent, box.Full.Y + 48, box.Full.Width - Indent, yesHeight));
            context.DrawRectangle(Brush.Parse("#11151F"), null, new(box.Full.X + Indent, box.Full.Y + 48 + yesHeight + 28, box.Full.Width - Indent, StackHeight(statement.Else)));
            Text(context, "иначе", new(box.Full.X + 14, box.Full.Y + 48 + yesHeight + 5), 12, "#FFFFFF", 150, 22);
            if (statement.Then.Count == 0) { EmptyHint(context, box.Full.X + Indent + 14, box.Full.Y + 60); }
            if (statement.Else.Count == 0) { EmptyHint(context, box.Full.X + Indent + 14, box.Full.Y + 48 + yesHeight + 40); }
        }
        Puzzle(context, box.Header, brush, stroke, node.Kind == ScenarioNodeKind.Start, node.Kind == ScenarioNodeKind.End);
        var title = node.Kind switch
        {
            ScenarioNodeKind.Start => "когда сказано: «" + Short(Editor?.Trigger ?? "фраза запуска", 24) + "»",
            ScenarioNodeKind.End => "Готово",
            ScenarioNodeKind.Condition => "если " + (node.Condition == ScenarioConditionKind.AiEnabled ? "ИИ включён" : string.IsNullOrWhiteSpace(node.Value) ? "[выбери условие]" : $"«{Short(node.Value, 22)}» есть в настройках") + " то",
            ScenarioNodeKind.Delay => $"подождать  {node.DelaySeconds} сек.",
            ScenarioNodeKind.LegacyCommand => "команда  «" + Short(node.Value, 30) + "»",
            _ => block?.SelectedAction?.Title ?? "[выбери действие]"
        };
        var hasFields = node.Kind == ScenarioNodeKind.Action && block?.Arguments.Any(argument => argument.Value.Length > 0) == true;
        Text(context, title, box.Header.TopLeft + new Vector(14, hasFields ? 6 : 15), 12, "#FFFFFF", box.Header.Width - 56, 22);
        if (hasFields)
        {
            var values = string.Join(" · ", block!.Arguments.Where(argument => argument.Value.Length > 0).Select(argument => argument.SelectedChoice?.Title ?? argument.Value));
            Text(context, Short(values, 45), box.Header.TopLeft + new Vector(14, 27), 11, "#E0ECFF", box.Header.Width - 56, 18);
        }
        if (node.Kind is not (ScenarioNodeKind.Start or ScenarioNodeKind.End))
        {
            context.DrawRectangle(Brush.Parse("#26000000"), null, EditRect(box), 5, 5);
            Text(context, "···", EditRect(box).TopLeft + new Vector(6, 0), 18, "#FFFFFF", 25, 24);
        }
    }
    private static void EmptyHint(DrawingContext context, double x, double y) => Text(context, "перетащи блок сюда", new(x, y), 11, "#76859F", 240, 22);
    private static Rect EditRect(BlockBox box) => new(box.Header.Right - 38, box.Header.Y + 11, 28, 26);
    private static string Short(string value, int max) => value.Length > max ? value[..(max - 1)] + "…" : value;
    private static void Puzzle(DrawingContext context, Rect rect, IBrush fill, IPen? stroke, bool hat, bool cap)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new(rect.X, rect.Y + (hat ? 12 : 0)), true);
            if (hat) { path.CubicBezierTo(new(rect.X + 20, rect.Y - 10), new(rect.X + 75, rect.Y - 10), new(rect.X + 100, rect.Y)); }
            else { path.LineTo(new(rect.X + 22, rect.Y)); path.LineTo(new(rect.X + 28, rect.Y + 6)); path.LineTo(new(rect.X + 46, rect.Y + 6)); path.LineTo(new(rect.X + 52, rect.Y)); }
            path.LineTo(rect.TopRight); path.LineTo(rect.BottomRight);
            if (!cap) { path.LineTo(new(rect.X + 52, rect.Bottom)); path.LineTo(new(rect.X + 46, rect.Bottom + 6)); path.LineTo(new(rect.X + 28, rect.Bottom + 6)); path.LineTo(new(rect.X + 22, rect.Bottom)); }
            path.LineTo(rect.BottomLeft); path.EndFigure(true);
        }
        context.DrawGeometry(fill, stroke, geometry);
    }
    private static void Text(DrawingContext context, string value, Point point, double size, string color, double width, double height)
    {
        context.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Brush.Parse(color))
        { MaxTextWidth = Math.Max(1, width), MaxTextHeight = height }, point);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs eventArgs)
    {
        base.OnPointerPressed(eventArgs);
        if (Editor?.IsRunning == true) { return; }
        Focus(); _lastPointer = _pressedAt = eventArgs.GetPosition(this);
        var properties = eventArgs.GetCurrentPoint(this).Properties;
        if (properties.IsMiddleButtonPressed) { _panning = true; eventArgs.Pointer.Capture(this); return; }
        if (!properties.IsLeftButtonPressed) { return; }
        for (var index = 0; index < PaletteKinds.Length; index++)
        {
            if (PaletteRect(index).Contains(_lastPointer)) { _newKind = PaletteKinds[index]; _dragging = true; eventArgs.Pointer.Capture(this); InvalidateVisual(); return; }
        }
        if (_lastPointer.X < PaletteWidth) { return; }
        var world = World(_lastPointer);
        var hit = _boxes.LastOrDefault(box => box.Header.Contains(world));
        if (hit is not null)
        {
            if (Editor is not null) { Editor.SelectedBlock = Editor.Blocks.FirstOrDefault(block => block.Id == hit.Node.Id); }
            if (eventArgs.ClickCount == 2 || EditRect(hit).Contains(world)) { Editor?.OpenBlockEditor(); return; }
            if (hit.Node.Kind is not (ScenarioNodeKind.Start or ScenarioNodeKind.End)) { _movingId = hit.Node.Id; eventArgs.Pointer.Capture(this); }
            else { _panning = true; eventArgs.Pointer.Capture(this); }
        }
        else { _panning = true; eventArgs.Pointer.Capture(this); }
        InvalidateVisual();
    }
    protected override void OnPointerMoved(PointerEventArgs eventArgs)
    {
        base.OnPointerMoved(eventArgs);
        var point = eventArgs.GetPosition(this);
        if (_panning) { _viewCustomized = true; _pan += point - _lastPointer; }
        if (_movingId is not null && Math.Abs(point.X - _pressedAt.X) + Math.Abs(point.Y - _pressedAt.Y) > 5) { _dragging = true; }
        _lastPointer = point;
        if (_dragging)
        {
            var world = World(point);
            _hoverSlot = point.X < PaletteWidth ? null : _slots.Where(slot => world.X >= slot.Bounds.X - 12 && world.X <= slot.Bounds.Right + 24 && Math.Abs(world.Y - slot.Bounds.Center.Y) < 32)
                .OrderBy(slot => Math.Abs(world.Y - slot.Bounds.Center.Y)).ThenByDescending(slot => slot.Bounds.X).FirstOrDefault();
        }
        InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs eventArgs)
    {
        base.OnPointerReleased(eventArgs);
        if (_dragging && Editor?.IsRunning == false)
        {
            if (_hoverSlot is not null) { Editor.DropBlock(_movingId, _newKind, _hoverSlot.Owner, _hoverSlot.Otherwise, _hoverSlot.Index); }
            else if (_newKind is not null && eventArgs.GetPosition(this).X < PaletteWidth)
            {
                var command = _newKind switch { ScenarioNodeKind.Action => Editor.AddActionCommand, ScenarioNodeKind.Delay => Editor.AddDelayCommand, ScenarioNodeKind.Condition => Editor.AddConditionCommand, _ => Editor.AddTextCommand };
                command.Execute(null);
            }
        }
        ResetDrag(); eventArgs.Pointer.Capture(null);
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs eventArgs) { base.OnPointerCaptureLost(eventArgs); ResetDrag(); }
    private void ResetDrag() { _movingId = null; _newKind = null; _dragging = false; _panning = false; _hoverSlot = null; InvalidateVisual(); }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs eventArgs)
    {
        base.OnPointerWheelChanged(eventArgs);
        if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control)) { ZoomAt(Math.Pow(1.12, eventArgs.Delta.Y), eventArgs.GetPosition(this)); }
        else { _viewCustomized = true; _pan += new Vector(eventArgs.Delta.X * 44, eventArgs.Delta.Y * 44); InvalidateVisual(); }
        eventArgs.Handled = true;
    }
    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        base.OnKeyDown(eventArgs);
        if (Editor?.IsRunning == true) { return; }
        if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control) && eventArgs.Key == Key.Z) { Editor?.UndoCommand.Execute(null); eventArgs.Handled = true; }
        else if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control) && eventArgs.Key == Key.Y) { Editor?.RedoCommand.Execute(null); eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.Delete) { Editor?.RemoveBlockCommand.Execute(null); eventArgs.Handled = true; }
        else if (eventArgs.Key == Key.Enter) { Editor?.OpenBlockEditor(); eventArgs.Handled = true; }
    }
    private sealed record BlockBox(ScenarioNode Node, Rect Header, Rect Full);
    private sealed record DropSlot(Guid? Owner, bool Otherwise, int Index, Rect Bounds);
}
