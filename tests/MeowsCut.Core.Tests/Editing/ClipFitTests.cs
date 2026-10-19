using FluentAssertions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Export;
using MeowsCut.Core.Projects;

namespace MeowsCut.Core.Tests.Editing;

/// <summary>Своя подгонка кадра у отдельного куска.</summary>
public class ClipFitTests
{
    private static Project WithFit(FitMode? fit)
    {
        var project = Project.FromMedia(Fake.Info(10));
        var clip = project.Sequence.Video.Clips[0];
        var fitted = clip.WithTransform(clip.Transform with { Fit = fit });
        return project.WithSequence(project.Sequence.WithTrack(project.Sequence.Video.Replace(fitted)));
    }

    [Fact]
    public void Own_fit_makes_the_transform_non_identity()
    {
        (ClipTransform.Identity with { Fit = FitMode.Cover }).IsIdentity.Should().BeFalse(
            "иначе быстрый экспорт скопировал бы кадр без подгонки");
    }

    [Fact]
    public void Own_fit_different_from_the_project_needs_a_uniform_frame()
    {
        WithFit(FitMode.Cover).Sequence.NeedsUniformFrame.Should().BeTrue();
    }

    [Fact]
    public void Fit_equal_to_the_project_changes_nothing()
    {
        WithFit(FitMode.Contain).Sequence.HasOwnFit.Should().BeFalse("по умолчанию проект вписывает кадр");
    }

    [Fact]
    public void Own_fit_survives_the_draft()
    {
        var project = WithFit(FitMode.Cover);
        var media = project.Sources.ToDictionary(source => source.Id.Value, source => source.Info);

        var (restored, _) = ProjectDocumentReader.Restore(ProjectDocumentWriter.Create(project), media);

        restored.Sequence.Video.Clips[0].Transform.Fit.Should().Be(FitMode.Cover);
    }

    [Fact]
    public void Old_drafts_follow_the_project()
    {
        var project = WithFit(null);
        var media = project.Sources.ToDictionary(source => source.Id.Value, source => source.Info);

        var (restored, _) = ProjectDocumentReader.Restore(ProjectDocumentWriter.Create(project), media);

        restored.Sequence.Video.Clips[0].Transform.Fit.Should().BeNull();
    }
}
