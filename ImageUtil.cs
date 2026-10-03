using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace PromptGenerator
{
    /// <summary>
    /// 上传图片的载荷：原始字节（BMP 已被转码为 PNG）与对应 MIME 类型。
    /// </summary>
    public class ImagePayload
    {
        private readonly byte[] _data;
        private readonly string _mimeType;

        public ImagePayload(byte[] data, string mimeType)
        {
            _data = data == null ? new byte[0] : data;
            _mimeType = mimeType == null ? string.Empty : mimeType;
        }

        /// <summary>图片字节。</summary>
        public byte[] Data
        {
            get { return _data; }
        }

        /// <summary>MIME 类型，例如 image/png。</summary>
        public string MimeType
        {
            get { return _mimeType; }
        }

        /// <summary>字节长度。</summary>
        public int Length
        {
            get { return _data.Length; }
        }

        /// <summary>组装为 OpenAI 兼容的 base64 data URL。</summary>
        public string ToDataUrl()
        {
            return "data:" + _mimeType + ";base64," + Convert.ToBase64String(_data);
        }
    }

    /// <summary>
    /// 界面预览用的图像。图像由内存流解码得到，必须与宿主流同生共死，
    /// 本类负责在释放时一并释放，避免长期持有文件句柄。
    /// </summary>
    public class ImagePreview : IDisposable
    {
        private Image _image;
        private MemoryStream _stream;

        internal ImagePreview(Image image, MemoryStream stream)
        {
            _image = image;
            _stream = stream;
        }

        /// <summary>解码得到的图像；释放后为 null。</summary>
        public Image Image
        {
            get { return _image; }
        }

        public void Dispose()
        {
            if (_image != null)
            {
                _image.Dispose();
                _image = null;
            }
            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }
        }
    }

    /// <summary>
    /// 图片读取、格式判定、BMP 转 PNG、大小校验、缩略图生成与删除。
    /// 一律先读入内存再解码，不使用 Image.FromFile（避免长期占用文件句柄）。
    /// </summary>
    internal static class ImageUtil
    {
        /// <summary>不支持的图片格式提示。</summary>
        public const string UnsupportedFormatMessage = "不支持的图片格式，仅支持 JPG / JPEG / PNG / WebP / BMP。";

        /// <summary>图片过大提示。</summary>
        public const string TooLargeMessage = "图片过大（单张上限 32MiB），请压缩后再试。";

        /// <summary>文件选择对话框的过滤器。</summary>
        public const string OpenFileFilter = "图片文件|*.jpg;*.jpeg;*.png;*.webp;*.bmp|所有文件|*.*";

        /// <summary>扩展名是否属于支持集（大小写不敏感，入参需含点）。</summary>
        public static bool IsSupportedExtensionValue(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return false;
            }
            string e = extension.ToLowerInvariant();
            for (int i = 0; i < Defaults.SupportedImageExtensions.Length; i++)
            {
                if (Defaults.SupportedImageExtensions[i] == e)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>文件路径的扩展名是否属于支持集。</summary>
        public static bool IsSupportedExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            string extension;
            try
            {
                extension = Path.GetExtension(path);
            }
            catch (Exception)
            {
                return false;
            }
            return IsSupportedExtensionValue(extension);
        }

        /// <summary>
        /// 按上传流程读取图片：
        /// 扩展名过滤 → 32 MiB 大小校验 → 读入内存 → BMP 转码为 PNG → 给出 MIME。
        /// 任何失败都通过 error 返回可直接展示给用户的文案。
        /// </summary>
        public static bool LoadForUpload(string path, out ImagePayload payload, out string error)
        {
            payload = null;
            error = string.Empty;

            if (string.IsNullOrEmpty(path))
            {
                error = "请先选择图片文件。";
                return false;
            }

            string extension;
            try
            {
                extension = Path.GetExtension(path);
            }
            catch (Exception)
            {
                error = UnsupportedFormatMessage;
                return false;
            }

            if (!IsSupportedExtensionValue(extension))
            {
                error = UnsupportedFormatMessage;
                return false;
            }

            long length;
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists)
                {
                    error = "图片无法读取：文件不存在。";
                    return false;
                }
                length = info.Length;
            }
            catch (Exception ex)
            {
                error = "图片无法读取：" + ex.Message;
                return false;
            }

            if (length > Defaults.MaxImageBytes)
            {
                error = TooLargeMessage;
                return false;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                error = "图片无法读取：" + ex.Message;
                return false;
            }

            if (bytes.Length == 0)
            {
                error = "图片无法读取：文件内容为空。";
                return false;
            }

            string ext = extension.ToLowerInvariant();
            if (ext == ".bmp")
            {
                // DeepSeek 不支持 BMP，本地转码为 PNG 后上传
                byte[] png;
                string convertError;
                if (!TryConvertBmpToPng(bytes, out png, out convertError))
                {
                    error = convertError;
                    return false;
                }
                payload = new ImagePayload(png, "image/png");
                return true;
            }

            if (ext == ".jpg" || ext == ".jpeg")
            {
                payload = new ImagePayload(bytes, "image/jpeg");
            }
            else if (ext == ".webp")
            {
                payload = new ImagePayload(bytes, "image/webp");
            }
            else
            {
                payload = new ImagePayload(bytes, "image/png");
            }
            return true;
        }

        /// <summary>BMP 字节 → PNG 字节（仅用 System.Drawing，无第三方依赖）。</summary>
        public static bool TryConvertBmpToPng(byte[] bmpBytes, out byte[] pngBytes, out string error)
        {
            pngBytes = null;
            error = string.Empty;
            if (bmpBytes == null || bmpBytes.Length == 0)
            {
                error = "图片无法读取：文件内容为空。";
                return false;
            }

            try
            {
                using (MemoryStream input = new MemoryStream(bmpBytes, false))
                using (Image image = Image.FromStream(input))
                using (MemoryStream output = new MemoryStream())
                {
                    image.Save(output, ImageFormat.Png);
                    pngBytes = output.ToArray();
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "图片无法读取：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 从内存字节建立界面预览。WebP 在 .NET Framework 的 GDI+ 下无法解码，
        /// 此时返回 false（上传与生成不受影响，仅界面不显示缩略图）。
        /// </summary>
        public static bool TryCreatePreview(ImagePayload payload, out ImagePreview preview, out string error)
        {
            preview = null;
            error = string.Empty;
            if (payload == null || payload.Length == 0)
            {
                error = "图片无法预览：数据为空。";
                return false;
            }

            MemoryStream stream = null;
            try
            {
                stream = new MemoryStream(payload.Data, false);
                Image image = Image.FromStream(stream);
                preview = new ImagePreview(image, stream);
                return true;
            }
            catch (Exception ex)
            {
                if (stream != null)
                {
                    stream.Dispose();
                }
                error = "图片无法预览：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 生成缩略图：等比缩放到最大 250×200 后另存为 PNG。
        /// 失败（原图被移动/删除、解码异常）返回 false，调用方应继续主流程。
        /// </summary>
        public static bool TryCreateThumbnail(string sourcePath, string targetPath)
        {
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(targetPath))
            {
                return false;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(sourcePath);
                if (bytes.Length == 0)
                {
                    return false;
                }

                using (MemoryStream input = new MemoryStream(bytes, false))
                using (Image source = Image.FromStream(input))
                {
                    int width = source.Width;
                    int height = source.Height;
                    if (width <= 0 || height <= 0)
                    {
                        return false;
                    }

                    double scale = Math.Min(
                        (double)Defaults.ThumbMaxWidth / width,
                        (double)Defaults.ThumbMaxHeight / height);
                    int thumbWidth = Math.Max(1, (int)Math.Round(width * scale));
                    int thumbHeight = Math.Max(1, (int)Math.Round(height * scale));

                    using (Bitmap thumb = new Bitmap(thumbWidth, thumbHeight))
                    {
                        using (Graphics graphics = Graphics.FromImage(thumb))
                        {
                            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            graphics.SmoothingMode = SmoothingMode.HighQuality;
                            graphics.DrawImage(source, 0, 0, thumbWidth, thumbHeight);
                        }

                        string directory = Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        // 缩略图是可再生成的附属数据，不做原子写
                        using (FileStream stream = new FileStream(targetPath, FileMode.Create,
                            FileAccess.Write, FileShare.None))
                        {
                            thumb.Save(stream, ImageFormat.Png);
                        }
                    }
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>生成缩略图文件名：32 位十六进制 guid + .png。</summary>
        public static string NewThumbFileName()
        {
            return Guid.NewGuid().ToString("N") + ".png";
        }

        /// <summary>
        /// best-effort 删除缩略图文件。传入的只应是文件名，
        /// 这里再做一次 Path.GetFileName 净化，避免路径穿越。
        /// </summary>
        public static void TryDeleteThumb(string thumbFile)
        {
            try
            {
                if (string.IsNullOrEmpty(thumbFile))
                {
                    return;
                }
                string name = Path.GetFileName(thumbFile);
                if (string.IsNullOrEmpty(name))
                {
                    return;
                }
                string path = Path.Combine(Storage.ThumbsDir, name);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // 删除失败仅残留孤儿文件，不影响记录删除
            }
        }
    }
}
