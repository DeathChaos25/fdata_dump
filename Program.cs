using System;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using CsvHelper;

namespace fdata_dump
{
    class Program
    {
        public static List<RDB_Names> GlobalNameList = new List<RDB_Names>();

        public static async Task Main(string[] args)
        {
            Stopwatch timer = new Stopwatch();
            timer.Start();

            Console.WriteLine("Koei Tecmo FData extractor\n");
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: drag and drop the folder where the .fdata files are located (subfolders are checked).\nPress any key to exit...");
                Console.ReadKey();
                return;
            }

            string csv_path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "rdb_common.csv"); // get names csv from app folder
            GlobalNameList = ProcessRDBCSV(csv_path);

            string[] filePaths = Directory.GetFiles(args[0], "*.fdata*", SearchOption.AllDirectories);

            List<Task> tasks = new List<Task>();
            foreach (string filePath in filePaths)
            {
                tasks.Add(Task.Run(() => ProcessFileAsync(filePath)));
            }

            await Task.WhenAll(tasks);

            timer.Stop();
            Console.WriteLine($"\nDone! Time elapsed: {timer.Elapsed}\nPress any key to exit...");
            Console.ReadKey();
        }

        public static async Task ProcessFileAsync(string filePath)
        {
            try
            {
                using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    reader.BaseStream.Position = 0x10;
                    long IDRKOffset = 0;

                    Console.WriteLine($"Processing FData file {Path.GetFileName(filePath)}");

                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        IDRKOffset = reader.BaseStream.Position;
                        // Console.WriteLine($"IDRKOffset is {IDRKOffset:X}");

                        long magicVersion = reader.ReadInt64();
                        // Console.WriteLine($"IDRK {magicVersion:X}");
                        long entrySize = reader.ReadInt64();
                        long compSize = reader.ReadInt64();
                        long decompSize = reader.ReadInt64();
                        int entryType = reader.ReadInt32();
                        int fileKtid = reader.ReadInt32();
                        uint typeInfoKtid = reader.ReadUInt32();

                        long SKIP = entrySize - compSize;
                        SKIP -= 0x2C;

                        reader.BaseStream.Position += SKIP;

                        // Console.WriteLine($"Temp: {reader.BaseStream.Position:X}");

                        string ext = getExtensionFromKTIDInfo(typeInfoKtid);
                        string fname = $"{fileKtid:X}.{ext}";

                        fname = getPredefinedName(fname); // check if name matches in csv

                        // Console.WriteLine($"Expected filename is {fname}");

                        string fullName = Path.Combine(Path.GetDirectoryName(filePath), "fdata_out", ext);
                        Directory.CreateDirectory(fullName);
                        string outputPath = Path.Combine(fullName, fname);

                        if (File.Exists(outputPath))
                        {
                            decompSize = 0;
                            Console.WriteLine($"Skipping FData file {Path.GetFileName(filePath)} target file {fname}");
                        }
                        else
                        {
                            Console.WriteLine($"Extracting FData file {Path.GetFileName(filePath)} target file {fname}");
                        }

                        while (decompSize > 0)
                        {
                            short zsize = reader.ReadInt16();

                            // Console.WriteLine($"zsize {zsize} at {reader.BaseStream.Position:X}");
                            long junk = reader.ReadInt64();

                            byte[] compressedData;
                            byte[] decompressedData;

                            if (compSize == decompSize)
                            {
                                decompressedData = reader.ReadBytes((int)zsize);
                                decompSize = 0;
                            }
                            else if (decompSize > 16384)
                            {
                                compressedData = reader.ReadBytes((int)zsize);
                                decompressedData = DecompressStream(compressedData, 16384);
                                decompSize -= 16384;
                            }
                            else
                            {
                                compressedData = reader.ReadBytes((int)zsize);
                                decompressedData = DecompressStream(compressedData, (int)decompSize);
                                decompSize = 0;
                            }

                            using (FileStream output = new FileStream(outputPath, FileMode.Append, FileAccess.Write, FileShare.None))
                            {
                                output.Write(decompressedData);
                            }
                        }

                        var targetPadding = IDRKOffset + entrySize;
                        targetPadding += ((0x10 - targetPadding % 0x10) % 0x10);

                        // Console.WriteLine($"NextPost calculated as {targetPadding}");
                        // Console.WriteLine($"Current Offset is {reader.BaseStream.Position:X8}");

                        reader.BaseStream.Position = targetPadding;

                        // Console.WriteLine($"Fixed offset is {reader.BaseStream.Position:X8}");

                        // Console.WriteLine($"\n");
                    }

                    Console.WriteLine($"Finished processing FData file {Path.GetFileName(filePath)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading file: " + filePath + ": " + ex.Message);
            }
        }

        public static byte[] DecompressStream(byte[] compresseddata, int decompressedSize)
        {
            Stream compressedStream = new MemoryStream(compresseddata, false);
            using (MemoryStream decompressedMemoryStream = new MemoryStream(decompressedSize))
            {
                using (ZLibStream deflateStream = new ZLibStream(compressedStream, CompressionMode.Decompress))
                {
                    byte[] buffer = new byte[4096];
                    int bytesRead;

                    while ((bytesRead = deflateStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        decompressedMemoryStream.Write(buffer, 0, bytesRead);
                    }
                }

                return decompressedMemoryStream.ToArray();
            }
        }

        private static readonly Dictionary<uint, string> ExtensionMap = new Dictionary<uint, string>
        {
            { 0x563bdef1, "g1m" },
            { 0x6fa91671, "g1a" },
            { 0xafbec60c, "g1t" },
            { 0x8e39aa37, "ktid" },
            { 0xbe144b78, "ktid" },
            { 0x20a6a0bb, "kidsobjdb" },
            { 0x5153729b, "mtl" },
            { 0xb340861a, "mtl" },
            { 0x56efe45c, "grp" },
            { 0xbbf9b49d, "grp" },
            { 0x0d34474d, "srst" },
            { 0x27bc54b7, "rigbin" },
            { 0x54738c76, "g1co" },
            { 0x56d8deda, "sid" },
            { 0x5c3e543c, "swg" },
            { 0x7bcd279f, "g1s" },
            { 0x9cb3a4b6, "oidex" },
            { 0xbbd39f2d, "srsa" },
            { 0x1ab40ae8, "oid" },
            { 0xed410290, "kts" },
            { 0x1fdcaa40, "kidstask" },
            { 0x4D0102AC, "g1em" },
            { 0x5599AA51, "kscl" },
            { 0xB097D41F, "g1e" },
            { 0xB1630F51, "kidsrender" },
            { 0xD7F47FB1, "efpl" },
            { 0xF20DE437, "texinfo" },
            { 0xF13845EF, "sclshape" },
            { 0xa8d88566, "g1cox" },
            { 0x17614AF5, "g1mx" },
            { 0x79C724C2, "g1p" },
            { 0xb0a14534, "sgcbin" }
        };

        public static string getExtensionFromKTIDInfo( uint ktid_typeinfo )
        {
            if (ExtensionMap.TryGetValue(ktid_typeinfo, out var extension))
            {
                return extension;
            }
            else
            {
                // Console.WriteLine($"Unknown TypeInfo {ktid_typeinfo:X}");
                return $"{ktid_typeinfo:X}";
            }
        }

        public static List<RDB_Names> ProcessRDBCSV(string csvData)
        {
            using (var reader = new StreamReader(csvData))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                return csv.GetRecords<RDB_Names>().ToList();
            }
        }

        public static string getPredefinedName( string filename )
        {
            foreach (RDB_Names nameEntry in GlobalNameList)
            {
                // Console.WriteLine($"Comparing {Path.GetFileNameWithoutExtension(filename)} to target name {nameEntry.Hash:X}");

                if (Path.GetFileNameWithoutExtension(filename).ToLower() == $"{nameEntry.Hash}".ToLower())
                {
                    Console.WriteLine($"RDB File {filename} matched to name {nameEntry.Name}");
                    filename = nameEntry.Name;
                }
            }
            return filename;
        }

        public class RDB_Names
        {
            public string Hash { get; set; }
            public string Name { get; set; }
        }
    }
}