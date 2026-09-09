using FluentAssertions;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Commands;
using MeowsCut.Core.Editing.Timeline;
using static MeowsCut.Core.Tests.Editing.Fake;

namespace MeowsCut.Core.Tests.Editing;

public class ProjectTests
{
    [Fact]
    public void Project_from_media_has_one_source_and_one_clip()
    {
        var project = Project.FromMedia(Info(seconds: 30));

        project.Sources.Should().HaveCount(1);
        project.Sequence.ClipCount.Should().Be(1);
        project.Sequence.Duration.Should().Be(S(30));
    }

    [Fact]
    public void Adding_the_same_file_twice_reuses_the_source()
    {
        var project = Project.FromMedia(Info(path: @"C:\видео\клип.mp4"));

        var (updated, source) = project.WithSource(Info(path: @"C:\видео\клип.mp4"));

        updated.Sources.Should().HaveCount(1);
        source.Id.Should().Be(project.Sources[0].Id);
    }

    [Fact]
    public void Adding_another_file_adds_a_second_source()
    {
        var project = Project.FromMedia(Info(path: @"C:\видео\первый.mp4"));

        var (updated, source) = project.WithSource(Info(path: @"C:\видео\второй.mkv"));

        updated.Sources.Should().HaveCount(2);
        updated.Find(source.Id).Should().NotBeNull();
    }

    [Fact]
    public void Used_sources_lists_only_files_present_on_the_timeline()
    {
        var project = Project.FromMedia(Info(path: @"C:\видео\первый.mp4"));
        var (withSecond, second) = project.WithSource(Info(seconds: 5, path: @"C:\видео\второй.mkv"));

        withSecond.UsedSources().Should().HaveCount(1, "второй файл добавлен, но на дорожке его нет");

        var sequence = new AppendClipCommand(Clip.FromSource(second)).Apply(withSecond.Sequence);
        var final = withSecond.WithSequence(sequence);

        final.UsedSources().Should().HaveCount(2);
    }

    [Fact]
    public void Used_sources_does_not_duplicate_a_file_cut_into_pieces()
    {
        var project = Project.FromMedia(Info(seconds: 30));
        var sequence = new SplitClipCommand(S(10)).Apply(project.Sequence);

        project.WithSequence(sequence).UsedSources().Should().HaveCount(1);
    }

    [Fact]
    public void Unknown_source_is_rejected()
    {
        var project = Project.FromMedia(Info());

        var act = () => project.Require(SourceId.New());

        act.Should().Throw<EditOperationException>();
    }
}
