using System.Text;

namespace Shadowrun
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 4 || args[0] != "-E" || args[2] != "-T")
            {
                Console.WriteLine("用法:Shadowrun -E <数据文件> -T <目录文件>");
                Console.WriteLine("示例:Shadowrun -E game.data -T game.toc");
                Console.WriteLine("当前参数:");
                for (int i = 0; i < args.Length; i++)
                {
                    Console.WriteLine($"  [{i}] = {args[i]}");
                }
                return;
            }

            string dataFile = args[1];
            string tocFile = args[3];

            try
            {
                Unpacker unpacker = new Unpacker();
                unpacker.Unpack(dataFile, tocFile);
                Console.WriteLine("解包成功完成!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误:{ex.Message}");
                Console.WriteLine($"堆栈跟踪:{ex.StackTrace}");
            }
        }
    }

    public class Unpacker
    {
        private bool isLittleEndian = true;

        public void Unpack(string dataFilePath, string tocFilePath)
        {
            if (!File.Exists(dataFilePath))
                throw new FileNotFoundException($"未找到数据文件:{dataFilePath}");

            if (!File.Exists(tocFilePath))
                throw new FileNotFoundException($"未找到目录文件:{tocFilePath}");

            Console.WriteLine($"数据文件:{dataFilePath}");
            Console.WriteLine($"目录文件:{tocFilePath}");
            Console.WriteLine();

            using (FileStream tocStream = new FileStream(tocFilePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader tocReader = new BinaryReader(tocStream))
            using (FileStream dataStream = new FileStream(dataFilePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader dataReader = new BinaryReader(dataStream))
            {
                ParseTOCFile(tocReader, dataReader);
            }
        }

        private void ParseTOCFile(BinaryReader tocReader, BinaryReader dataReader)
        {
            long startPos = tocReader.BaseStream.Position;

            byte[] signature = tocReader.ReadBytes(4);
            string sigStr = Encoding.ASCII.GetString(signature);

            Console.WriteLine($"发现签名:'{sigStr}'(十六进制:{BitConverter.ToString(signature)})");

            if (sigStr == "srr1")
            {
                Console.WriteLine("检测到小端序格式(srr1是1rrs的反向)");
                isLittleEndian = true;

                tocReader.BaseStream.Seek(startPos, SeekOrigin.Begin);
                ParseLittleEndianTOC(tocReader, dataReader);
                return;
            }
            else if (sigStr == "1rrs")
            {
                Console.WriteLine("检测到大端序格式");
                isLittleEndian = false;
                ParseBigEndianTOC(tocReader, dataReader);
                return;
            }
            else
            {
                throw new InvalidDataException($"不支持的目录文件签名:'{sigStr}',预期为'1rrs'或'srr1'。");
            }
        }

        private void ParseLittleEndianTOC(BinaryReader tocReader, BinaryReader dataReader)
        {
            tocReader.ReadBytes(4);

            uint version = ReadUInt32LittleEndian(tocReader);
            uint fileCount = ReadUInt32LittleEndian(tocReader);
            uint dummy1 = ReadUInt32LittleEndian(tocReader);

            Console.WriteLine($"版本:{version},文件数量:{fileCount}");

            tocReader.ReadBytes(16);

            uint hashesCount = ReadUInt32LittleEndian(tocReader);
            uint dummy2 = ReadUInt32LittleEndian(tocReader);

            byte[] sign = tocReader.ReadBytes(4);
            string signStr = Encoding.ASCII.GetString(sign);

            Console.WriteLine($"二级签名:'{signStr}',哈希数量:{hashesCount}");

            if (signStr != "bssd" && signStr != "dssb")
            {
                Console.WriteLine($"警告:意外的二级签名'{signStr}',预期为'dssb'或'bssd'");
            }

            for (int i = 0; i < hashesCount; i++)
            {
                tocReader.ReadBytes(20);
                tocReader.ReadBytes(12);
            }

            int zeroCount = 0;
            while (tocReader.BaseStream.Position < tocReader.BaseStream.Length - 4)
            {
                uint testValue = ReadUInt32LittleEndian(tocReader);
                if (testValue != 0)
                {
                    tocReader.BaseStream.Seek(-4, SeekOrigin.Current);
                    break;
                }
                zeroCount++;
            }

            Console.WriteLine($"在文件列表前跳过了{zeroCount}个零双字");

            ExtractFilesLittleEndian(tocReader, dataReader, fileCount);
        }

        private void ParseBigEndianTOC(BinaryReader tocReader, BinaryReader dataReader)
        {
            uint version = tocReader.ReadUInt32();
            uint fileCount = tocReader.ReadUInt32();
            uint dummy1 = tocReader.ReadUInt32();

            Console.WriteLine($"版本:{version}, 文件数量:{fileCount}");

            tocReader.ReadBytes(16);

            uint hashesCount = tocReader.ReadUInt32();
            uint dummy2 = tocReader.ReadUInt32();

            byte[] sign = tocReader.ReadBytes(4);
            string signStr = Encoding.ASCII.GetString(sign);

            Console.WriteLine($"二级签名:'{signStr}',哈希数量:{hashesCount}");

            for (int i = 0; i < hashesCount; i++)
            {
                tocReader.ReadBytes(20);
                tocReader.ReadBytes(12);
            }

            int zeroCount = 0;
            while (tocReader.BaseStream.Position < tocReader.BaseStream.Length - 4)
            {
                uint testValue = tocReader.ReadUInt32();
                if (testValue != 0)
                {
                    tocReader.BaseStream.Seek(-4, SeekOrigin.Current);
                    break;
                }
                zeroCount++;
            }

            Console.WriteLine($"在文件列表前跳过了{zeroCount}个零双字");

            ExtractFilesBigEndian(tocReader, dataReader, fileCount);
        }

        private void ExtractFilesLittleEndian(BinaryReader tocReader, BinaryReader dataReader, uint fileCount)
        {
            string outputDir = "extracted_files";
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            Console.WriteLine($"开始提取{fileCount}个文件...");

            for (int i = 0; i < fileCount; i++)
            {
                try
                {
                    if (tocReader.BaseStream.Position >= tocReader.BaseStream.Length - 50)
                    {
                        Console.WriteLine("提前到达目录文件末尾");
                        break;
                    }

                    uint size = ReadUInt32LittleEndian(tocReader);
                    uint offset = ReadUInt32LittleEndian(tocReader);

                    tocReader.ReadBytes(8);

                    ushort nameSize = ReadUInt16LittleEndian(tocReader);

                    tocReader.ReadByte();

                    byte[] nameBytes = tocReader.ReadBytes(nameSize);
                    string fileName = Encoding.ASCII.GetString(nameBytes);

                    Console.WriteLine($"[{i + 1}/{fileCount}]正在提取:{fileName}(偏移:0x{offset:X8},大小:{size}字节)");

                    ExtractSingleFile(dataReader, fileName, offset, size, outputDir);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"提取文件{i}时出错: {ex.Message}");
                }
            }
        }

        private void ExtractFilesBigEndian(BinaryReader tocReader, BinaryReader dataReader, uint fileCount)
        {
            string outputDir = "extracted_files";
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            Console.WriteLine($"开始提取{fileCount}个文件...");

            for (int i = 0; i < fileCount; i++)
            {
                try
                {
                    if (tocReader.BaseStream.Position >= tocReader.BaseStream.Length - 50)
                    {
                        Console.WriteLine("提前到达目录文件末尾");
                        break;
                    }

                    uint size = tocReader.ReadUInt32();
                    uint offset = tocReader.ReadUInt32();

                    tocReader.ReadBytes(8);

                    ushort nameSize = tocReader.ReadUInt16();
                    tocReader.ReadByte();

                    byte[] nameBytes = tocReader.ReadBytes(nameSize);
                    string fileName = Encoding.ASCII.GetString(nameBytes);

                    Console.WriteLine($"[{i + 1}/{fileCount}]正在提取:{fileName}(偏移:0x{offset:X8},大小:{size}字节)");

                    ExtractSingleFile(dataReader, fileName, offset, size, outputDir);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"提取文件{i}时出错:{ex.Message}");
                }
            }
        }

        private void ExtractSingleFile(BinaryReader dataReader, string fileName, uint offset, uint size, string outputDir)
        {
            string safeFileName = MakeSafeFileName(fileName);
            string outputPath = Path.Combine(outputDir, safeFileName);

            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Console.WriteLine($"创建目录:{directory}");
                Directory.CreateDirectory(directory);
            }

            if (offset >= dataReader.BaseStream.Length)
            {
                throw new ArgumentException($"偏移:0x{offset:X8}超出数据文件,大小:0x{dataReader.BaseStream.Length:X8}");
            }

            if (offset + size > dataReader.BaseStream.Length)
            {
                throw new ArgumentException($"文件{fileName}超出数据文件范围(偏移:0x{offset:X8}, 大小:{size},数据文件大小:0x{dataReader.BaseStream.Length:X8})");
            }

            dataReader.BaseStream.Seek(offset, SeekOrigin.Begin);

            byte[] fileData = dataReader.ReadBytes((int)size);

            File.WriteAllBytes(outputPath, fileData);
        }

        private uint ReadUInt32LittleEndian(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToUInt32(bytes, 0);
        }

        private ushort ReadUInt16LittleEndian(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(2);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToUInt16(bytes, 0);
        }

        private string MakeSafeFileName(string fileName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            StringBuilder safeName = new StringBuilder();

            foreach (char c in fileName)
            {
                if (c == '\\' || c == '/')
                {
                    safeName.Append(Path.DirectorySeparatorChar);
                }
                else if (Array.IndexOf(invalidChars, c) == -1)
                {
                    safeName.Append(c);
                }
                else
                {
                    safeName.Append('_');
                }
            }

            return safeName.ToString();
        }
    }
}