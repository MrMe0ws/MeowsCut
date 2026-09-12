using FluentAssertions;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Media;
using MeowsCut.Core.Projects;
using MeowsCut.Core.Tests.Editing;

namespace MeowsCut.Core.Tests.Projects;

/// <summary>
/// Затухания и поворот переживают сохранение черновика.
/// </summary>
/// <remarks>
/// Эти свойства добавлены позже остальных, и их легко забыть в паре
/// «писатель — читатель»: черновик откроется без ошибок, просто клип окажется
/// не повёрнут и без затемнения, а пользователь решит, что редактор их потерял.
/// </remarks>
public class ProjectPictureEditTests
{
    private static (Project Project, Dictionary<Guid, MediaInfo> Media) Build()
    {
        var info = Fake.Info(30);
        var project = Project.FromMedia(info);

        var clip = project.Sequence.Video.Clips[0]
            .WithFades(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.5))
            .WithTransform(ClipTransform.Identity.WithRotation(270).WithZoom(1.2));

        var media = project.Sources.ToDictionary(source => source.Id.Value, source => source.Info);

        return (project.WithSequence(project.Sequence.WithTrack(project.Sequence.Video.Replace(clip))), media);
    }

    [Fact]
    public void Fades_and_rotation_survive_the_round_trip()
    {
        var (project, media) = Build();

        var (restored, _) = ProjectDocumentReader.Restore(ProjectDocumentWriter.Create(project), media);
        var clip = restored.Sequence.Video.Clips[0];

        clip.FadeIn.Should().Be(TimeSpan.FromSeconds(1.5));
        clip.FadeOut.Should().Be(TimeSpan.FromSeconds(2.5));
        clip.Transform.Rotation.Should().Be(270);
        clip.Transform.Zoom.Should().BeApproximately(1.2, 0.0001);
    }

    [Fact]
    public void Draft_without_the_new_fields_opens_as_an_untouched_clip()
    {
        // Черновик, сделанный до появления затуханий: в JSON этих полей нет,
        // и клип обязан открыться обычным, а не с нулевым поворотом «наоборот».
        var (project, media) = Build();
        var document = ProjectDocumentWriter.Create(project);

        var old = new ProjectDocument
        {
            Sources = document.Sources,
            Format = document.Format,
            Clips = document.Clips
                .Select(clip => new ProjectClipDocument
                {
                    SourceId = clip.SourceId,
                    SourceStartMs = clip.SourceStartMs,
                    SourceEndMs = clip.SourceEndMs,
                    Speed = clip.Speed,
                    AudioEnabled = clip.AudioEnabled,
                    Volume = clip.Volume,
                    Zoom = clip.Zoom
                })
                .ToArray()
        };

        var (restored, _) = ProjectDocumentReader.Restore(old, media);
        var result = restored.Sequence.Video.Clips[0];

        result.HasFades.Should().BeFalse();
        result.Transform.IsRotated.Should().BeFalse();
    }
}
