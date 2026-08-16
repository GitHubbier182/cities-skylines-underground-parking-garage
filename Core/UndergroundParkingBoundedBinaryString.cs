using System;
using System.IO;
using System.Text;

namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingBoundedBinaryString
    {
        internal const int MaximumCharacters = 512;
        private const int MaximumUtf8Bytes = MaximumCharacters * 3;
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static string Read(BinaryReader reader, string fieldName)
        {
            if (reader == null)
                throw new ArgumentNullException("reader");

            int byteCount = Read7BitEncodedByteCount(reader, fieldName);
            byte[] bytes = reader.ReadBytes(byteCount);
            if (bytes.Length != byteCount)
                throw new InvalidDataException(fieldName + " is truncated.");

            string value;
            try
            {
                value = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                throw new InvalidDataException(fieldName + " contains malformed UTF-8.");
            }
            if (value.Length > MaximumCharacters)
                throw new InvalidDataException(fieldName + " exceeds the supported character limit.");
            return value;
        }

        public static void ValidateForWrite(string value, string fieldName)
        {
            if (value == null)
                throw new InvalidOperationException(fieldName + " is required.");
            if (value.Length > MaximumCharacters)
                throw new InvalidOperationException(fieldName + " exceeds the supported character limit.");

            int byteCount;
            try
            {
                byteCount = StrictUtf8.GetByteCount(value);
            }
            catch (EncoderFallbackException)
            {
                throw new InvalidOperationException(fieldName + " contains malformed UTF-16.");
            }
            if (byteCount > MaximumUtf8Bytes)
                throw new InvalidOperationException(fieldName + " exceeds the supported encoded limit.");
        }

        private static int Read7BitEncodedByteCount(BinaryReader reader, string fieldName)
        {
            uint value = 0u;
            for (int index = 0; index < 5; index++)
            {
                byte current;
                try
                {
                    current = reader.ReadByte();
                }
                catch (EndOfStreamException)
                {
                    throw new InvalidDataException(fieldName + " has a truncated length prefix.");
                }

                uint payload = (uint)(current & 0x7f);
                if (index == 4 && (current & 0xf0) != 0)
                    throw new InvalidDataException(fieldName + " has a malformed length prefix.");
                value |= payload << (index * 7);
                if ((current & 0x80) == 0)
                {
                    if (index > 0 && payload == 0u)
                        throw new InvalidDataException(fieldName + " has an overlong length prefix.");
                    if (value > MaximumUtf8Bytes)
                        throw new InvalidDataException(fieldName + " exceeds the supported encoded limit.");
                    return (int)value;
                }
            }

            throw new InvalidDataException(fieldName + " has a malformed length prefix.");
        }
    }
}
