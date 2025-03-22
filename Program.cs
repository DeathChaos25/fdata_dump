using System;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using CsvHelper;
using static fdata_dump.RDB_NameHash;

using p5spclib;
using p5spclib.Model.Intermediate;

namespace fdata_dump
{
    class Program
    {
        public static List<RDB_Names> GlobalNameList = new List<RDB_Names>();
        public static List<RDB_HashMap> ExtensionsDictionary = new List<RDB_HashMap>();

        public static List<string> OutputFileList = new List<string>();

        public static bool DEBUG = false;
        public static bool Log = true;

        public static string inPath = String.Empty;

        public static Dictionary<uint, string> TargetGroupingFolder = new Dictionary<uint, string>();

        public static KidsTypeInfoDb typeInfoDb;

        public static async Task Main(string[] args)
        {
            Stopwatch timer = new Stopwatch();
            timer.Start();

            Console.WriteLine("Koei Tecmo FData extractor\n");

            if (args.Length == 0)
            {
                Console.WriteLine("Usage: drag and drop the folder where the .fdata files are located (subfolders are checked).");
                Console.WriteLine("Optional arguments:");
                Console.WriteLine("  -fe : Enable FEW Three Hopes dumping mode.");
                Console.WriteLine("  -l  : Disable (most) logging.");
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
                return;
            }

            var optionalArgs = new Dictionary<string, bool>
            {
                { "-fe", false }, // FEW Three Hopes mode
                { "-l", false }   // Logging mode
            };

            // Check for optional arguments
            for (int i = 1; i < args.Length; i++)
            {
                if (optionalArgs.ContainsKey(args[i].ToLower()))
                {
                    optionalArgs[args[i].ToLower()] = true;
                }
                else
                {
                    Console.WriteLine($"Warning: Unknown argument '{args[i]}' will be ignored.");
                }
            }

            // Access the optional arguments
            bool isFE = optionalArgs["-fe"];
            bool disableLogging = optionalArgs["-l"];

            if (disableLogging) Log = false;

            string csv_path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "rdb_common.csv"); // get names csv from app folder
            GlobalNameList = ProcessRDBCSV(csv_path);

            string csv_ext_path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "rdb_extensions.csv"); // get extension dictionary csv from app folder
            ExtensionsDictionary = ProcessExtensionsCSV(csv_ext_path);

