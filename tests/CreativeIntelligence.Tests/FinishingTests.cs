using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Terminación de los videos generados: subtítulos, textos, música, fundidos y control de cuadros negros.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class FinishingTests(DatabaseFixture fixture)
{
    [Fact]
    public void Los_subtitulos_llevan_las_frases_de_cada_tramo_a_los_tiempos_del_montaje()
    {
        List<TimedText> result = SubtitleBuilder.FromTranscript(
        [
            new ClipSpeech(1000, 3000,
            [
                new TranscriptLineItem(500, 1400, "antes"),
                new TranscriptLineItem(1500, 2500, "adentro"),
                new TranscriptLineItem(2400, 3400, "se corta al final"),
                new TranscriptLineItem(2700, 3700, "después"),
            ]),
            new ClipSpeech(0, 2000, [new TranscriptLineItem(0, 1000, "  segundo tramo "), new TranscriptLineItem(1000, 1100, "muy corta")]),
        ]);

        Assert.Equal(
            [
                new TimedText(500, 1500, "adentro", null),
                new TimedText(1400, 2000, "se corta al final", null),
                new TimedText(2000, 3000, "segundo tramo", null),
            ],
            result);
    }

    [Fact]
    public void Los_textos_sobre_el_video_se_ubican_en_todo_el_video_la_apertura_o_el_cierre()
    {
        List<OverlayView> overlays =
        [
            new OverlayView("todo", OverlayPlacement.Top, OverlayMoment.Whole),
            new OverlayView("abre", OverlayPlacement.Center, OverlayMoment.Opening),
            new OverlayView("cierra", OverlayPlacement.Bottom, OverlayMoment.Closing),
        ];

        Assert.Equal(
            [
                new TimedText(0, 10_000, "todo", OverlayPlacement.Top),
                new TimedText(0, 3000, "abre", OverlayPlacement.Center),
                new TimedText(7000, 10_000, "cierra", OverlayPlacement.Bottom),
            ],
            SubtitleBuilder.FromOverlays(10_000, overlays));
        // En un video más corto que la apertura, la apertura y el cierre ocupan todo.
        Assert.All(SubtitleBuilder.FromOverlays(2000, overlays), t => Assert.Equal((0, 2000), (t.StartMs, t.EndMs)));
    }

    [Fact]
    public void El_archivo_de_textos_respeta_el_formato_y_no_deja_pasar_ordenes_de_formato()
    {
        string ass = SubtitleBuilder.BuildAss(
            1080, 1920,
            [
                new TimedText(1500, 62_340, "hola {\\b1}mundo\nsegunda línea", null),
                new TimedText(0, 3000, "OFERTA", OverlayPlacement.Center),
            ]);

        Assert.Contains("PlayResX: 1080\nPlayResY: 1920\n", ass);
        Assert.Contains("Style: Sub,Arial,73,", ass);
        Assert.Contains("Style: Center,Arial,106,", ass);
        Assert.Contains("Dialogue: 0,0:00:01.50,0:01:02.34,Sub,,0,0,0,,hola (/b1)mundo\\Nsegunda línea\n", ass);
        Assert.Contains("Dialogue: 0,0:00:00.00,0:00:03.00,Center,,0,0,0,,OFERTA\n", ass);
        Assert.DoesNotContain("{", ass);
    }

    [Theory]
    [InlineData(0, false, 0, new[] { "black" }, new[] { true })]
    [InlineData(499, false, 0, new[] { "black" }, new[] { true })]
    [InlineData(500, false, 0, new[] { "black" }, new[] { false })]
    [InlineData(0, true, 3, new[] { "black", "subtitles" }, new[] { true, true })]
    [InlineData(0, true, 0, new[] { "black", "subtitles" }, new[] { true, false })]
    public void El_control_de_la_terminacion_mira_los_cuadros_negros_y_los_subtitulos_pedidos(
        int blackMs, bool requested, int lines, string[] rules, bool[] passed)
    {
        List<QualityResult> results = CompositionRules.EvaluateFinishing(blackMs, requested, lines);

        Assert.Equal(rules, results.Select(r => r.Rule));
        Assert.Equal(passed, results.Select(r => r.Passed));
    }

    [Fact]
    public async Task Una_pista_de_musica_se_analiza_y_solo_la_autorizada_entra_en_una_receta()
    {
        Scenario scenario = await ArrangeAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        MusicService music = scope.ServiceProvider.GetRequiredService<MusicService>();
        CompositionService composition = scope.ServiceProvider.GetRequiredService<CompositionService>();
        MediaRightsBody authorized = new() { ReuseAuthorized = true, RightsHolder = "  Estudio  " };

        long trackId = await UploadAsync(music, scenario.MusicPath, "Base alegre", authorized);
        Assert.Empty(await music.ListAsync(true, CancellationToken.None));
        await DrainAsync();

        MusicTrackItem track = Assert.Single(await music.ListAsync(true, CancellationToken.None));
        Assert.Equal(("Base alegre", MediaProcessingStatus.Ready, "Estudio", true), (track.Name, track.Status, track.RightsHolder, track.ReuseAuthorized));
        Assert.InRange(track.DurationMs!.Value, 900, 1200);
        await JobTests.AssertBusinessAsync(409, "MUSIC_DUPLICATE", () => UploadAsync(music, scenario.MusicPath, "Otra vez", authorized));
        await JobTests.AssertBusinessAsync(415, "UNSUPPORTED_MEDIA_TYPE", () => UploadAsync(music, fixture.SampleVideoPath, "Un video", authorized));

        // Un archivo que no es audio queda fallido con el motivo y no se ofrece.
        string garbage = Path.Combine(fixture.StorageRoot, "samples", $"basura-{Guid.NewGuid():N}.mp3");
        await File.WriteAllTextAsync(garbage, "esto no es audio");
        await UploadAsync(music, garbage, null, authorized);
        await DrainAsync();
        MusicTrackItem broken = (await music.ListAsync(false, CancellationToken.None)).Single(m => m.Id != trackId);
        Assert.Equal(MediaProcessingStatus.Failed, broken.Status);
        Assert.NotNull(broken.Error);
        Assert.Single(await music.ListAsync(true, CancellationToken.None));

        CreateRecipeBody body = new() { Name = "Con música", MusicTrackId = trackId, Items = [new RecipeItemBody { SegmentId = scenario.Cuts[0] }] };
        await JobTests.AssertBusinessAsync(
            404, "MUSIC_NOT_FOUND",
            () => composition.CreateAsync(
                new CreateRecipeBody { Name = "x", MusicTrackId = broken.Id, Items = body.Items }, CancellationToken.None));
        await music.SetAuthorizedAsync(trackId, false, CancellationToken.None);
        await JobTests.AssertBusinessAsync(409, "REUSE_NOT_AUTHORIZED", () => composition.CreateAsync(body, CancellationToken.None));
        await music.SetAuthorizedAsync(trackId, true, CancellationToken.None);
        long recipeId = await composition.CreateAsync(body, CancellationToken.None);

        // Si la pista pierde la autorización después, la receta deja de poder renderizarse.
        await music.SetAuthorizedAsync(trackId, false, CancellationToken.None);
        BusinessException denied = await Assert.ThrowsAsync<BusinessException>(() => composition.RenderAsync(recipeId, CancellationToken.None));
        Assert.Equal("REUSE_NOT_AUTHORIZED", denied.Code);
        Assert.Contains("Base alegre", denied.Message);

        await using AsyncServiceScope otherScope = fixture.Scope(await fixture.NewOrganizationAsync());
        Assert.Empty(await otherScope.ServiceProvider.GetRequiredService<MusicService>().ListAsync(false, CancellationToken.None));
    }

    [Fact]
    public async Task El_render_quema_los_textos_mezcla_la_musica_y_funde_sin_cambiar_la_duracion()
    {
        Scenario scenario = await ArrangeAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        CompositionService composition = scope.ServiceProvider.GetRequiredService<CompositionService>();
        MusicService music = scope.ServiceProvider.GetRequiredService<MusicService>();
        IMediaTools tools = fixture.Services.GetRequiredService<IMediaTools>();
        long trackId = await UploadAsync(music, scenario.MusicPath, "Base", new MediaRightsBody { ReuseAuthorized = true });
        await DrainAsync();

        // El video de cortes no tiene audio: sin música queda en silencio, con música se oye.
        List<RecipeItemBody> items = [new RecipeItemBody { SegmentId = scenario.Cuts[0] }, new RecipeItemBody { SegmentId = scenario.Cuts[2] }];
        long plainId = await composition.CreateAsync(new CreateRecipeBody { Name = "Simple", Format = "1:1", Items = items }, CancellationToken.None);
        long finishedId = await composition.CreateAsync(
            new CreateRecipeBody
            {
                Name = "Terminada",
                Format = "1:1",
                Items = items,
                Transition = RecipeTransition.FadeBlack,
                MusicTrackId = trackId,
                MusicVolumePercent = 50,
                Overlays =
                [
                    new OverlayBody { Text = "  OFERTA HOY  ", Placement = OverlayPlacement.Center, Moment = OverlayMoment.Whole },
                    new OverlayBody { Text = "   " },
                ],
            },
            CancellationToken.None);
        await composition.RenderAsync(plainId, CancellationToken.None);
        await composition.RenderAsync(finishedId, CancellationToken.None);
        await DrainAsync();

        RecipeDetail plain = await composition.GetAsync(plainId, CancellationToken.None);
        RecipeDetail finished = await composition.GetAsync(finishedId, CancellationToken.None);
        Assert.Equal((RecipeTransition.FadeBlack, "Base", 50, false), (finished.Transition, finished.MusicName, finished.MusicVolumePercent, finished.Subtitles));
        Assert.Equal(new OverlayView("OFERTA HOY", OverlayPlacement.Center, OverlayMoment.Whole), Assert.Single(finished.Overlays));
        Assert.Empty(plain.Overlays);
        VariantView plainVideo = plain.Variants.Single();
        VariantView finishedVideo = finished.Variants.Single();
        Assert.Null(finishedVideo.Error);
        Assert.True(plainVideo.PassedQuality);
        Assert.True(finishedVideo.PassedQuality);
        Assert.Equal(["audio", "black", "duration", "resolution"], finishedVideo.Checks.Select(c => c.Rule));
        // El fundido no alarga ni acorta el montaje.
        Assert.Equal(plainVideo.DurationMs, finishedVideo.DurationMs);

        string plainPath = await PathAsync(scope, plainVideo.Id);
        string finishedPath = await PathAsync(scope, finishedVideo.Id);
        // Mismo instante, en medio del primer tramo: la imagen cambia sólo por el texto quemado.
        Assert.NotEqual(await tools.FrameHashAsync(plainPath, 1000, CancellationToken.None), await tools.FrameHashAsync(finishedPath, 1000, CancellationToken.None));
        AudioLevels? silent = await tools.MeasureAudioAsync(plainPath, 0, 3000, CancellationToken.None);
        AudioLevels? withMusic = await tools.MeasureAudioAsync(finishedPath, 0, 3000, CancellationToken.None);
        Assert.True(silent is null || silent.MeanDb < MediaRules.AudioSilentBelowDb);
        Assert.True(withMusic!.MeanDb > MediaRules.AudioSilentBelowDb);
    }

    [Fact]
    public async Task Los_subtitulos_salen_de_la_transcripcion_y_si_no_hay_voz_el_control_lo_avisa()
    {
        Scenario scenario = await ArrangeAsync(withVoice: true);
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        CompositionService composition = scope.ServiceProvider.GetRequiredService<CompositionService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.MediaTranscriptLines.AnyAsync());

        long spoken = await composition.CreateAsync(
            new CreateRecipeBody
            {
                Name = "Con voz",
                Format = "1:1",
                Subtitles = true,
                Items = [new RecipeItemBody { SegmentId = scenario.Voice!.Value }, new RecipeItemBody { SegmentId = scenario.Cuts[0] }],
            },
            CancellationToken.None);
        long mute = await composition.CreateAsync(
            new CreateRecipeBody { Name = "Sin voz", Format = "1:1", Subtitles = true, Items = [new RecipeItemBody { SegmentId = scenario.Cuts[0] }] },
            CancellationToken.None);
        await composition.RenderAsync(spoken, CancellationToken.None);
        await composition.RenderAsync(mute, CancellationToken.None);
        await DrainAsync();

        VariantView withSubtitles = (await composition.GetAsync(spoken, CancellationToken.None)).Variants.Single();
        Assert.Null(withSubtitles.Error);
        Assert.True(withSubtitles.PassedQuality);
        QualityResult check = withSubtitles.Checks.Single(c => c.Rule == "subtitles");
        Assert.True(check.Passed);
        Assert.Contains("subtítulo(s) quemados", check.Detail);

        // Pidió subtítulos y no hay nada que subtitular: el video sale, pero no pasa el control.
        VariantView without = (await composition.GetAsync(mute, CancellationToken.None)).Variants.Single();
        Assert.Equal(MediaProcessingStatus.Ready, without.Status);
        Assert.False(without.PassedQuality);
        Assert.False(without.Checks.Single(c => c.Rule == "subtitles").Passed);
    }

    [Fact]
    public async Task Un_tramo_largo_en_negro_se_detecta_y_un_video_normal_no_tiene()
    {
        IMediaTools tools = fixture.Services.GetRequiredService<IMediaTools>();
        string black = Path.Combine(fixture.StorageRoot, "samples", $"negro-{Guid.NewGuid():N}.mp4");
        await DatabaseFixture.RunFfmpegAsync(
            "-f", "lavfi", "-i", "color=c=red:d=1:s=320x240:r=30", "-f", "lavfi", "-i", "color=c=black:d=1.2:s=320x240:r=30",
            "-filter_complex", "[0:v][1:v]concat=n=2:v=1:a=0", "-c:v", "libx264", "-pix_fmt", "yuv420p", black);

        Assert.InRange(await tools.LongestBlackMsAsync(black, CancellationToken.None), 1000, 1300);
        Assert.Equal(0, await tools.LongestBlackMsAsync(fixture.SampleCutsVideoPath, CancellationToken.None));
    }

    [Fact]
    public async Task Un_lote_aplica_la_misma_terminacion_a_todas_sus_variantes()
    {
        Scenario scenario = await ArrangeAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        long batchId = await scope.ServiceProvider.GetRequiredService<BatchService>().CreateAsync(
            new CreateBatchBody
            {
                Name = "Lote terminado",
                Count = 5,
                Transition = RecipeTransition.FadeBlack,
                Subtitles = true,
                Overlays = [new OverlayBody { Text = "Envío gratis", Placement = OverlayPlacement.Bottom, Moment = OverlayMoment.Closing }],
                Items = scenario.Cuts.Select(id => new RecipeItemBody { SegmentId = id }).ToList(),
            },
            CancellationToken.None);
        await fixture.ClearJobsAsync();

        List<CreativeRecipe> recipes = await db.CreativeRecipes.AsNoTracking().Include(r => r.Overlays).Where(r => r.CreativeBatchId == batchId).ToListAsync();
        Assert.Equal(5, recipes.Count);
        Assert.All(recipes, r => Assert.Equal((RecipeTransition.FadeBlack, true), (r.Transition, r.Subtitles)));
        Assert.All(recipes, r => Assert.Equal(("Envío gratis", OverlayMoment.Closing), (Assert.Single(r.Overlays).Text, r.Overlays[0].Moment)));
    }

    private static async Task<long> UploadAsync(MusicService music, string path, string? name, MediaRightsBody rights)
    {
        await using FileStream stream = File.OpenRead(path);
        return await music.UploadAsync(Path.GetFileName(path), name, rights, stream, CancellationToken.None);
    }

    private static async Task<string> PathAsync(AsyncServiceScope scope, long variantId)
    {
        string relative = await scope.ServiceProvider.GetRequiredService<AppDbContext>().CreativeVariants
            .Where(v => v.Id == variantId).Select(v => v.StoragePath!).SingleAsync();
        return scope.ServiceProvider.GetRequiredService<IMediaStorage>().GetLocalPath(relative);
    }

    // Una organización con el video de tres cortes autorizado, un tono de un segundo para usar de música y,
    // si se pide, el video con voz ya transcripto.
    private async Task<Scenario> ArrangeAsync(bool withVoice = false)
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        MediaRightsBody rights = new() { ReuseAuthorized = true };
        long cuts;
        await using (FileStream stream = File.OpenRead(fixture.SampleCutsVideoPath))
        {
            cuts = (await media.IngestAsync("cortes.mp4", rights, stream, CancellationToken.None)).AssetId;
        }

        long? voice = null;
        if (withVoice)
        {
            await using FileStream stream = File.OpenRead(fixture.SampleVoiceVideoPath);
            voice = (await media.IngestAsync("voz.mp4", rights, stream, CancellationToken.None)).AssetId;
        }

        await DrainAsync();
        SceneService scenes = scope.ServiceProvider.GetRequiredService<SceneService>();
        List<long> cutScenes = (await scenes.ListAsync(cuts, CancellationToken.None)).Segments.Select(s => s.Id).ToList();
        Assert.Equal(3, cutScenes.Count);
        long? voiceScene = voice is null ? null : (await scenes.ListAsync(voice.Value, CancellationToken.None)).Segments[0].Id;

        // Cada organización recibe un tono distinto: la misma pista no se carga dos veces.
        string musicPath = Path.Combine(fixture.StorageRoot, "samples", $"musica-{org}.wav");
        await DatabaseFixture.RunFfmpegAsync("-f", "lavfi", "-i", $"sine=frequency={200 + org}:duration=1", musicPath);
        return new Scenario(org, cutScenes, voiceScene, musicPath);
    }

    private async Task DrainAsync()
    {
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }

    private sealed record Scenario(int Organization, List<long> Cuts, long? Voice, string MusicPath);
}
