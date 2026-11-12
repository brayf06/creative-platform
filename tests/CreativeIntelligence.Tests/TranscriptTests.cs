using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Transcripción con Whisper local real (modelo tiny) sobre un video con voz sintetizada por FFmpeg.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class TranscriptTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Un_video_con_voz_queda_transcripto_con_tiempos_y_el_texto_aparece_en_su_escena()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        long id = await IngestAsync(org, "voz.mp4", fixture.SampleVoiceVideoPath);
        Assert.Null((await GetAsync(org, id)).Status);

        await DrainAsync();

        TranscriptView transcript = await GetAsync(org, id);
        Assert.True(transcript.HasAudio);
        Assert.Equal(MediaProcessingStatus.Ready, transcript.Status);
        Assert.Equal("en", transcript.Language);
        Assert.Equal("faster-whisper:tiny", transcript.Model);
        Assert.NotEmpty(transcript.Lines);
        string text = string.Join(" ", transcript.Lines.Select(l => l.Text));
        Assert.Contains("nails", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("minutes", text, StringComparison.OrdinalIgnoreCase);
        Assert.All(transcript.Lines, l => Assert.True(l.EndMs > l.StartMs));
        Assert.Equal(transcript.Lines.OrderBy(l => l.StartMs).Select(l => l.StartMs), transcript.Lines.Select(l => l.StartMs));

        await using AsyncServiceScope scope = fixture.Scope(org);
        SceneList scenes = await scope.ServiceProvider.GetRequiredService<SceneService>().ListAsync(id, CancellationToken.None);
        Assert.Contains("nails", string.Join(" ", scenes.Segments.Select(s => s.Transcript)), StringComparison.OrdinalIgnoreCase);

        // El cómputo local queda medido en el uso de IA, con costo cero.
        AiUsageRecord usage = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AiUsageRecords.AsNoTracking()
            .SingleAsync(u => u.Task == "media.transcription");
        Assert.Equal("media.transcription", usage.Task);
        Assert.Equal("faster-whisper:tiny", usage.Model);
        Assert.Equal(0m, usage.CostUsd);
        Assert.True(usage.DurationMs > 0);
        Assert.NotNull(usage.BackgroundJobId);
    }

    [Fact]
    public async Task Volver_a_transcribir_reemplaza_las_frases_en_lugar_de_duplicarlas()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        long id = await IngestAsync(org, "voz.mp4", fixture.SampleVoiceVideoPath);
        await DrainAsync();
        int lines = (await GetAsync(org, id)).Lines.Count;

        await using (AsyncServiceScope scope = fixture.Scope(org))
        {
            await scope.ServiceProvider.GetRequiredService<TranscriptService>().RequestAsync(id, CancellationToken.None);
        }

        Assert.Equal(MediaProcessingStatus.Pending, (await GetAsync(org, id)).Status);
        await DrainAsync();

        TranscriptView transcript = await GetAsync(org, id);
        Assert.Equal(MediaProcessingStatus.Ready, transcript.Status);
        Assert.Equal(lines, transcript.Lines.Count);
    }

    [Fact]
    public async Task Sin_audio_no_se_transcribe_y_otra_organizacion_no_ve_la_transcripcion()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        int other = await fixture.NewOrganizationAsync();
        long silent = await IngestAsync(org, "cortes.mp4", fixture.SampleCutsVideoPath);
        long image = await IngestAsync(org, "producto.png", fixture.SampleImagePath);

        await using (AsyncServiceScope early = fixture.Scope(org))
        {
            TranscriptService service = early.ServiceProvider.GetRequiredService<TranscriptService>();
            await JobTests.AssertBusinessAsync(409, "MEDIA_NOT_READY", () => service.RequestAsync(silent, CancellationToken.None));
        }

        await DrainAsync();

        TranscriptView transcript = await GetAsync(org, silent);
        Assert.False(transcript.HasAudio);
        Assert.Null(transcript.Status);
        Assert.Empty(transcript.Lines);
        await using (AsyncServiceScope scope = fixture.Scope(org))
        {
            TranscriptService service = scope.ServiceProvider.GetRequiredService<TranscriptService>();
            await JobTests.AssertBusinessAsync(409, "NO_AUDIO", () => service.RequestAsync(silent, CancellationToken.None));
            await JobTests.AssertBusinessAsync(409, "NO_AUDIO", () => service.RequestAsync(image, CancellationToken.None));
        }

        await using AsyncServiceScope otherScope = fixture.Scope(other);
        TranscriptService foreign = otherScope.ServiceProvider.GetRequiredService<TranscriptService>();
        await JobTests.AssertBusinessAsync(404, "MEDIA_NOT_FOUND", () => foreign.GetAsync(silent, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "MEDIA_NOT_FOUND", () => foreign.RequestAsync(silent, CancellationToken.None));
    }

    [Theory]
    [InlineData(0, 2000, "uno dos")]
    [InlineData(2000, 4000, "tres")]
    [InlineData(4000, 6000, "")]
    [InlineData(0, 6000, "uno dos tres")]
    public void El_texto_de_un_tramo_son_las_frases_cuyo_punto_medio_cae_dentro(int startMs, int endMs, string expected)
    {
        // "dos" cruza el corte de los 2000 ms, pero su punto medio (1950) queda en el primer tramo.
        List<TranscriptLineItem> lines = [new(100, 900, "uno"), new(1000, 2900, "dos"), new(3000, 3800, "tres")];

        Assert.Equal(expected, TranscriptService.TextBetween(lines, startMs, endMs));
    }

    private async Task<long> IngestAsync(int organizationId, string fileName, string sourcePath)
    {
        await using AsyncServiceScope scope = fixture.Scope(organizationId);
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        await using FileStream stream = File.OpenRead(sourcePath);
        return (await media.IngestAsync(fileName, new MediaRightsBody(), stream, CancellationToken.None)).AssetId;
    }

    private async Task<TranscriptView> GetAsync(int organizationId, long assetId)
    {
        await using AsyncServiceScope scope = fixture.Scope(organizationId);
        return await scope.ServiceProvider.GetRequiredService<TranscriptService>().GetAsync(assetId, CancellationToken.None);
    }

    private async Task DrainAsync()
    {
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }
}