            string priority_list = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "priority.txt");
            List<string> priorityList = File.ReadAllLines(priority_list).ToList();

            string debug_list = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "debug.txt");
            List<string> debugList = File.ReadAllLines(debug_list).ToList();

            string objdb_list = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "objdb.txt");
            List<string> objdbList = File.ReadAllLines(objdb_list).ToList();

            typeInfoDb = KidsTypeInfoDb.Load(Path.Combine(AppContext.BaseDirectory, "kidstypeinfodb.yml"));

            inPath = args[0];

            string[] filePaths = Directory.GetFiles(inPath, "*.fdata*", SearchOption.AllDirectories);

            List<Task> tasks = new List<Task>();

            var sortedFilePaths = filePaths
                .Where(filePath => priorityList.Contains(Path.GetFileName(filePath)))
                .ToList();

            var debugFiles = filePaths
                .Where(filePath => debugList.Contains(Path.GetFileName(filePath)))
                .ToList();

            // Process .name files first
            Console.WriteLine("Attempting to find and process .name files...");
            if (isFE)
            {
                foreach (string filePath in debugFiles)
                {
                    tasks.Add(Task.Run(() => ProcessFileAsyncFE(filePath)));
                }
            }
            else
            {
                foreach (string filePath in debugFiles)
                {
                    tasks.Add(Task.Run(() => ProcessFileAsync(filePath)));
                }
            }

            await Task.WhenAll(tasks);

            tasks.Clear(); // empty list

            ///////////////////// Process name files

            string[] namePaths = Directory.GetFiles(inPath, "*.name*", SearchOption.AllDirectories);

            List<Task<List<RDB_Names>>> tasks_ = new List<Task<List<RDB_Names>>>();
            foreach (string filePath in namePaths)
            {
                tasks_.Add(ProcessDotNameFiles(filePath));
            }

            List<RDB_Names>[] results = await Task.WhenAll(tasks_);

            List<RDB_Names> typeinfoNameList = new List<RDB_Names>();

            foreach (var result in results)
            {
                typeinfoNameList.AddRange(result);
            }

            /*foreach (var RDB_Names in typeinfoNameList)
            {
                Console.WriteLine($"{RDB_Names.Name} - {RDB_Names.Hash}");
            }*/

            foreach (var type_info_string in typeinfoNameList)
            {
                GetNameHashesFromTypeInfo(type_info_string.Name, type_info_string.Hash);
            }

            if (OutputFileList.Count > 0)
            {
                string outputFileName = Path.Combine(inPath, "fdata_out", "filelist-fdata-rdb.csv");
                File.WriteAllLines(outputFileName, OutputFileList);
            }

            Console.WriteLine($"\nFinished Processing .name files");
            timer.Stop();
            timer.Restart();


            Console.WriteLine("Attempting to Pre-process important FData files to obtain grouping info...");
            if (isFE)
            {
                foreach (string filePath in sortedFilePaths)
                {
                    tasks.Add(Task.Run(() => ProcessFileAsyncFE(filePath)));
                }
            }
            else
            {
                foreach (string filePath in sortedFilePaths)
                {
                    tasks.Add(Task.Run(() => ProcessFileAsync(filePath)));
                }
            }

            await Task.WhenAll(tasks);

            tasks.Clear();

            foreach (string filePath in sortedFilePaths)
            {
                tasks.Add(Task.Run(() => ProcessFileAsync(filePath)));
            }

            await Task.WhenAll(tasks);

            tasks.Clear();

            string[] objdbPaths = Directory.GetFiles(inPath, "*.kidssingletondb*", SearchOption.AllDirectories);

            var objdbFiles = objdbPaths
                .Where(objdbPaths => objdbList.Contains(Path.GetFileName(objdbPaths)))
                .ToList();

            foreach (string filePath in objdbFiles)
            {
                ProcessOBJDBFiles(filePath);
            }

            Console.WriteLine($"\nFinished Processing .name files\nTime elapsed: {timer.Elapsed}");
            timer.Stop();
            timer.Restart();

            Console.WriteLine("Processing FData files...");
            if (isFE)
            {
                Console.WriteLine("FEW Three Hopes mode enabled.");
                foreach (string filePath in filePaths)
                {
                    tasks.Add(Task.Run(() => ProcessFileAsyncFE(filePath)));
                }
            }
            else
            {
                foreach (string filePath in filePaths)
                {
                    tasks.Add(Task.Run(() => ProcessFileAsync(filePath)));
                }
            }

            await Task.WhenAll(tasks);

            tasks.Clear();

            string[] fileFilePaths = Directory.GetFiles(inPath, "*.file*", SearchOption.AllDirectories);

            Console.WriteLine("Processing .file files...");

            foreach (string filePath in fileFilePaths)
            {
                tasks.Add(Task.Run(() => ProcessFileFilesAsync(filePath)));
            }

            await Task.WhenAll(tasks);

            timer.Stop();
            Console.WriteLine($"\nDone! Time elapsed: {timer.Elapsed}\nPress any key to exit...");
            Console.ReadKey();
        }

        public static void ProcessOBJDBFiles(string filePath)
        {
            try
            {
                KidsObjDb kidsObjDb2 = new KidsObjDb();
                kidsObjDb2.LoadBinary(typeInfoDb, filePath);

                string GroupName = Path.GetFileNameWithoutExtension(filePath);

                Console.WriteLine($"Processing OBJDB file {GroupName}");

                foreach (KidsObj obj in kidsObjDb2.Objects)
                {
                    // Console.WriteLine($"Object Type : [0x{obj.TypeHash} {obj.TypeName}] - Hash 0x{obj.Hash}");
                    foreach (var property in obj.Properties)
                    {
                        for (int i = 0; i < property.Values.Count; i++)
                        {
                            if (property.Type == KidsPropertyType.UInt32)
                            {
                                if (property.UInt32Values[i] > 0)
                                {
                                    TargetGroupingFolder[property.UInt32Values[i]] = GroupName;
                                    if (DEBUG) Console.WriteLine($"Adding hash 0x{property.UInt32Values[i]} to {GroupName}");
                                }
                                // Console.WriteLine($"Property : {property.Name} - 0x{property.UInt32Values[i]:X8}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading file: " + filePath + ": " + ex.Message);
            }
        }


        public static async Task ProcessFileAsyncFE(string filePath) // FEW Three Hopes mode
        {
            try
            {
                using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    reader.BaseStream.Position = 0x10;
                    if (Log) Console.WriteLine($"Processing FData file {Path.GetFileName(filePath)}");

                    long IDRKOffset = 0;

                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        if (DEBUG) Console.WriteLine($"IDRK at 0x{reader.BaseStream.Position:X}");
                        IDRKOffset = reader.BaseStream.Position;

                        RDB.RdbEntry entry = RDB.ReadEntry(reader);

                        string ext = getExtensionFromKTIDInfo(entry.TypeInfoKtid);
                        string fname = $"{entry.FileKtid:X8}.{ext}";

                        fname = getPredefinedName(fname).ToLower(); // check if name matches in csv

                        if (DEBUG) Console.WriteLine($"Expected filename is {fname}");

                        string fullName = Path.Combine(inPath, "fdata_out", ext);
                        Directory.CreateDirectory(fullName);
                        string outputPath = Path.Combine(fullName, fname);

                        if (File.Exists(outputPath))
                        {
                            entry.FileSize = 0;
                            // if (Log) Console.WriteLine($"Skipping FData file {Path.GetFileName(filePath)} target file {fname}");
                        }
                        else
                        {
                            if (Log) Console.WriteLine($"Extracting FData file {Path.GetFileName(filePath)} target file {fname}");
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
                                uint zsize = reader.ReadUInt32();

                                if (DEBUG) Console.WriteLine($"zsize 0x{zsize:X} at 0x{reader.BaseStream.Position - 2:X}");
                                // long junk = reader.ReadInt64();

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

                    if (Log) Console.WriteLine($"Finished processing FData file {Path.GetFileName(filePath)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading file: " + filePath + ": " + ex.Message);
            }
        }


        public static async Task ProcessFileAsync(string filePath)
        {
            try
            {
                using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    reader.BaseStream.Position = 0x10;
                    if (Log) Console.WriteLine($"Processing FData file {Path.GetFileName(filePath)}");

                    long IDRKOffset = 0;

                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        if (DEBUG) Console.WriteLine($"IDRK at 0x{reader.BaseStream.Position:X}");
                        IDRKOffset = reader.BaseStream.Position;

                        RDB.RdbEntry entry = RDB.ReadEntry(reader);

                        string ext = getExtensionFromKTIDInfo(entry.TypeInfoKtid);
                        string fname = $"{entry.FileKtid:X8}.{ext}".ToLower();

                        fname = getPredefinedName(fname); // check if name matches in csv

                        // if (DEBUG) Console.WriteLine($"Expected filename is {fname}");

                        string groupFolder = "System";

                        string fullName = Path.Combine(inPath, "fdata_out", GetTargetGroupingFolder(entry.FileKtid, entry.TypeInfoKtid), ext);
                        Directory.CreateDirectory(fullName);
                        string outputPath = Path.Combine(fullName, fname);

                        if (File.Exists(outputPath))
                        {
                            entry.FileSize = 0;
                            // if (Log) Console.WriteLine($"Skipping FData file {Path.GetFileName(filePath)} target file {fname}");
                        }
                        else
                        {
                            if (Log) Console.WriteLine($"Extracting FData file {Path.GetFileName(filePath)} target file {fname}");
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

                    if (Log) Console.WriteLine($"Finished processing FData file {Path.GetFileName(filePath)}");
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

        static async Task<List<RDB_Names>> ProcessDotNameFiles(string filePath)
        {
            List<RDB_Names> fileResults = new List<RDB_Names>();

            try
            {
                using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    reader.BaseStream.Position = 0x18;
                    if (Log) Console.WriteLine($"Processing .name file {Path.GetFileName(filePath)}");

                    long IRNKOffset = 0;

                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        IRNKOffset = reader.BaseStream.Position;
                        if (DEBUG) Console.WriteLine($"IRNK at 0x{IRNKOffset}");
                        ulong Magic = reader.ReadUInt64();

                        uint entrySize = reader.ReadUInt32();
                        uint Field04 = reader.ReadUInt32();
                        uint pointerCount = reader.ReadUInt32();

                        List<uint> pointers = new List<uint>();

                        for (int i = 0; i < pointerCount; i++)
                        {
                            pointers.Add(reader.ReadUInt32());
                        }

                        reader.BaseStream.Position = IRNKOffset + pointers[0];
                        if (DEBUG) Console.WriteLine($"First String at at 0x{reader.BaseStream.Position:X}");
                        string targetName = ReadNullTerminatedString(reader);
                        targetName = RemoveHashSuffixPrefix(targetName);

                        reader.BaseStream.Position = IRNKOffset + pointers[1];
                        if (DEBUG) Console.WriteLine($"Second String at at 0x{reader.BaseStream.Position:X}");
                        string typeInfoString = ReadNullTerminatedString(reader);

                        fileResults.Add(new RDB_Names { Hash = typeInfoString, Name = targetName });

                        reader.BaseStream.Position = IRNKOffset + (long)entrySize;
                        reader.BaseStream.Position += ((4 - reader.BaseStream.Position % 4) % 4);

                        if (DEBUG) Console.WriteLine();
                    }

                    if (Log) Console.WriteLine($"Finished processing .name file {Path.GetFileName(filePath)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading file: " + filePath + ": " + ex.Message);
            }

            return fileResults;
        }

        public static string getExtensionFromKTIDInfo( uint ktid_typeinfo )
        {
            foreach (RDB_HashMap hashEntry in ExtensionsDictionary)
            {
                if (hashEntry.ktid_typeinfo.ToLower() == $"{ktid_typeinfo:X8}".ToLower())
                {
                    return hashEntry.extension;
                }
            }

            return $"{ktid_typeinfo:X8}";
        }

        public static async Task ProcessFileFilesAsync(string filePath)
        {
            try
            {
                using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    if (Log) Console.WriteLine($"Processing .file file {Path.GetFileName(filePath)}");

                    ulong magic = reader.ReadUInt64();

                    if (magic != 0x303030304B524449)
                    {
                        Console.WriteLine($"Invalid Magic, not IDRK, found 0x{magic:X16}");
                        return;
                    }

                    reader.BaseStream.Position = 0x24;

                    uint FileKtid = reader.ReadUInt32();
                    uint TypeInfoKtid = reader.ReadUInt32();

                    if (TypeInfoKtid != 0x0d34474d)
                    {
                        Console.WriteLine($"{Path.GetFileName(filePath)} is not a SRST file; skipping");
                        return;
                    }

                    string fname = $"{FileKtid:X8}.srst".ToLower();

                    fname = getPredefinedName(fname); // check if name matches in csv

                    string groupFolder = "RRPreview";

                    string fullName = Path.Combine(inPath, "fdata_out", groupFolder, "srst");
                    Directory.CreateDirectory(fullName);
                    string outputPath = Path.Combine(fullName, fname);

                    if (File.Exists(outputPath))
                    {
                        Console.WriteLine($"Skipping .file file {Path.GetFileName(filePath)} target file {fname}; file already exists");
                        return;
                    }

                    int bytesToSkip = 0x38;

                    using (FileStream outputFileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                    using (BinaryWriter writer = new BinaryWriter(outputFileStream))
                    {
                        reader.BaseStream.Seek(bytesToSkip, SeekOrigin.Begin);

                        // Read and write the remaining bytes
                        byte[] buffer = new byte[4096]; // Buffer size for reading/writing
                        int bytesRead;
                        while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            writer.Write(buffer, 0, bytesRead);
                        }
                    }

                    if (Log) Console.WriteLine($"Finished processing .file file {Path.GetFileName(filePath)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading file: " + filePath + ": " + ex.Message);
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

        public static string GetTargetGroupingFolder(uint fileHash, uint typeInfo)
        {
            if (typeInfo == 0xbf6b52c7)
            {
                return "System";
            }
            else if (typeInfo == 0x20a6a0bb)
            {
                return "KIDSSystemResource";
            }
            else if (typeInfo == 0x0d34474d || typeInfo == 0xbbd39f2d)
            {
                return "RRPreview";
            }

            if (TargetGroupingFolder.TryGetValue(fileHash, out string name))
            {
                return name;
            }

            return "Root";
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
                    //Console.WriteLine($"RDB File {filename} matched to name {nameEntry.Name}");
                    filename = nameEntry.Name;
                }
            }
            return filename;
        }

        public static void GetNameHashesFromTypeInfo(string fileName, string typeInfoType)
        {
            string targetName = String.Empty;

            if (typeInfoType == "TypeInfo::Object::3D::Displayset::Model")
            {
                targetName = $"{fileName}.g1m";
                GlobalNameList.Add(new RDB_Names { Hash = Hash(targetName), Name = targetName });
                OutputFileList.Add($"g1m,{Hash(targetName)},{fileName}");
                
                targetName = $"{fileName}.ktid";
                GlobalNameList.Add(new RDB_Names { Hash = Hash(targetName), Name = targetName });
                OutputFileList.Add($"ktid,{Hash(targetName)},{fileName}");
                
                targetName = $"{fileName}.mtl";
                GlobalNameList.Add(new RDB_Names { Hash = Hash(targetName), Name = targetName });
                OutputFileList.Add($"mtl,{Hash(targetName)},{fileName}");
                
                targetName = $"{fileName}.grp";
                GlobalNameList.Add(new RDB_Names { Hash = Hash(targetName), Name = targetName });
                OutputFileList.Add($"grp,{Hash(targetName)},{fileName}");
                
                targetName = $"{fileName}.oid";
                GlobalNameList.Add(new RDB_Names { Hash = Hash(targetName), Name = targetName });
                OutputFileList.Add($"oid,{Hash(targetName)},{fileName}");
                
                targetName = $"{fileName}.oidex";
                GlobalNameList.Add(new RDB_Names { Hash = Hash(targetName), Name = targetName });
                OutputFileList.Add($"oidex,{Hash(targetName)},{fileName}");
                
                targetName = $"{fileName}.swg";
                GlobalNameList.Add(new RDB_Names { Hash = Hash(targetName), Name = targetName });
                OutputFileList.Add($"swg,{Hash(targetName)},{fileName}");
                
                targetName = $"{fileName}.rigbin";
                GlobalNameList.Add(new RDB_Names { Hash = Hash(targetName), Name = targetName });
                OutputFileList.Add($"rigbin,{Hash(targetName)},{fileName}");

            }
            else if (typeInfoType == "TypeInfo::Object::DopeSheet::Sound")
            {
                targetName = $"{fileName}.srsa";
                GlobalNameList.Add(new RDB_Names { Hash = Hash(targetName), Name = targetName });
                OutputFileList.Add($"srsa,{Hash(targetName)},{fileName}");

                targetName = $"{fileName}.srst";
                GlobalNameList.Add(new RDB_Names { Hash = Hash(targetName), Name = targetName });
                OutputFileList.Add($"srst,{Hash(targetName)},{fileName}");
            }
            else if (typeInfoType == "TypeInfo::Object::Animation::Data::Model::G1A")
            {
                targetName = $"{fileName}.g1a";
                GlobalNameList.Add(new RDB_Names { Hash = Hash(targetName), Name = targetName });
                OutputFileList.Add($"g1a,{Hash(targetName)},{fileName}");
            }
            else if (typeInfoType == "TypeInfo::Object::Render::Texture::Static")
            {
                string truncatedString = fileName.IndexOf("MPR") != -1 ? fileName.Substring(fileName.IndexOf("MPR")) : fileName;

                targetName = $"{fileName}.g1t";
                GlobalNameList.Add(new RDB_Names { Hash = Hash(targetName), Name = targetName });
                OutputFileList.Add($"g1t,{Hash(targetName)},{fileName}");

                if (fileName.Contains("MPR"))
                {
                    targetName = $"{truncatedString}.g1t";
                    GlobalNameList.Add(new RDB_Names { Hash = Hash(targetName), Name = targetName });
                    OutputFileList.Add($"g1t,{Hash(targetName)},{truncatedString}");
                }
            }

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

        static string ReadNullTerminatedString(BinaryReader reader) // why is this not already a thing, hello?
        {
            using (MemoryStream ms = new MemoryStream())
            {
                byte currentByte;
                while ((currentByte = reader.ReadByte()) != 0)
                {
                    ms.WriteByte(currentByte);
                }

                // Convert the collected bytes to a UTF-8 string
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }
}
