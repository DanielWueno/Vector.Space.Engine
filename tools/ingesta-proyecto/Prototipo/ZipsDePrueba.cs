using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace IngestaProyecto.Prototipo;

/// <summary>Zips maliciosos fabricados para la prueba; ninguno sale de la carpeta del banco.</summary>
public static class ZipsDePrueba
{
    public const string TextoEscape = "si este archivo existe fuera de la carpeta de extraccion, la extraccion escapo (1.7.3)";

    /// <summary>Entradas que intentan escapar (Zip Slip) mezcladas con una legitima.</summary>
    public static void Slip(string ruta)
    {
        using var f = File.Create(ruta);
        using var z = new ZipArchive(f, ZipArchiveMode.Create);
        Escribir(z, "docs/legitimo.md", "# Legitimo\n\nUn documento normal con texto suficiente para indexarse en la coleccion de prueba.\n");
        Escribir(z, "../../escape-slip-1.md", TextoEscape);
        Escribir(z, "docs/../../../escape-slip-2.md", TextoEscape);
        Escribir(z, "..\\..\\escape-slip-3.md", TextoEscape);
        Escribir(z, "/tmp/escape-slip-4.md", TextoEscape);
        Escribir(z, "C:/escape-slip-5.md", TextoEscape);
        Escribir(z, "docs/archivo.md:flujo-alterno", TextoEscape);
    }

    /// <summary>Una entrada de <paramref name="bytes"/> ceros: pocos KB comprimidos, cientos de MB descomprimidos.</summary>
    public static void Bomba(string ruta, long bytes)
    {
        using var f = File.Create(ruta);
        using var z = new ZipArchive(f, ZipArchiveMode.Create);
        Escribir(z, "README.md", "# Bomba\n\nEl archivo de al lado descomprime a cientos de megabytes de ceros sin aportar nada.\n");
        var e = z.CreateEntry("src/ceros.cs", CompressionLevel.SmallestSize);
        using var s = e.Open();
        var bloque = new byte[1 << 20];
        for (long escrito = 0; escrito < bytes; escrito += bloque.Length) s.Write(bloque, 0, (int)Math.Min(bloque.Length, bytes - escrito));
    }

    /// <summary>
    /// <paramref name="entradas"/> entradas .md de <paramref name="bytesPorEntrada"/> ceros cada una: cada una cabe bajo el
    /// tope por archivo del perfil, pero juntas descomprimen a mucho mas que el zip.
    /// </summary>
    public static void BombaDeEntradas(string ruta, int entradas, int bytesPorEntrada)
    {
        using var f = File.Create(ruta);
        using var z = new ZipArchive(f, ZipArchiveMode.Create);
        var ceros = new byte[bytesPorEntrada];
        for (var i = 0; i < entradas; i++)
        {
            var e = z.CreateEntry($"docs/ceros-{i}.md", CompressionLevel.SmallestSize);
            using var s = e.Open();
            s.Write(ceros);
        }
    }

    /// <summary>
    /// Una entrada de <paramref name="bytesReales"/> cuyas cabeceras (local y central) declaran 100 bytes: el tope
    /// tiene que contar lo que sale del inflador, no lo que dice la cabecera.
    /// </summary>
    public static void Mentirosa(string ruta, long bytesReales)
    {
        using (var f = File.Create(ruta))
        using (var z = new ZipArchive(f, ZipArchiveMode.Create))
        {
            var e = z.CreateEntry("src/miente.cs", CompressionLevel.SmallestSize);
            using var s = e.Open();
            var linea = Encoding.ASCII.GetBytes("// linea repetida para que comprima mucho y descomprima a mas de lo declarado\n");
            for (long escrito = 0; escrito < bytesReales; escrito += linea.Length) s.Write(linea);
        }

        var datos = File.ReadAllBytes(ruta);
        for (var i = 0; i + 4 <= datos.Length; i++)
        {
            var firma = BinaryPrimitives.ReadUInt32LittleEndian(datos.AsSpan(i));
            if (firma == 0x04034b50) BinaryPrimitives.WriteUInt32LittleEndian(datos.AsSpan(i + 22), 100); // local
            else if (firma == 0x02014b50) BinaryPrimitives.WriteUInt32LittleEndian(datos.AsSpan(i + 24), 100); // central
        }
        File.WriteAllBytes(ruta, datos);
    }

    /// <summary>Muchas entradas minimas: agota el limite de entradas sin descomprimir casi nada.</summary>
    public static void MuchasEntradas(string ruta, int n)
    {
        using var f = File.Create(ruta);
        using var z = new ZipArchive(f, ZipArchiveMode.Create);
        for (var i = 0; i < n; i++) z.CreateEntry($"d{i % 100}/f{i}.txt", CompressionLevel.NoCompression);
    }

    /// <summary>Una entrada marcada como enlace simbolico Unix (S_IFLNK) que apunta fuera.</summary>
    public static void Enlace(string ruta)
    {
        using var f = File.Create(ruta);
        using var z = new ZipArchive(f, ZipArchiveMode.Create);
        Escribir(z, "docs/legitimo.md", "# Legitimo\n\nUn documento normal con texto suficiente para indexarse en la coleccion de prueba.\n");
        var e = z.CreateEntry("docs/enlace.md");
        e.ExternalAttributes = unchecked((int)(0xA1FFu << 16)); // lrwxrwxrwx
        using var s = e.Open();
        s.Write(Encoding.UTF8.GetBytes("../../../../../../etc/hostname"));
    }

    private static void Escribir(ZipArchive z, string nombre, string texto)
    {
        var e = z.CreateEntry(nombre);
        using var s = e.Open();
        s.Write(Encoding.UTF8.GetBytes(texto));
    }
}
