using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace fdata_dump
{
    public class RDB
    {
        public static bool DEBUG = false;
        public struct RdbHeader
        {
            public uint Magic;
            public uint Version;
            public uint HeaderSize;
            public uint SystemId;
            public uint FileCount;
            public uint Ktid;
            public string Path;

            public static RdbHeader Read(BinaryReader reader)
            {
                RdbHeader header = new RdbHeader
                {
                    Magic = reader.ReadUInt32(),
                    Version = reader.ReadUInt32(),
                    HeaderSize = reader.ReadUInt32(),
                    SystemId = reader.ReadUInt32(),
                    FileCount = reader.ReadUInt32(),
                    Ktid = reader.ReadUInt32()
                };

                List<byte> pathBytes = new List<byte>();
                byte b;
                while ((b = reader.ReadByte()) != 0)
                {
                    pathBytes.Add(b);
                }
                header.Path = Encoding.UTF8.GetString(pathBytes.ToArray());

                if (header.Version != 0x30303030)
                {
                    throw new InvalidDataException("Invalid RDB version.");
                }

                return header;
            }
        }

        // RdbFlags structure (bit fields)
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        public struct RdbFlags
        {
            [System.Runtime.InteropServices.FieldOffset(0)]
            public uint Value;

            public bool Flag1 => (Value & 0x1) != 0;
            public bool Flag2 => (Value & 0x2) != 0;
            public bool Flag3 => (Value & 0x4) != 0;
        }

       public class RdbEntry
        {
            public uint Magic;
            public uint Version;
            public ulong EntrySize;
            public ulong CompSize;
            public ulong FileSize;
            public uint EntryType;
            public uint FileKtid;
            public uint TypeInfoKtid;
            public RdbFlags Flags;
            public byte[] UnkContent;
            public string Name;

            public static RdbEntry Read(BinaryReader reader)
            {
                if (DEBUG) Console.WriteLine($"Reading RDB Entry at 0x{reader.BaseStream.Position:X8}");
                RdbEntry entry = new RdbEntry
                {
                    Magic = reader.ReadUInt32(),
                    Version = reader.ReadUInt32(),
                    EntrySize = reader.ReadUInt64(),
                    CompSize = reader.ReadUInt64(),
                    FileSize = reader.ReadUInt64(),
                    EntryType = reader.ReadUInt32(),
                    FileKtid = reader.ReadUInt32(),
                    TypeInfoKtid = reader.ReadUInt32(),
                    Flags = new RdbFlags { Value = reader.ReadUInt32() }
                };

                if (DEBUG)
                {
                    Console.WriteLine($"RDB Entry EntrySize 0x{entry.EntrySize:X8}");
                    Console.WriteLine($"RDB Entry CompSize 0x{entry.CompSize:X8}");
                    Console.WriteLine($"RDB Entry FileSize 0x{entry.FileSize:X8}");
                    Console.WriteLine($"RDB Entry EntryType 0x{entry.EntryType:X8}");
                    Console.WriteLine($"RDB Entry FileKtid 0x{entry.FileKtid:X8}");
                    Console.WriteLine($"RDB Entry TypeInfoKtid 0x{entry.TypeInfoKtid:X8}");
                }

                if (entry.Version != 0x30303030)
                {
                    throw new InvalidDataException("Invalid RDB entry version.");
                }

                int unkContentSize = (int)(entry.EntrySize - entry.CompSize - 0x30);
                entry.UnkContent = reader.ReadBytes(unkContentSize);

                if (DEBUG) Console.WriteLine($"RDB Current Offset ------------- 0x{reader.BaseStream.Position:X8}");

                /*byte[] nameBytes = reader.ReadBytes((int)entry.StringSize);
                entry.Name = Encoding.UTF8.GetString(nameBytes);

                while (reader.BaseStream.Position % 4 != 0)
                {
                    reader.ReadByte();
                }*/

                return entry;
            }
        }

        public static RdbHeader ReadHeader(BinaryReader reader)
        {
            return RdbHeader.Read(reader);
        }

        public static RdbEntry ReadEntry(BinaryReader reader)
        {
            return RdbEntry.Read(reader);
        }
    }
}
