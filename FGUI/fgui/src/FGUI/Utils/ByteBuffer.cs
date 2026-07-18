#if CLIENT
using System.Drawing;
using System.Text;

namespace FairyGUI.Utils;

public class ByteBuffer
{
    private static bool _readSInvalidFFFFLogged;
    public bool LittleEndian { get; set; }
    public string[]? StringTable { get; set; }
    public int Version { get; set; }

    private int _pointer;
    private readonly int _offset;
    private readonly int _length;
    private readonly byte[] _data;

    public ByteBuffer(byte[] data, int offset = 0, int length = -1)
    {
        _data = data;
        _pointer = 0;
        _offset = offset;
        _length = length < 0 ? data.Length - offset : length;
        LittleEndian = false;
    }

    public int Position
    {
        get => _pointer;
        set => _pointer = value;
    }

    public int Length => _length;
    public bool BytesAvailable => _pointer < _length;
    public byte[] Buffer => _data;

    public int Skip(int count)
    {
        _pointer += count;
        return _pointer;
    }

    public byte ReadByte() => _data[_offset + _pointer++];

    public byte[] ReadBytes(byte[] output, int destIndex, int count)
    {
        if (count > _length - _pointer)
            throw new ArgumentOutOfRangeException(nameof(count));
        Array.Copy(_data, _offset + _pointer, output, destIndex, count);
        _pointer += count;
        return output;
    }

    public byte[] ReadBytes(int count)
    {
        if (count > _length - _pointer)
            throw new ArgumentOutOfRangeException(nameof(count));
        byte[] result = new byte[count];
        Array.Copy(_data, _offset + _pointer, result, 0, count);
        _pointer += count;
        return result;
    }

    public ByteBuffer ReadBuffer()
    {
        int count = ReadInt();
        ByteBuffer ba = new ByteBuffer(_data, _offset + _pointer, count)
        {
            StringTable = StringTable,
            Version = Version
        };
        _pointer += count;
        return ba;
    }

    public char ReadChar() => (char)ReadShort();
    public bool ReadBool() { bool result = _data[_offset + _pointer] == 1; _pointer++; return result; }

    public short ReadShort()
    {
        if (_pointer + 2 > _length)
            throw new IndexOutOfRangeException($"ReadShort: position={_pointer}, length={_length}");
        int startIndex = _offset + _pointer;
        _pointer += 2;
        if (LittleEndian)
            return (short)(_data[startIndex] | (_data[startIndex + 1] << 8));
        else
            return (short)((_data[startIndex] << 8) | _data[startIndex + 1]);
    }

    public ushort ReadUshort() => (ushort)ReadShort();

    public int ReadInt()
    {
        int startIndex = _offset + _pointer;
        _pointer += 4;
        if (LittleEndian)
            return _data[startIndex] | (_data[startIndex + 1] << 8) | (_data[startIndex + 2] << 16) | (_data[startIndex + 3] << 24);
        else
            return (_data[startIndex] << 24) | (_data[startIndex + 1] << 16) | (_data[startIndex + 2] << 8) | _data[startIndex + 3];
    }

    public uint ReadUint() => (uint)ReadInt();

    public float ReadFloat()
    {
        int startIndex = _offset + _pointer;
        _pointer += 4;
        byte[] temp = new byte[4];
        if (LittleEndian == BitConverter.IsLittleEndian)
            return BitConverter.ToSingle(_data, startIndex);
        temp[3] = _data[startIndex];
        temp[2] = _data[startIndex + 1];
        temp[1] = _data[startIndex + 2];
        temp[0] = _data[startIndex + 3];
        return BitConverter.ToSingle(temp, 0);
    }

