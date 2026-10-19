using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using MeowsCut.App.Timeline;

namespace MeowsCut.App.Tests.Timeline;

/// <summary>
/// Волна на доске рисуется столбиками по пикам, снятым с картинки showwavespic.
/// </summary>
public class WaveformPeaksTests
{
    [Fact]
    public void Peak_is_the_height_of_the_painted_column()
    {
        // Три столбца: тишина, половина высоты и во всю высоту картинки.
        var peaks = WaveformPeaks.FromImage(Picture(height: 20, (0, 0), (5, 15), (0, 20)));

        peaks.Count.Should().Be(3);
        peaks.Max(0, 0.33).Should().Be(0);
        peaks.Max(0.34, 0.66).Should().BeApproximately(0.5, 0.01);
        peaks.Max(0.67, 1).Should().Be(1);
    }

    [Fact]
    public void Short_loud_hit_survives_a_squeezed_board()
    {
        // На сжатой доске один столбик закрывает много столбцов файла —
        // берётся самый громкий, иначе короткий удар пропал бы.
        var peaks = new WaveformPeaks([0.1f, 0.1f, 0.9f, 0.1f]);

        peaks.Max(0, 1).Should().BeApproximately(0.9, 0.001);
    }

    [Fact]
    public void Slice_reads_only_its_own_part_of_the_file()
    {
        // Кусок со второй половины файла: громкое начало файла в нём не видно.
        var slice = new WaveformSlice(new WaveformPeaks([1f, 1f, 0.2f, 0.4f]), 0.5, 1);

        slice.Max(0, 0.5).Should().BeApproximately(0.2, 0.001);
        slice.Max(0.5, 1).Should().BeApproximately(0.4, 0.001);
    }

    /// <summary>Картинка, где в каждом столбце закрашены строки от и до (не включая).</summary>
    private static BitmapSource Picture(int height, params (int From, int To)[] columns)
    {
        var width = columns.Length;
        var pixels = new byte[width * height * 4];

        for (var x = 0; x < width; x++)
        {
            for (var y = columns[x].From; y < columns[x].To; y++)
            {
                var at = ((y * width) + x) * 4;
                pixels[at] = pixels[at + 1] = pixels[at + 2] = pixels[at + 3] = 255;
            }
        }

        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    }
}
