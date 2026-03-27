using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DdsBatchAnalyzer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== DDS 批量分析工具 ===\n");

            // 获取目标文件夹路径
            string targetPath = GetTargetFolder();
            if (string.IsNullOrEmpty(targetPath))
            {
                Console.WriteLine("未选择有效文件夹，程序退出。");
                return;
            }

            Console.WriteLine($"\n目标文件夹: {targetPath}");
            Console.WriteLine("正在扫描 DDS 文件...");

            // 获取所有 DDS 文件（包括子目录）
            var ddsFiles = Directory.GetFiles(targetPath, "*.dds", SearchOption.AllDirectories)
                                     .OrderBy(f => f)
                                     .ToList();

            if (ddsFiles.Count == 0)
            {
                Console.WriteLine("未找到任何 DDS 文件。");
                return;
            }

            Console.WriteLine($"找到 {ddsFiles.Count} 个 DDS 文件，正在分析...\n");

            // 分析所有文件
            var results = new List<DdsRecord>();
            int successCount = 0;
            int errorCount = 0;

            for (int i = 0; i < ddsFiles.Count; i++)
            {
                string filePath = ddsFiles[i];
                string relativePath = GetRelativePath(targetPath, filePath);

                Console.Write($"\r处理进度: {i + 1}/{ddsFiles.Count} - {Path.GetFileName(filePath)}");

                try
                {
                    var metadata = DdsMetadata.Parse(filePath);
                    long fileSize = new FileInfo(filePath).Length;

                    results.Add(new DdsRecord
                    {
                        RelativePath = relativePath,
                        Encoding = metadata.Encoding,
                        Resolution = $"{metadata.Width}x{metadata.Height}",
                        MipmapLevels = metadata.MipMapCount,
                        FileSizeBytes = fileSize
                    });

                    successCount++;
                }
                catch (Exception ex)
                {
                    results.Add(new DdsRecord
                    {
                        RelativePath = relativePath,
                        Encoding = $"错误: {ex.Message}",
                        Resolution = "N/A",
                        MipmapLevels = 0,
                        FileSizeBytes = new FileInfo(filePath).Length
                    });

                    errorCount++;
                }
            }

            Console.WriteLine("\n");

            // 生成 CSV 文件
            string csvPath = Path.Combine(targetPath, "manifest.csv");
            GenerateCsv(csvPath, results);

            // 输出统计信息
            Console.WriteLine($"=== 分析完成 ===");
            Console.WriteLine($"成功: {successCount} 个文件");
            Console.WriteLine($"失败: {errorCount} 个文件");
            Console.WriteLine($"总计: {results.Count} 个文件");
            Console.WriteLine($"\nCSV 文件已保存至: {csvPath}");

            // 可选：打开 CSV 文件所在文件夹
            Console.Write("\n是否打开 CSV 所在文件夹？(Y/N): ");
            if (Console.ReadLine()?.Trim().ToUpper() == "Y")
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{csvPath}\"");
            }

            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }

        static string? GetTargetFolder()
        {
            Console.WriteLine("请输入要分析的文件夹路径（或拖拽文件夹至此）:");
            string input = Console.ReadLine()?.Trim().Trim('"');

            if (!string.IsNullOrEmpty(input) && Directory.Exists(input))
            {
                return input;
            }

            Console.WriteLine("路径无效，请重试。");
            return null;
        }

        static string GetRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;

            Uri baseUri = new Uri(basePath);
            Uri fullUri = new Uri(fullPath);
            Uri relativeUri = baseUri.MakeRelativeUri(fullUri);

            return Uri.UnescapeDataString(relativeUri.ToString())
                      .Replace('/', Path.DirectorySeparatorChar);
        }

        static void GenerateCsv(string csvPath, List<DdsRecord> records)
        {
            var csv = new StringBuilder();

            // 写入 CSV 头
            csv.AppendLine("\"相对路径\",\"编码方式\",\"分辨率\",\"Mipmap级别\",\"大小(字节)\"");

            // 写入数据行
            foreach (var record in records)
            {
                csv.AppendLine($"\"{EscapeCsvField(record.RelativePath)}\"," +
                              $"\"{EscapeCsvField(record.Encoding)}\"," +
                              $"\"{record.Resolution}\"," +
                              $"{record.MipmapLevels}," +
                              $"{record.FileSizeBytes}");
            }

            File.WriteAllText(csvPath, csv.ToString(), System.Text.Encoding.UTF8);
        }

        static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return "";

            // CSV 字段中的双引号需要转义为两个双引号
            return field.Replace("\"", "\"\"");
        }
    }

    /// <summary>
    /// DDS 文件元数据记录
    /// </summary>
    public class DdsRecord
    {
        public string? RelativePath { get; set; }
        public string? Encoding { get; set; }
        public string? Resolution { get; set; }
        public uint MipmapLevels { get; set; }
        public long FileSizeBytes { get; set; }
    }

    /// <summary>
    /// DDS 元数据解析器
    /// </summary>
    public class DdsMetadata
    {
        // DDS 文件头结构常量
        private const int DdsHeaderSize = 124;
        private const uint DdsMagic = 0x20534444; // "DDS "

        // 像素格式标志位
        private const uint DDPF_FOURCC = 0x00000004;
        private const uint DDPF_ALPHAPIXELS = 0x00000001;
        private const uint DDPF_RGB = 0x00000040;

        public uint Width { get; private set; }
        public uint Height { get; private set; }
        public uint MipMapCount { get; private set; }
        public string? Encoding { get; private set; }

        public static DdsMetadata Parse(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                // 验证 DDS 标识
                uint magic = reader.ReadUInt32();
                if (magic != DdsMagic)
                    throw new InvalidDataException("无效的 DDS 文件格式");

                // 读取 DDS_HEADER 结构 (124 字节)
                uint size = reader.ReadUInt32(); // 应为 124
                uint flags = reader.ReadUInt32();
                uint height = reader.ReadUInt32();
                uint width = reader.ReadUInt32();
                uint pitchOrLinearSize = reader.ReadUInt32();
                uint depth = reader.ReadUInt32();
                uint mipMapCount = reader.ReadUInt32();

                // 跳过 11 个 DWORD 保留字段 (44 字节)
                reader.BaseStream.Seek(44, SeekOrigin.Current);

                // 读取像素格式结构 (32 字节)
                uint pfSize = reader.ReadUInt32();     // 应为 32
                uint pfFlags = reader.ReadUInt32();
                uint fourCC = reader.ReadUInt32();
                uint rgbBitCount = reader.ReadUInt32();
                uint rBitMask = reader.ReadUInt32();
                uint gBitMask = reader.ReadUInt32();
                uint bBitMask = reader.ReadUInt32();
                uint aBitMask = reader.ReadUInt32();

                // 解析编码方式
                string encoding = ParseEncoding(reader, pfFlags, fourCC, rgbBitCount,
                                                rBitMask, gBitMask, bBitMask, aBitMask);

                return new DdsMetadata
                {
                    Width = width,
                    Height = height,
                    MipMapCount = mipMapCount == 0 ? (uint)1 : mipMapCount,
                    Encoding = encoding
                };
            }
        }

        private static string ParseEncoding(BinaryReader reader, uint pfFlags, uint fourCC, uint bitCount,
                                            uint rMask, uint gMask, uint bMask, uint aMask)
        {
            // 压缩纹理 (FourCC)
            if ((pfFlags & DDPF_FOURCC) != 0)
            {
                // 将 FourCC 数值转为字符串 (如 'DXT1' → "DXT1")
                byte[] fourCcBytes = BitConverter.GetBytes(fourCC);
                string fourCcStr = System.Text.Encoding.ASCII.GetString(fourCcBytes).TrimEnd('\0');

                // 处理 DX10+ 格式
                if (fourCcStr == "DX10")
                {
                    try
                    {
                        // 读取 DDS_HEADER_DXT10 扩展头 (20 字节)
                        uint dxgiFormat = reader.ReadUInt32();
                        uint resourceDimension = reader.ReadUInt32();
                        uint miscFlag = reader.ReadUInt32();
                        uint arraySize = reader.ReadUInt32();
                        uint miscFlags2 = reader.ReadUInt32();

                        return GetDxgiFormatString(dxgiFormat);
                    }
                    catch
                    {
                        return "DX10 (解析失败)";
                    }
                }

                // 常见压缩格式映射
                return fourCcStr switch
                {
                    "DXT1" => "BC1 (DXT1)",
                    "DXT2" => "BC2 (DXT2)",
                    "DXT3" => "BC2 (DXT3)",
                    "DXT4" => "BC3 (DXT4)",
                    "DXT5" => "BC3 (DXT5)",
                    "BC4U" => "BC4_UNORM",
                    "BC4S" => "BC4_SNORM",
                    "BC5U" => "BC5_UNORM",
                    "BC5S" => "BC5_SNORM",
                    "ATI1" => "BC4 (ATI1)",
                    "ATI2" => "BC5 (ATI2)",
                    "BC6H" => "BC6H",
                    "BC7" => "BC7",
                    _ => fourCcStr
                };
            }

            // RGB / RGBA 格式 (未压缩)
            if ((pfFlags & DDPF_RGB) != 0)
            {
                string format = "";
                string layout = "";

                // 确定位深
                if (bitCount == 32)
                    format = (aMask != 0) ? "32bpp RGBA" : "32bpp RGBx";
                else if (bitCount == 24)
                    format = "24bpp RGB";
                else if (bitCount == 16)
                    format = "16bpp RGB";
                else
                    format = $"{bitCount}bpp RGB";

                // 判断颜色布局（主要针对 32bpp）
                if (rMask == 0x00FF0000 && gMask == 0x0000FF00 && bMask == 0x000000FF)
                    layout = "BGRA";
                else if (rMask == 0x000000FF && gMask == 0x0000FF00 && bMask == 0x00FF0000)
                    layout = "RGBA";
                else if (rMask == 0x0000F800 && gMask == 0x000007E0 && bMask == 0x0000001F)
                    layout = "R5G6B5";
                else if (rMask == 0x00007C00 && gMask == 0x000003E0 && bMask == 0x0000001F)
                    layout = "A1R5G5B5";

                return string.IsNullOrEmpty(layout) ? format : $"{format} ({layout})";
            }

            return "未知格式";
        }

        private static string GetDxgiFormatString(uint dxgiFormat)
        {
            // DXGI_FORMAT 枚举常用值
            return dxgiFormat switch
            {
                71 => "BC1_UNORM",
                72 => "BC1_UNORM_SRGB",
                73 => "BC2_UNORM",
                74 => "BC2_UNORM_SRGB",
                75 => "BC3_UNORM",
                76 => "BC3_UNORM_SRGB",
                77 => "BC4_UNORM",
                78 => "BC4_SNORM",
                79 => "BC5_UNORM",
                80 => "BC5_SNORM",
                95 => "BC6H_UF16",
                96 => "BC6H_SF16",
                97 => "BC7_UNORM",
                98 => "BC7_UNORM_SRGB",
                24 => "R8G8B8A8_UNORM",
                26 => "R8G8B8A8_UNORM_SRGB",
                28 => "B8G8R8A8_UNORM",
                29 => "B8G8R8A8_UNORM_SRGB",
                _ => $"DXGI_FORMAT_{dxgiFormat}"
            };
        }
    }
}