    public long ReadLong()
    {
        int startIndex = _offset + _pointer;
        _pointer += 8;
        if (LittleEndian)
        {
            int i1 = _data[startIndex] | (_data[startIndex + 1] << 8) | (_data[startIndex + 2] << 16) | (_data[startIndex + 3] << 24);
            int i2 = _data[startIndex + 4] | (_data[startIndex + 5] << 8) | (_data[startIndex + 6] << 16) | (_data[startIndex + 7] << 24);
            return (uint)i1 | ((long)i2 << 32);
        }
        else
        {
            int i1 = (_data[startIndex] << 24) | (_data[startIndex + 1] << 16) | (_data[startIndex + 2] << 8) | _data[startIndex + 3];
            int i2 = (_data[startIndex + 4] << 24) | (_data[startIndex + 5] << 16) | (_data[startIndex + 6] << 8) | _data[startIndex + 7];
            return (uint)i2 | ((long)i1 << 32);
        }
    }

    public double ReadDouble()
    {
        int startIndex = _offset + _pointer;
        _pointer += 8;
        if (LittleEndian == BitConverter.IsLittleEndian)
            return BitConverter.ToDouble(_data, startIndex);
        byte[] temp = new byte[8];
        for (int i = 0; i < 8; i++)
            temp[7 - i] = _data[startIndex + i];
        return BitConverter.ToDouble(temp, 0);
    }

    public string ReadString()
    {
        ushort len = ReadUshort();
        string result = Encoding.UTF8.GetString(_data, _offset + _pointer, len);
        _pointer += len;
        return result;
    }

    public string ReadString(int len)
    {
        string result = Encoding.UTF8.GetString(_data, _offset + _pointer, len);
        _pointer += len;
        return result;
    }

    public string? ReadS()
    {
        if (_pointer + 2 > _length)
            throw new IndexOutOfRangeException($"ReadS: position={_pointer}, length={_length}");
        int index = ReadUshort();
        if (index == 65535)
        {
            // Compatibility: some exports may emit 0xFFFF for optional "no string" fields.
            if (!_readSInvalidFFFFLogged)
            {
                _readSInvalidFFFFLogged = true;
                Game.Logger.LogWarning("[FGUI][ByteBuffer] ReadS got index=65535(0xFFFF), treat as null.");
            }
            return null;
        }
        if (index == 65534) return null;
        if (index == 65533) return string.Empty;
        if (StringTable == null || index >= StringTable.Length)
            throw new IndexOutOfRangeException($"ReadS: StringTable index={index}, StringTable.Length={StringTable?.Length ?? 0}");
        return StringTable[index];
    }

    public string?[] ReadSArray(int cnt)
    {
        string?[] ret = new string?[cnt];
        for (int i = 0; i < cnt; i++)
            ret[i] = ReadS();
        return ret;
    }

    public void WriteS(string value)
    {
        int index = ReadUshort();
        if (index != 65535 && index != 65534 && index != 65533 && StringTable != null)
            StringTable[index] = value;
    }

    public Color ReadColor()
    {
        int startIndex = _offset + _pointer;
        byte r = _data[startIndex];
        byte g = _data[startIndex + 1];
        byte b = _data[startIndex + 2];
        byte a = _data[startIndex + 3];
        _pointer += 4;
        return Color.FromArgb(a, r, g, b);
    }

    public bool Seek(int indexTablePos, int blockIndex)
    {
        int tmp = _pointer;
        _pointer = indexTablePos;
        
        // Bounds check
        if (_pointer >= _length) { _pointer = tmp; return false; }
        
        int segCount = _data[_offset + _pointer++];
        if (blockIndex < segCount)
        {
            if (_pointer >= _length) { _pointer = tmp; return false; }
            bool useShort = _data[_offset + _pointer++] == 1;
            int newPos;
            if (useShort)
            {
                _pointer += 2 * blockIndex;
                if (_pointer + 2 > _length) { _pointer = tmp; return false; }
                newPos = ReadShort();
            }
            else
            {
                _pointer += 4 * blockIndex;
                if (_pointer + 4 > _length) { _pointer = tmp; return false; }
                newPos = ReadInt();
            }
            if (newPos > 0)
            {
                _pointer = indexTablePos + newPos;
                if (_pointer > _length) { _pointer = tmp; return false; }
                return true;
            }
            _pointer = tmp;
            return false;
        }
        _pointer = tmp;
        return false;
    }
}
#endif

