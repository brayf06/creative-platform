using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Calidad de audio medida con FFmpeg real sobre tonos generados con volumen conocido.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class AudioQualityTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("normal", "volume=1", AudioQuality.Good)]
    [InlineData("bajo", "volume=0.1", AudioQuality.Low)]
    [InlineData("casi-mudo", "volume=0.005", AudioQuality.Silent)]
    [InlineData("saturado", "volume=30", AudioQuality.Clipped)]
    public async Task El_volumen_de_un_tono_se_mide_y_se_clasifica(string name, string filter, AudioQuality expected)
    {
        // El tono de FFmpeg sale a 1/8 de la escala (pico de unos -18 dB); el filtro lo sube o lo baja.
        string path = Path.Combine(fixture.StorageRoot, "samples", $"tono-{name}.wav");
        await DatabaseFixture.RunFfmpegAsync("-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-af", filter, path);
        IMediaTools tools = fixture.Services.GetRequiredService<IMediaTools>();

        AudioLevels? levels = await tools.MeasureAudioAsync(path, 500, 1500, CancellationToken.None);

        Assert.NotNull(levels);
        Assert.True(levels.PeakDb >= levels.MeanDb);
        Assert.InRange(levels.MeanDb, -100m, 0m);
        Assert.Equal(expected, MediaRules.ClassifyAudio(levels.MeanDb, levels.PeakDb));
    }

    [Fact]
    public async Task El_silencio_digital_se_mide_como_sin_sonido()
    {
        string path = Path.Combine(fixture.StorageRoot, "samples", "silencio.wav");
        await DatabaseFixture.RunFfmpegAsync("-f", "lavfi", "-i", "anullsrc=r=44100:cl=mono", "-t", "1", path);

        AudioLevels? levels = await fixture.Services.GetRequiredService<IMediaTools>().MeasureAudioAsync(path, 0, 1000, CancellationToken.None);

        Assert.NotNull(levels);
        Assert.Equal(AudioQuality.Silent, MediaRules.ClassifyAudio(levels.MeanDb, levels.PeakDb));
    }

    [Fact]
    public async Task Cada_escena_de_un_video_con_audio_queda_con_sus_niveles_y_al_editarla_se_vuelven_a_medir()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        SceneService scenes = scope.ServiceProvider.GetRequiredService<SceneService>();
        long withAudio = await IngestAsync(media, "con tono.mp4", fixture.SampleVideoPath);
        long withoutAudio = await IngestAsync(media, "mudo.mp4", fixture.SampleCutsVideoPath);
        await DrainAsync();

        SceneList audible = await scenes.ListAsync(withAudio, CancellationToken.None);
        Assert.True(audible.HasAudio);
        SegmentItem only = Assert.Single(audible.Segments);
        Assert.Equal(AudioQuality.Good, only.AudioQuality);
        Assert.InRange(only.AudioMeanDb!.Value, -30m, -15m);

        // Sin pista de audio no hay nada que medir: queda sin dato, no como "sin sonido".
        SceneList mute = await scenes.ListAsync(withoutAudio, CancellationToken.None);
        Assert.False(mute.HasAudio);
        Assert.All(mute.Segments, s => Assert.Null(s.AudioQuality));

        await scope.ServiceProvider.GetRequiredService<SegmentEditService>().SplitAsync(only.Id, 1000, CancellationToken.None);
        Assert.All((await scenes.ListAsync(withAudio, CancellationToken.None)).Segments, s => Assert.Null(s.AudioQuality));

        await DrainAsync();
        List<SegmentItem> measured = (await scenes.ListAsync(withAudio, CancellationToken.None)).Segments;
        Assert.Equal(2, measured.Count);
        Assert.All(measured, s => Assert.Equal(AudioQuality.Good, s.AudioQuality));
        Assert.All(measured, s => Assert.True(s.HasThumbnail));
    }

    [Theory]
    [InlineData(-20.0, -3.0, AudioQuality.Good)]
    [InlineData(-30.0, -10.0, AudioQuality.Good)]
    [InlineData(-30.1, -10.0, AudioQuality.Low)]
    [InlineData(-50.0, -30.0, AudioQuality.Low)]
    [InlineData(-50.1, -30.0, AudioQuality.Silent)]
    [InlineData(-12.0, -0.5, AudioQuality.Clipped)]
    [InlineData(-12.0, 0.0, AudioQuality.Clipped)]
    [InlineData(-12.0, -0.6, AudioQuality.Good)]
    [InlineData(-100.0, -100.0, AudioQuality.Silent)]
    public void La_calidad_sale_del_volumen_medio_y_del_pico(double mean, double peak, AudioQuality expected)
    {
        Assert.Equal(expected, MediaRules.ClassifyAudio((decimal)mean, (decimal)peak));
    }

    [Fact]
    public void Sin_medicion_no_hay_calidad()
    {
        Assert.Null(MediaRules.ClassifyAudio(null, null));
        Assert.Null(MediaRules.ClassifyAudio(-20m, null));
    }

    private static async Task<long> IngestAsync(MediaLibraryService media, string fileName, string sourcePath)
    {
        await using FileStream stream = File.OpenRead(sourcePath);
        return (await media.IngestAsync(fileName, new MediaRightsBody(), stream, CancellationToken.None)).AssetId;
    }

    private async Task DrainAsync()
    {
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }
}