using System.ComponentModel;
using System.Collections.Specialized;
using FaceLab.Maui.ViewModels;
using Microsoft.Maui.Graphics.Platform;
using GraphicsImage = Microsoft.Maui.Graphics.IImage;

namespace FaceLab.Maui.Controls;

public sealed class FaceOverlayView : Grid
{
    private readonly Image _image = new() { Aspect = Aspect.AspectFit };
    private readonly GraphicsView _graphicsView = new();
    private readonly FaceOverlayDrawable _drawable = new();

    public static readonly BindableProperty ImageBytesProperty = BindableProperty.Create(
        nameof(ImageBytes),
        typeof(byte[]),
        typeof(FaceOverlayView),
        propertyChanged: static (bindable, _, newValue) =>
            ((FaceOverlayView)bindable).SetImageBytes((byte[]?)newValue));

    public static readonly BindableProperty FacesProperty = BindableProperty.Create(
        nameof(Faces),
        typeof(IReadOnlyList<FaceOverlayItem>),
        typeof(FaceOverlayView),
        propertyChanged: static (bindable, oldValue, newValue) =>
            ((FaceOverlayView)bindable).SetFaces(
                (IReadOnlyList<FaceOverlayItem>?)oldValue,
                (IReadOnlyList<FaceOverlayItem>?)newValue));

    public byte[]? ImageBytes
    {
        get => (byte[]?)GetValue(ImageBytesProperty);
        set => SetValue(ImageBytesProperty, value);
    }

    public IReadOnlyList<FaceOverlayItem>? Faces
    {
        get => (IReadOnlyList<FaceOverlayItem>?)GetValue(FacesProperty);
        set => SetValue(FacesProperty, value);
    }

    public FaceOverlayView()
    {
        _graphicsView.Drawable = _drawable;
        _graphicsView.InputTransparent = true;
        Children.Add(_image);
        Children.Add(_graphicsView);
        SizeChanged += (_, _) => _graphicsView.Invalidate();
    }

    private void SetImageBytes(byte[]? bytes)
    {
        _drawable.Image = null;
        _image.Source = bytes is null
            ? null
            : ImageSource.FromStream(() => new MemoryStream(bytes));

        if (bytes is not null)
        {
            using var stream = new MemoryStream(bytes);
            _drawable.Image = PlatformImage.FromStream(stream);
        }

        _graphicsView.Invalidate();
    }

    private void SetFaces(
        IReadOnlyList<FaceOverlayItem>? oldFaces,
        IReadOnlyList<FaceOverlayItem>? newFaces)
    {
        if (oldFaces is not null)
        {
            if (oldFaces is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= FacesCollectionChanged;
            }

            foreach (var face in oldFaces)
            {
                face.PropertyChanged -= FacePropertyChanged;
            }
        }

        if (newFaces is not null)
        {
            if (newFaces is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += FacesCollectionChanged;
            }

            foreach (var face in newFaces)
            {
                face.PropertyChanged += FacePropertyChanged;
            }
        }

        _drawable.Faces = newFaces;
        _graphicsView.Invalidate();
    }

    private void FacePropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        _graphicsView.Invalidate();

    private void FacesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        _graphicsView.Invalidate();
}

public sealed class FaceOverlayDrawable : IDrawable
{
    private static readonly Color[] Palette =
    [
        Colors.Blue,
        Colors.Red,
        Colors.Green,
        Colors.Orange,
        Colors.Purple,
        Colors.Cyan
    ];

    public GraphicsImage? Image { get; set; }

    public IReadOnlyList<FaceOverlayItem>? Faces { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Image is null || Image.Width <= 0 || Image.Height <= 0 || Faces is null)
        {
            return;
        }

        var scale = Math.Min(dirtyRect.Width / Image.Width, dirtyRect.Height / Image.Height);
        var offsetX = (dirtyRect.Width - Image.Width * scale) / 2;
        var offsetY = (dirtyRect.Height - Image.Height * scale) / 2;

        canvas.StrokeSize = 3;
        canvas.FillColor = Colors.White;

        for (var index = 0; index < Faces.Count; index++)
        {
            var face = Faces[index];
            var color = Palette[index % Palette.Length];

            if (face.ShowRectangle)
            {
                canvas.StrokeColor = color;
                canvas.DrawRectangle(
                    offsetX + face.Record.Left * scale,
                    offsetY + face.Record.Top * scale,
                    face.Record.Width * scale,
                    face.Record.Height * scale);
            }

            if (face.ShowLandmarks && face.Landmarks is not null)
            {
                canvas.FillColor = color;
                foreach (var point in face.Landmarks)
                {
                    canvas.FillCircle(
                        (float)(offsetX + point.X * scale),
                        (float)(offsetY + point.Y * scale),
                        3);
                }
            }
        }
    }
}
