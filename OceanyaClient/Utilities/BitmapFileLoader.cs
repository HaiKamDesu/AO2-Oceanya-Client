using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace OceanyaClient.Utilities
{
    internal static class BitmapFileLoader
    {
        public static BitmapImage LoadFrozen(string path)
        {
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }

        public static BitmapImage LoadFrozenFromBytes(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using MemoryStream stream = new MemoryStream(bytes);
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
