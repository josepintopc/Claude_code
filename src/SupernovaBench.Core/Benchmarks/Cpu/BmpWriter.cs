namespace SupernovaBench.Core.Benchmarks.Cpu;

/// <summary>Grava pixels BGRA32 num arquivo BMP de 24 bits (sem dependências de imagem).</summary>
public static class BmpWriter
{
    public static void Save(string path, int[] bgra, int width, int height)
    {
        if (bgra.Length != width * height) throw new ArgumentException("Tamanho dos pixels não confere.", nameof(bgra));
        int rowSize = (width * 3 + 3) & ~3;
        int dataSize = rowSize * height;
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        // BITMAPFILEHEADER
        w.Write((byte)'B'); w.Write((byte)'M');
        w.Write(54 + dataSize); w.Write(0); w.Write(54);
        // BITMAPINFOHEADER
        w.Write(40); w.Write(width); w.Write(height); w.Write((short)1); w.Write((short)24);
        w.Write(0); w.Write(dataSize); w.Write(2835); w.Write(2835); w.Write(0); w.Write(0);
        var row = new byte[rowSize];
        for (int y = height - 1; y >= 0; y--) // BMP é gravado de baixo para cima
        {
            for (int x = 0; x < width; x++)
            {
                int p = bgra[y * width + x];
                row[x * 3] = (byte)p;
                row[x * 3 + 1] = (byte)(p >> 8);
                row[x * 3 + 2] = (byte)(p >> 16);
            }
            w.Write(row);
        }
    }
}
