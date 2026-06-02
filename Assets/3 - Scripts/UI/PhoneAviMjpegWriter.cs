using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Minimal AVI Motion-JPEG writer used by the phone camera's video mode.
/// Streams JPEG frames directly into an .avi container — Unity's built-in
/// ImageConversion produces the JPEG bytes, we wrap them in the standard
/// AVI chunks here. Output plays natively in VLC, Windows Media Player,
/// browsers, etc., with no external dependencies (no FFmpeg, no Asset Store).
///
/// Pattern: WriteHeader() reserves placeholder sizes/frame-counts at the top
/// of the file; WriteFrame() appends each frame and records its offset+size
/// for the index; Close() writes the idx1 chunk and seeks back to patch the
/// reserved placeholders. Standard AVI authoring flow — large open-source
/// reference implementations follow the same shape.
/// </summary>
public class PhoneAviMjpegWriter
{
    FileStream     _fs;
    BinaryWriter   _bw;
    readonly int   _width, _height, _fps;
    int            _frameCount;
    readonly List<uint> _frameOffsets = new List<uint>();
    readonly List<uint> _frameSizes   = new List<uint>();

    long _riffSizePos;
    long _mainHeaderFramesPos;
    long _streamHeaderFramesPos;
    long _moviSizePos;
    long _moviStartPos;

    public bool IsOpen => _fs != null;

    public PhoneAviMjpegWriter(string path, int width, int height, int fps)
    {
        _width  = width;
        _height = height;
        _fps    = fps;
        _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        _bw = new BinaryWriter(_fs);
        WriteHeader();
    }

    void WriteFourCC(string s) => _bw.Write(Encoding.ASCII.GetBytes(s));

    void WriteHeader()
    {
        // RIFF AVI header
        WriteFourCC("RIFF");
        _riffSizePos = _fs.Position;
        _bw.Write((uint)0);                          // RIFF size — patched in Close()
        WriteFourCC("AVI ");

        // LIST 'hdrl' — header list containing avih + strl(strh+strf)
        WriteFourCC("LIST");
        // hdrl size = 4 ("hdrl") + avih chunk(8+56) + strl list(8+4 + strh chunk(8+56) + strf chunk(8+40))
        _bw.Write((uint)(4 + 8 + 56 + 8 + 4 + 8 + 56 + 8 + 40));
        WriteFourCC("hdrl");

        // avih chunk (main AVI header)
        WriteFourCC("avih");
        _bw.Write((uint)56);
        _bw.Write((uint)(1000000 / _fps));           // microseconds per frame
        _bw.Write((uint)(_width * _height * 3 * _fps)); // max bytes/sec (rough upper bound)
        _bw.Write((uint)0);                          // padding granularity
        _bw.Write((uint)0x00000010);                 // flags = AVIF_HASINDEX
        _mainHeaderFramesPos = _fs.Position;
        _bw.Write((uint)0);                          // total frames — patched
        _bw.Write((uint)0);                          // initial frames
        _bw.Write((uint)1);                          // streams
        _bw.Write((uint)(_width * _height * 3));     // suggested buffer size
        _bw.Write((uint)_width);
        _bw.Write((uint)_height);
        _bw.Write((uint)0); _bw.Write((uint)0); _bw.Write((uint)0); _bw.Write((uint)0); // reserved

        // LIST 'strl' — stream list containing strh + strf
        WriteFourCC("LIST");
        _bw.Write((uint)(4 + 8 + 56 + 8 + 40));
        WriteFourCC("strl");

        // strh chunk (stream header)
        WriteFourCC("strh");
        _bw.Write((uint)56);
        WriteFourCC("vids");
        WriteFourCC("MJPG");
        _bw.Write((uint)0);                          // flags
        _bw.Write((ushort)0);                        // priority
        _bw.Write((ushort)0);                        // language
        _bw.Write((uint)0);                          // initial frames
        _bw.Write((uint)1);                          // scale
        _bw.Write((uint)_fps);                       // rate (rate/scale = fps)
        _bw.Write((uint)0);                          // start
        _streamHeaderFramesPos = _fs.Position;
        _bw.Write((uint)0);                          // length — patched
        _bw.Write((uint)(_width * _height * 3));     // suggested buffer size
        _bw.Write((uint)0xFFFFFFFF);                 // quality (-1 = default)
        _bw.Write((uint)0);                          // sample size
        _bw.Write((short)0); _bw.Write((short)0);    // rect: left, top
        _bw.Write((short)_width); _bw.Write((short)_height); // rect: right, bottom

        // strf chunk (BITMAPINFOHEADER stream format)
        WriteFourCC("strf");
        _bw.Write((uint)40);
        _bw.Write((uint)40);                         // biSize
        _bw.Write(_width);                           // biWidth
        _bw.Write(_height);                          // biHeight
        _bw.Write((ushort)1);                        // biPlanes
        _bw.Write((ushort)24);                       // biBitCount
        WriteFourCC("MJPG");                         // biCompression
        _bw.Write((uint)(_width * _height * 3));     // biSizeImage
        _bw.Write(0); _bw.Write(0);                  // bi[X|Y]PelsPerMeter
        _bw.Write((uint)0); _bw.Write((uint)0);      // biClr[Used|Important]

        // LIST 'movi' — movie data, frames go inside here as 00dc chunks
        WriteFourCC("LIST");
        _moviSizePos = _fs.Position;
        _bw.Write((uint)0);                          // movi size — patched
        _moviStartPos = _fs.Position;
        WriteFourCC("movi");
    }

    public void WriteFrame(byte[] jpegData)
    {
        if (jpegData == null || jpegData.Length == 0 || _fs == null) return;
        uint relOffset = (uint)(_fs.Position - _moviStartPos);
        _frameOffsets.Add(relOffset);
        _frameSizes.Add((uint)jpegData.Length);

        WriteFourCC("00dc");
        _bw.Write((uint)jpegData.Length);
        _bw.Write(jpegData);
        if ((jpegData.Length & 1) == 1) _bw.Write((byte)0); // 2-byte alignment padding
        _frameCount++;
    }

    public void Close()
    {
        if (_fs == null) return;

        // Patch movi list size now that we know it.
        long moviEnd = _fs.Position;
        uint moviSize = (uint)(moviEnd - _moviStartPos);
        _fs.Position = _moviSizePos;
        _bw.Write(moviSize);

        // Write idx1 — index of every frame's offset+size relative to movi list.
        _fs.Position = moviEnd;
        WriteFourCC("idx1");
        _bw.Write((uint)(_frameCount * 16));
        for (int i = 0; i < _frameCount; i++)
        {
            WriteFourCC("00dc");
            _bw.Write((uint)0x00000010);             // AVIIF_KEYFRAME
            _bw.Write(_frameOffsets[i]);
            _bw.Write(_frameSizes[i]);
        }

        // Patch the placeholders at the top.
        long fileEnd = _fs.Position;
        _fs.Position = _riffSizePos;
        _bw.Write((uint)(fileEnd - 8));
        _fs.Position = _mainHeaderFramesPos;
        _bw.Write((uint)_frameCount);
        _fs.Position = _streamHeaderFramesPos;
        _bw.Write((uint)_frameCount);

        _bw.Flush();
        _bw.Close();
        _fs = null;
        _bw = null;
    }

    public int FrameCount => _frameCount;
}
