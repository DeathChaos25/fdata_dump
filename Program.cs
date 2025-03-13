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
        public static List<RDB_HashMap> ExtensionsDictionary = new List<RDB_HashMap>();

        public static bool DEBUG = false;

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

            string csv_ext_path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "rdb_extensions.csv"); // get extension dictionary csv from app folder
            ExtensionsDictionary = ProcessExtensionsCSV(csv_ext_path);

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
                    Console.WriteLine($"Processing FData file {Path.GetFileName(filePath)}");

                    long IDRKOffset = 0;

                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        if (DEBUG) Console.WriteLine($"IDRK at 0x{reader.BaseStream.Position:X}");
                        IDRKOffset = reader.BaseStream.Position;

                        RDB.RdbEntry entry = RDB.ReadEntry(reader);

                        string ext = getExtensionFromKTIDInfo(entry.TypeInfoKtid);
                        string fname = $"{entry.FileKtid:X}.{ext}";

                        fname = getPredefinedName(fname); // check if name matches in csv

                        if (DEBUG) Console.WriteLine($"Expected filename is {fname}");

                        string fullName = Path.Combine(Path.GetDirectoryName(filePath), "fdata_out", ext);
                        Directory.CreateDirectory(fullName);
                        string outputPath = Path.Combine(fullName, fname);

                        if (File.Exists(outputPath))
                        {
                            entry.FileSize = 0;
                            Console.WriteLine($"Skipping FData file {Path.GetFileName(filePath)} target file {fname}");
                        }
                        else
                        {
                            Console.WriteLine($"Extracting FData file {Path.GetFileName(filePath)} target file {fname}");
                        }

                        if (entry.CompSize == entry.FileSize)
                        {
                            byte[] decompressedData = reader.ReadBytes((int)entry.FileSize);
                            using (FileStream output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                output.Write(decompressedData);
                            }
                            if (DEBUG) Console.WriteLine($"File {fname} is not compressed; skipping decompression step");
                        }
                        else
                        {
                            while (entry.FileSize > 0)
                            {
                                ushort zsize = reader.ReadUInt16();

                                if (DEBUG) Console.WriteLine($"zsize 0x{zsize:X} at 0x{reader.BaseStream.Position - 2:X}");
                                long junk = reader.ReadInt64();

                                byte[] compressedData;
                                byte[] decompressedData;

                                if (entry.FileSize > 16384)
                                {
                                    compressedData = reader.ReadBytes((int)zsize);
                                    decompressedData = DecompressStream(compressedData, 16384);
                                    entry.FileSize -= 16384;
                                }
                                else
                                {
                                    compressedData = reader.ReadBytes((int)zsize);
                                    decompressedData = DecompressStream(compressedData, (int)entry.FileSize);
                                    entry.FileSize = 0;
                                }

                                using (FileStream output = new FileStream(outputPath, FileMode.Append, FileAccess.Write, FileShare.None))
                                {
                                    output.Write(decompressedData);
                                }
                            }
                        }
                        if (DEBUG) Console.WriteLine($"RDB Data end at 0x{reader.BaseStream.Position:X}");

                        reader.BaseStream.Position = IDRKOffset + (long)entry.EntrySize;
                        reader.BaseStream.Position += ((0x10 - reader.BaseStream.Position % 0x10) % 0x10);

                        if (DEBUG) Console.WriteLine($"After padding 0x{reader.BaseStream.Position:X}");
                        if (DEBUG) Console.WriteLine();
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

        public static string getExtensionFromKTIDInfo( uint ktid_typeinfo )
        {
            foreach (RDB_HashMap hashEntry in ExtensionsDictionary)
            {
                if (hashEntry.ktid_typeinfo.ToLower() == $"{ktid_typeinfo:X}".ToLower())
                {
                    return hashEntry.extension;
                }
            }

            return $"{ktid_typeinfo:X}";
        }

        public static List<RDB_Names> ProcessRDBCSV(string csvData)
        {
            using (var reader = new StreamReader(csvData))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                return csv.GetRecords<RDB_Names>().ToList();
            }
        }

        public static List<RDB_HashMap> ProcessExtensionsCSV(string csvData)
        {
            using (var reader = new StreamReader(csvData))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                return csv.GetRecords<RDB_HashMap>().ToList();
            }
        }

        public static string getPredefinedName( string filename )
        {
            foreach (RDB_Names nameEntry in GlobalNameList)
            {
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

        public class RDB_HashMap
        {
            public string ktid_typeinfo { get; set; }
            public string extension { get; set; }
        }
    }
}
