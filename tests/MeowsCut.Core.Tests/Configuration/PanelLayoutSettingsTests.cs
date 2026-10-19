using FluentAssertions;
using MeowsCut.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeowsCut.Core.Tests.Configuration;

/// <summary>Раскладка окна переживает перезапуск.</summary>
public sealed class PanelLayoutSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meows-settings-" + Guid.NewGuid().ToString("N"));

    private JsonAppSettingsStore Store() =>
        new(new AppPaths(_root, _root, _root), NullLogger<JsonAppSettingsStore>.Instance);

    [Fact]
    public async Task Sizes_come_back_after_restart()
    {
        var layout = new PanelLayout(410, 380, 0.42, 1500, 900, Maximized: true, MediaWidth: 300, SoundListHeight: 240);
        await Store().SaveAsync(AppSettings.Default with { Layout = layout }, CancellationToken.None);

        var loaded = await Store().LoadAsync(CancellationToken.None);

        loaded.Layout.Should().Be(layout);
    }

    [Fact]
    public async Task Old_settings_start_with_the_factory_layout()
    {
        await Store().SaveAsync(AppSettings.Default, CancellationToken.None);

        var loaded = await Store().LoadAsync(CancellationToken.None);

        loaded.Layout.Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
