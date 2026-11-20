// Собирает Win32-ресурсы exe (.res): иконку, манифест и информацию о версии.
// Зачем: csc сам пишет версию с языком «нейтральный» и без издателя — тогда «Автозагрузка» в диспетчере задач
// показывает «SoundFlip.exe» вместо «SoundFlip». Здесь версия пишется как у обычных программ: en-US, с издателем.
// Запуск: ResGen.exe <out.res> <app.ico> <app.manifest> <version a.b.c.d> <name> <company> <description>
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

static class ResGen
{
    const ushort Lang = 0x0409, CodePage = 1200;

    static void Main(string[] a)
    {
        string outPath = a[0], ico = a[1], manifest = a[2], version = a[3], name = a[4], company = a[5], description = a[6];
        using (var fs = File.Create(outPath))
        using (var w = new BinaryWriter(fs))
        {
            Entry(w, 0, 0, 0, new byte[0]); // обязательная пустая запись в начале .res

            // Иконка: каждое изображение — RT_ICON, плюс оглавление RT_GROUP_ICON.
            byte[] icoData = File.ReadAllBytes(ico);
            int count = BitConverter.ToUInt16(icoData, 4);
            var group = new MemoryStream();
            var gw = new BinaryWriter(group);
            gw.Write((ushort)0); gw.Write((ushort)1); gw.Write((ushort)count);
            for (int i = 0; i < count; i++)
            {
                int e = 6 + 16 * i;
                int size = BitConverter.ToInt32(icoData, e + 8), offset = BitConverter.ToInt32(icoData, e + 12);
                var img = new byte[size];
                Array.Copy(icoData, offset, img, 0, size);
                Entry(w, 3, (ushort)(i + 1), Lang, img);
                gw.Write(icoData, e, 12);          // ширина, высота, цвета, резерв, плоскости, бит/пиксель, размер
                gw.Write((ushort)(i + 1));         // id изображения
            }
            Entry(w, 14, 32512, Lang, group.ToArray());

            Entry(w, 24, 1, Lang, File.ReadAllBytes(manifest));
            Entry(w, 16, 1, Lang, VersionInfo(version, name, company, description));
        }
        Console.WriteLine("OK: " + outPath);
    }

    static void Entry(BinaryWriter w, ushort type, ushort id, ushort lang, byte[] data)
    {
        w.Write(data.Length);
        w.Write(32);                               // размер заголовка
        w.Write((ushort)0xFFFF); w.Write(type);
        w.Write((ushort)0xFFFF); w.Write(id);
        w.Write(0);                                // DataVersion
        w.Write((ushort)(type == 0 ? 0 : 0x1030)); // MOVEABLE | PURE | DISCARDABLE
        w.Write(lang);
        w.Write(0); w.Write(0);                    // Version, Characteristics
        w.Write(data);
        while (w.BaseStream.Position % 4 != 0) w.Write((byte)0);
    }

    // VS_VERSIONINFO
    static byte[] VersionInfo(string version, string name, string company, string description)
    {
        var v = Array.ConvertAll(version.Split('.'), int.Parse);
        var fixedInfo = new MemoryStream();
        var f = new BinaryWriter(fixedInfo);
        f.Write(0xFEEF04BD); f.Write(0x00010000);
        f.Write((v[0] << 16) | v[1]); f.Write((v[2] << 16) | v[3]);   // FileVersion
        f.Write((v[0] << 16) | v[1]); f.Write((v[2] << 16) | v[3]);   // ProductVersion
        f.Write(0x3F); f.Write(0);                                     // FileFlagsMask, FileFlags
        f.Write(0x00040004); f.Write(1); f.Write(0);                   // VOS_NT_WINDOWS32, VFT_APP, subtype
        f.Write(0); f.Write(0);                                        // дата

        string ver = string.Join(".", Array.ConvertAll(v, x => x.ToString()));
        var strings = new List<byte[]>();
        foreach (var kv in new[]
        {
            new[] { "CompanyName", company },
            new[] { "FileDescription", description },
            new[] { "FileVersion", ver },
            new[] { "InternalName", name + ".exe" },
            new[] { "LegalCopyright", "© " + company },
            new[] { "OriginalFilename", name + ".exe" },
            new[] { "ProductName", name },
            new[] { "ProductVersion", ver },
            new[] { "Assembly Version", ver },
        })
            strings.Add(Block(kv[0], Encoding.Unicode.GetBytes(kv[1] + "\0"), kv[1].Length + 1, 1, null));

        byte[] table = Block(Lang.ToString("X4") + CodePage.ToString("X4"), null, 0, 1, strings);
        byte[] sfi = Block("StringFileInfo", null, 0, 1, new List<byte[]> { table });
        var tr = new byte[4];
        BitConverter.GetBytes(Lang).CopyTo(tr, 0);
        BitConverter.GetBytes(CodePage).CopyTo(tr, 2);
        byte[] var = Block("Translation", tr, 4, 0, null);
        byte[] vfi = Block("VarFileInfo", null, 0, 1, new List<byte[]> { var });
        return Block("VS_VERSION_INFO", fixedInfo.ToArray(), (int)fixedInfo.Length, 0, new List<byte[]> { sfi, vfi });
    }

    // Общий вид блока: wLength, wValueLength, wType, ключ (UTF-16 + 0), выравнивание, значение, выравнивание, дочерние блоки.
    static byte[] Block(string key, byte[] value, int valueLength, ushort type, List<byte[]> children)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((ushort)0); w.Write((ushort)valueLength); w.Write(type);
        w.Write(Encoding.Unicode.GetBytes(key + "\0"));
        Pad(w);
        if (value != null) { w.Write(value); }
        if (children != null)
            foreach (var c in children) { Pad(w); w.Write(c); }
        byte[] r = ms.ToArray();
        BitConverter.GetBytes((ushort)r.Length).CopyTo(r, 0);
        return r;
    }

    static void Pad(BinaryWriter w) { while (w.BaseStream.Position % 4 != 0) w.Write((byte)0); }
}
