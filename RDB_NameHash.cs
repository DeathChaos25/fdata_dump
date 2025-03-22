using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace fdata_dump
{
    public class RDB_NameHash
    {
        public static string Hash(string file)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            // if (Regex.IsMatch(fileName, "[a-fA-F0-9]") && fileName.Length == 8)
            //     return $"0x{fileName}.file";

            string ext = $"R_{Path.GetExtension(file).ToUpper().Replace(".", "")}";
            byte[] HASH_PREFIX = { 0xEF, 0xBC, 0xBB };
            byte[] HASH_SUFFIX = { 0xEF, 0xBC, 0xBD };
            byte HASH_KEY = 0x1F;
            // R_<ext><prefix><fileName><suffix>
            byte[] hash = Encoding.UTF8.GetBytes(ext).Concat(HASH_PREFIX).Concat(Encoding.UTF8.GetBytes(fileName)).Concat(HASH_SUFFIX).ToArray();

            int iv = hash[0] * HASH_KEY;
            int key = HASH_KEY;
            byte[] text = SliceArray(hash, 1, hash.Length);
            unchecked
            {
                foreach (var ch in text)
                {
                    var state = key;
                    key *= HASH_KEY;
                    iv += HASH_KEY * state * (sbyte)ch;
                }
            }
            // Convert hash to byte string and switch endianness
            byte[] ivBytes = BitConverter.GetBytes((uint)iv);
            Array.Reverse(ivBytes);
            return $"{BitConverter.ToString(ivBytes).ToLower().Replace("-", "")}";
        }

        private static byte[] SliceArray(byte[] source, int start, int end)
        {
            int length = end - start;
            byte[] dest = new byte[length];
            Array.Copy(source, start, dest, 0, length);
            return dest;
        }

        public static string RemoveHashSuffixPrefix(string inputString)
        {
            byte[] HASH_PREFIX = { 0xEF, 0xBC, 0xBB };
            byte[] HASH_SUFFIX = { 0xEF, 0xBC, 0xBD };

            string prefix = Encoding.UTF8.GetString(HASH_PREFIX);
            string suffix = Encoding.UTF8.GetString(HASH_SUFFIX);

            int prefixIndex = inputString.IndexOf(prefix);
            int suffixIndex = inputString.IndexOf(suffix);

            if (prefixIndex == -1 || suffixIndex == -1)
            {
                return inputString;
            }

            prefixIndex += prefix.Length;

            return inputString.Substring(prefixIndex, suffixIndex - prefixIndex);
        }
    }
}
