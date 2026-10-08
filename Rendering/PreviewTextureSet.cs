using Avalonia.Media.Imaging;

namespace TextureTinter.Rendering;

public sealed class PreviewTextureSet
{
    public Bitmap? All { get; set; }
    public Bitmap? Top { get; set; }
    public Bitmap? Bottom { get; set; }
    public Bitmap? Side { get; set; }
    public Bitmap? Front { get; set; }
    public Bitmap? Back { get; set; }
    public Bitmap? Left { get; set; }
    public Bitmap? Right { get; set; }
    public Bitmap? Cross { get; set; }
    public Bitmap? Flat { get; set; }

    public Bitmap? PickTop()    => Top    ?? All ?? Side ?? Front ?? Bottom ?? Left ?? Right ?? Back;
    public Bitmap? PickBottom() => Bottom ?? All ?? Top  ?? Side  ?? Front  ?? Left ?? Right ?? Back;
    public Bitmap? PickFront()  => Front  ?? Side ?? All ?? Top   ?? Bottom ?? Left ?? Right ?? Back;
    public Bitmap? PickBack()   => Back   ?? Side ?? All ?? Top   ?? Bottom ?? Left ?? Right ?? Front;
    public Bitmap? PickLeft()   => Left   ?? Side ?? All ?? Top   ?? Bottom ?? Front ?? Right ?? Back;
    public Bitmap? PickRight()  => Right  ?? Side ?? All ?? Top   ?? Bottom ?? Front ?? Left ?? Back;
    public Bitmap? PickCross()  => Cross;
    public Bitmap? PickFlat()   => Flat ?? All;
}