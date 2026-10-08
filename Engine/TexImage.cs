using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace TextureTinter.Engine;

public sealed class TexImage
{
    public readonly int Width, Height;
    public readonly float[] Data;

    public TexImage(int w, int h)
    {
        Width = w; Height = h;
        Data = new float[w * h * 4];
    }

    public int PixelCount => Width * Height;

    public static TexImage FromBitmap(Bitmap bmp)
    {
        int w = bmp.PixelSize.Width;
        int h = bmp.PixelSize.Height;

        // Minecraft animated textures are stored as a vertical strip:
        //   width × (width × frameCount)
        // Crop to the first frame so previews, tiles and exports all show a single tile.
        if (h > w && h % w == 0) h = w;

        var img = new TexImage(w, h);

        var bytes = new byte[w * h * 4];
        unsafe
        {
            fixed (byte* p = bytes)
            {
                bmp.CopyPixels(new PixelRect(0, 0, w, h), (IntPtr)p, bytes.Length, w * 4);
            }
        }

        Parallel.For(0, h, y =>
        {
            int rowOff = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int i = rowOff + x * 4;
                img.Data[i + 0] = bytes[i + 2]; // R
                img.Data[i + 1] = bytes[i + 1]; // G
                img.Data[i + 2] = bytes[i + 0]; // B
                img.Data[i + 3] = bytes[i + 3]; // A
            }
        });
        return img;
    }

    public Bitmap ToBitmap()
    {
        int w = Width, h = Height;
        var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
                                     PixelFormat.Bgra8888, AlphaFormat.Premul);

        var bytes = new byte[w * h * 4];
        Parallel.For(0, h, y =>
        {
            int rowOff = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int i = rowOff + x * 4;
                bytes[i + 0] = Clamp(Data[i + 2]);
                bytes[i + 1] = Clamp(Data[i + 1]);
                bytes[i + 2] = Clamp(Data[i + 0]);
                bytes[i + 3] = Clamp(Data[i + 3]);
            }
        });

        using (var fb = wb.Lock())
        {
            unsafe
            {
                fixed (byte* src = bytes)
                {
                    byte* dst = (byte*)fb.Address;
                    int rowBytes = fb.RowBytes;
                    for (int y = 0; y < h; y++)
                        Buffer.MemoryCopy(src + y * w * 4, dst + y * rowBytes, rowBytes, w * 4);
                }
            }
        }
        return wb;

        static byte Clamp(float v) =>
            v < 0 ? (byte)0 : v > 255 ? (byte)255 : (byte)(v + 0.5f);
    }

    public TexImage Clone()
    {
        var c = new TexImage(Width, Height);
        Array.Copy(Data, c.Data, Data.Length);
        return c;
    }
}