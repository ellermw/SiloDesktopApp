using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ContinuumPlayer.Controls;

public sealed partial class BackdropImage : UserControl
{
    private double _imgW;
    private double _imgH;

    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(
            nameof(Source),
            typeof(ImageSource),
            typeof(BackdropImage),
            new PropertyMetadata(null, OnSourceChanged));

    /// <summary>
    /// Vertical anchor point (0.0 = top, 1.0 = bottom). Default 0.2 matches
    /// CSS object-position: center 20%.
    /// </summary>
    public static readonly DependencyProperty AnchorYProperty =
        DependencyProperty.Register(
            nameof(AnchorY),
            typeof(double),
            typeof(BackdropImage),
            new PropertyMetadata(0.2, OnAnchorYChanged));

    public ImageSource Source
    {
        get => (ImageSource)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public double AnchorY
    {
        get => (double)GetValue(AnchorYProperty);
        set => SetValue(AnchorYProperty, value);
    }

    public BackdropImage()
    {
        this.InitializeComponent();
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not BackdropImage ctrl) return;

        ctrl._imgW = 0;
        ctrl._imgH = 0;
        ctrl.InnerImage.Source = e.NewValue as ImageSource;

        if (e.NewValue is BitmapImage bmp)
        {
            // BitmapImage may not have pixel dimensions until ImageOpened fires
            bmp.ImageOpened += (_, _) =>
            {
                ctrl._imgW = bmp.PixelWidth;
                ctrl._imgH = bmp.PixelHeight;
                ctrl.Reposition();
            };

            // If already loaded (PixelWidth > 0), use immediately
            if (bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
            {
                ctrl._imgW = bmp.PixelWidth;
                ctrl._imgH = bmp.PixelHeight;
                ctrl.Reposition();
            }
        }
        else if (e.NewValue is WriteableBitmap wb)
        {
            // WriteableBitmap has pixel dimensions immediately
            ctrl._imgW = wb.PixelWidth;
            ctrl._imgH = wb.PixelHeight;
            ctrl.Reposition();
        }
    }

    private static void OnAnchorYChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BackdropImage ctrl)
            ctrl.Reposition();
    }

    private void ClipCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Canvas does NOT clip children by default in WinUI 3.
        // Apply a RectangleGeometry clip to enforce bounds.
        ClipCanvas.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height)
        };
        Reposition();
    }

    private void Reposition()
    {
        double containerW = ClipCanvas.ActualWidth;
        double containerH = ClipCanvas.ActualHeight;

        if (containerW <= 0 || containerH <= 0 || _imgW <= 0 || _imgH <= 0)
            return;

        // "cover" scale: fill container while preserving aspect ratio
        double scale = Math.Max(containerW / _imgW, containerH / _imgH);
        double scaledW = _imgW * scale;
        double scaledH = _imgH * scale;

        // Center horizontally
        double left = (containerW - scaledW) / 2;

        // Vertical anchor: position so anchorY% of the image aligns with anchorY% of container
        double anchorY = AnchorY;
        double top = containerH * anchorY - scaledH * anchorY;
        // Clamp so we never show gaps at top or bottom
        top = Math.Clamp(top, containerH - scaledH, 0);

        InnerImage.Width = scaledW;
        InnerImage.Height = scaledH;
        Canvas.SetLeft(InnerImage, left);
        Canvas.SetTop(InnerImage, top);
    }
}
