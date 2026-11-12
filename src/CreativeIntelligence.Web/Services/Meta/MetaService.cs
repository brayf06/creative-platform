using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Meta;

/// <summary>
/// Rendimiento publicitario de la organización activa: importa las métricas de los anuncios de Meta, las muestra
/// por anuncio y por día, vincula cada anuncio con el video generado que se publicó en él y reconstruye su linaje.
/// Las métricas son observaciones: dicen qué pasó, no por qué. Nada acá declara ganadores ni escribe en Meta.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="storage">Almacenamiento, para guardar el archivo importado.</param>
/// <param name="jobs">Cola de trabajos.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
public sealed class MetaService(AppDbContext db, IMediaStorage storage, JobService jobs, AuditService audit, CurrentContext current)
{
    /// <summary>Devuelve las últimas importaciones y los anuncios con sus métricas del período. No escribe.</summary>
    /// <param name="filter">Período; sin fechas, todo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Importaciones y hasta 500 anuncios, del que más gastó al que menos.</returns>
    public async Task<MetaOverview> OverviewAsync(MetaFilter filter, CancellationToken cancellationToken)
    {
        List<MetaImportItem> imports = await db.MetaImports.AsNoTracking()
            .OrderByDescending(i => i.Id)
            .Take(10)
            .Select(i => new MetaImportItem(i.Id, i.FileName, i.Status, i.Notes, i.Rows, i.Skipped, i.Ads, i.FirstDate, i.LastDate, i.CreatedAt))
            .ToListAsync(cancellationToken);
        Dictionary<long, AdTotalsRow> totals = (await TotalsAsync(filter, null, cancellationToken)).ToDictionary(t => t.AdId);
        List<AdRow> ads = await AdsQuery().Where(a => totals.Keys.Contains(a.Id)).ToListAsync(cancellationToken);
        return new MetaOverview(
            filter,
            imports,
            ads.Select(a => new AdListItem(a.Id, a.ExternalId, a.Name, a.CampaignName, a.AdSetName, a.Currency, a.VariantId, ToMetrics(totals[a.Id])))
                .OrderByDescending(a => a.Metrics.Spend)
                .Take(500)
                .ToList());
    }

    /// <summary>Devuelve un anuncio con sus métricas por día, su linaje si está vinculado y los videos que se le
    /// pueden vincular. No escribe.</summary>
    /// <param name="id">Anuncio.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El anuncio.</returns>
    /// <exception cref="BusinessException">404 <c>AD_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task<AdDetail> GetAsync(long id, CancellationToken cancellationToken)
    {
        AdRow ad = await AdsQuery().FirstOrDefaultAsync(a => a.Id == id, cancellationToken) ?? throw NotFound();
        List<AdDay> days = await db.MetaInsightsDaily.AsNoTracking()
            .Where(i => i.MetaAdId == id)
            .OrderBy(i => i.Date)
            .Select(i => new AdDay(i.Date, new AdMetrics(i.Spend, i.Impressions, i.Clicks, i.VideoPlays, i.ThruPlays, i.Purchases, i.PurchaseValue, 1)))
            .ToListAsync(cancellationToken);
        AdTotalsRow? totals = (await TotalsAsync(new MetaFilter(), id, cancellationToken)).FirstOrDefault();
        List<VariantOption> candidates = await db.CreativeVariants.AsNoTracking()
            .Where(v => v.Status == MediaProcessingStatus.Ready)
            .OrderByDescending(v => v.Id)
            .Take(200)
            .Select(v => new VariantOption(
                v.Id, db.CreativeRecipes.Where(r => r.Id == v.CreativeRecipeId).Select(r => r.Name).First(), v.CreatedAt))
            .ToListAsync(cancellationToken);
        return new AdDetail(
            ad.Id, ad.ExternalId, ad.Name, ad.AccountName, ad.CampaignName, ad.AdSetName, ad.Currency, ad.LinkOrigin,
            totals is null ? new AdMetrics(0, 0, 0, null, null, null, null, 0) : ToMetrics(totals),
            days,
            ad.VariantId is null ? null : await LineageAsync(ad.VariantId.Value, cancellationToken),
            candidates)
        {
            Experiments = await db.ExperimentArms.AsNoTracking()
                .Where(a => a.MetaAdId == id)
                .OrderByDescending(a => a.ExperimentId)
                .Select(a => new AdExperiment(a.ExperimentId, db.Experiments.Where(e => e.Id == a.ExperimentId).Select(e => e.Name).First(), a.Name))
                .ToListAsync(cancellationToken),
        };
    }

    /// <summary>
    /// Recibe un archivo de métricas y encola su importación. Escribe el archivo, el registro de la importación, la
    /// auditoría y el trabajo.
    /// </summary>
    /// <param name="fileName">Nombre del archivo.</param>
    /// <param name="currency">Moneda que se asume si el archivo no la dice.</param>
    /// <param name="content">Contenido del archivo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador de la importación.</returns>
    /// <exception cref="BusinessException">415 <c>UNSUPPORTED_MEDIA_TYPE</c> si no es un CSV; 400 <c>EMPTY_FILE</c>.</exception>
    public async Task<long> UploadAsync(string fileName, string currency, Stream content, CancellationToken cancellationToken)
    {
        string name = Path.GetFileName(fileName.Trim());
        if (!string.Equals(Path.GetExtension(name), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(415, "El archivo tiene que ser un CSV exportado de Ads Manager.", "UNSUPPORTED_MEDIA_TYPE");
        }

        StoredOriginal stored = await storage.SaveOriginalAsync(current.OrganizationId, content, cancellationToken);
        if (stored.SizeBytes == 0)
        {
            throw new BusinessException(400, "El archivo está vacío.", "EMPTY_FILE");
        }

        MetaImport import = new()
        {
            FileName = name.Length > 260 ? name[^260..] : name,
            StoragePath = stored.RelativePath,
            DefaultCurrency = currency.ToUpperInvariant(),
            Status = MediaProcessingStatus.Pending,
            CreatedByUserId = current.UserId,
        };
        return await db.InTransactionAsync(
            async () =>
            {
                db.MetaImports.Add(import);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record("meta.import", nameof(MetaImport), import.Id.ToString(), $"Cargó el informe {import.FileName}.");
                await db.SaveChangesAsync(cancellationToken);
                await jobs.EnqueueAsync(
                    MetaImportJobHandler.JobType,
                    $"{MetaImportJobHandler.JobType}:{import.Id}",
                    JsonSerializer.Serialize(new MetaImportPayload(import.Id)),
                    cancellationToken);
                return import.Id;
            },
            cancellationToken);
    }

    /// <summary>Vincula un anuncio con el video generado que se publicó en él, o quita el vínculo. Escribe el
    /// anuncio y la auditoría.</summary>
    /// <param name="adId">Anuncio.</param>
    /// <param name="variantId">Video generado; nulo para quitar el vínculo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>AD_NOT_FOUND</c>; 404 <c>VARIANT_NOT_FOUND</c> si el video no existe
    /// en la organización activa o no terminó de generarse.</exception>
    public async Task LinkAsync(long adId, long? variantId, CancellationToken cancellationToken)
    {
        MetaAd ad = await db.MetaAds.FirstOrDefaultAsync(a => a.Id == adId, cancellationToken) ?? throw NotFound();
        if (variantId is not null
            && !await db.CreativeVariants.AnyAsync(v => v.Id == variantId && v.Status == MediaProcessingStatus.Ready, cancellationToken))
        {
            throw new BusinessException(404, "El video no existe o todavía no terminó de generarse.", "VARIANT_NOT_FOUND");
        }

        ad.CreativeVariantId = variantId;
        ad.LinkOrigin = variantId is null ? null : AdLinkOrigin.Manual;
        ad.LinkedByUserId = variantId is null ? null : current.UserId;
        audit.Record(
            "meta.ad.link", nameof(MetaAd), adId.ToString(),
            variantId is null ? "Quitó el vínculo del anuncio con su video." : $"Vinculó el anuncio con el video #{variantId}.");
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Reconstruye cómo se generó un video: su receta, sus tramos y, si existen, su concepto, su plan, su
    /// brief, su marca y su lote. No escribe.</summary>
    /// <param name="variantId">Video generado.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El linaje.</returns>
    public async Task<AdLineage> LineageAsync(long variantId, CancellationToken cancellationToken)
    {
        AdLineage lineage = await db.CreativeVariants.AsNoTracking()
            .Where(v => v.Id == variantId)
            .Join(db.CreativeRecipes, v => v.CreativeRecipeId, r => r.Id, (v, r) => r)
            .Select(r => new AdLineage
            {
                VariantId = variantId,
                RecipeId = r.Id,
                RecipeName = r.Name,
                Hook = r.Concept,
                Hypothesis = r.Hypothesis,
                BatchId = r.CreativeBatchId,
                BatchName = db.CreativeBatches.Where(b => b.Id == r.CreativeBatchId).Select(b => b.Name).FirstOrDefault(),
                ConceptId = db.CreativeConcepts.Where(c => c.CreativeRecipeId == r.Id).Select(c => (long?)c.Id).FirstOrDefault(),
                ConceptTitle = db.CreativeConcepts.Where(c => c.CreativeRecipeId == r.Id).Select(c => c.Title).FirstOrDefault(),
                PlanId = db.CreativeConcepts.Where(c => c.CreativeRecipeId == r.Id).Select(c => (long?)c.CreativePlanId).FirstOrDefault(),
                BriefId = db.CreativePlans
                    .Where(p => db.CreativeConcepts.Any(c => c.CreativeRecipeId == r.Id && c.CreativePlanId == p.Id))
                    .Select(p => (long?)p.CampaignBriefId)
                    .FirstOrDefault(),
                BriefName = db.CreativePlans
                    .Where(p => db.CreativeConcepts.Any(c => c.CreativeRecipeId == r.Id && c.CreativePlanId == p.Id))
                    .Select(p => p.Brief!.Name)
                    .FirstOrDefault(),
                BrandName = db.CreativePlans
                    .Where(p => db.CreativeConcepts.Any(c => c.CreativeRecipeId == r.Id && c.CreativePlanId == p.Id))
                    .Select(p => p.Brief!.Brand!.Name)
                    .FirstOrDefault(),
            })
            .FirstAsync(cancellationToken);
        lineage.Items = await db.RecipeItems.AsNoTracking()
            .Where(i => i.CreativeRecipeId == lineage.RecipeId)
            .OrderBy(i => i.Position)
            .Select(i => new RecipeItemView(
                i.Position,
                i.Role,
                i.MediaAssetId,
                i.MediaAsset!.OriginalFileName,
                i.StartMs,
                i.EndMs,
                i.MediaSegmentId,
                i.MediaAsset.ReuseAuthorized
                    && (i.MediaAsset.RightsExpireOn == null || i.MediaAsset.RightsExpireOn >= DateOnly.FromDateTime(AppDbContext.UtcNow()))))
            .ToListAsync(cancellationToken);
        return lineage;
    }

    /// <summary>Convierte las sumas de un período en métricas con sus tasas.</summary>
    /// <param name="row">Sumas.</param>
    /// <returns>Las métricas.</returns>
    public static AdMetrics ToMetrics(AdTotalsRow row)
    {
        return new AdMetrics(row.Spend, row.Impressions, row.Clicks, row.VideoPlays, row.ThruPlays, row.Purchases, row.PurchaseValue, row.Days);
    }

    /// <summary>Suma las métricas diarias por anuncio dentro de un período. Las tasas no se suman: se calculan
    /// sobre estas sumas. No escribe.</summary>
    /// <param name="filter">Período.</param>
    /// <param name="adId">Sólo este anuncio; nulo para todos.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Una fila por anuncio con datos en el período.</returns>
    public async Task<List<AdTotalsRow>> TotalsAsync(MetaFilter filter, long? adId, CancellationToken cancellationToken)
    {
        return await db.MetaInsightsDaily.AsNoTracking()
            .Where(i => (adId == null || i.MetaAdId == adId)
                && (filter.From == null || i.Date >= filter.From)
                && (filter.To == null || i.Date <= filter.To))
            .GroupBy(i => i.MetaAdId)
            .Select(g => new AdTotalsRow
            {
                AdId = g.Key,
                Spend = g.Sum(i => i.Spend),
                Impressions = g.Sum(i => i.Impressions),
                Clicks = g.Sum(i => i.Clicks),
                VideoPlays = g.Sum(i => i.VideoPlays),
                ThruPlays = g.Sum(i => i.ThruPlays),
                Purchases = g.Sum(i => i.Purchases),
                PurchaseValue = g.Sum(i => i.PurchaseValue),
                Days = g.Count(),
            })
            .ToListAsync(cancellationToken);
    }

    private IQueryable<AdRow> AdsQuery()
    {
        return db.MetaAds.AsNoTracking().Select(a => new AdRow
        {
            Id = a.Id,
            ExternalId = a.ExternalId,
            Name = a.Name,
            CampaignName = a.AdSet!.Campaign!.Name,
            AdSetName = a.AdSet.Name,
            AccountName = db.MetaAdAccounts.Where(c => c.Id == a.AdSet.Campaign.MetaAdAccountId).Select(c => c.Name).First(),
            Currency = db.MetaAdAccounts.Where(c => c.Id == a.AdSet.Campaign.MetaAdAccountId).Select(c => c.Currency).First(),
            VariantId = a.CreativeVariantId,
            LinkOrigin = a.LinkOrigin,
        });
    }

    private static BusinessException NotFound()
    {
        return new BusinessException(404, "El anuncio no existe.", "AD_NOT_FOUND");
    }
}

/// <summary>
/// Importación de un informe de métricas. Lee el archivo, crea las cuentas, campañas, conjuntos y anuncios que no
/// existan y guarda una fila por anuncio y día. Es idempotente: volver a importar el mismo día de un anuncio
/// reemplaza sus valores (un informe más nuevo corrige al anterior), no los suma. Un archivo sin las columnas
/// obligatorias deja la importación fallida con el motivo, sin reintentos.
/// </summary>
/// <param name="db">Contexto de datos, con la organización del trabajo activa.</param>
/// <param name="storage">Almacenamiento.</param>
/// <param name="anomalies">Detección de anomalías sobre lo importado.</param>
public sealed class MetaImportJobHandler(AppDbContext db, IMediaStorage storage, AnomalyService anomalies) : IJobHandler
{
    /// <summary>Tipo de trabajo que atiende.</summary>
    public const string JobType = "meta.import-csv";

    /// <inheritdoc />
    public string Type => JobType;

    /// <inheritdoc />
    /// <exception cref="PermanentJobException">Si la importación ya no existe o el archivo no tiene las columnas
    /// obligatorias.</exception>
    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        MetaImportPayload? payload = JsonSerializer.Deserialize<MetaImportPayload>(execution.Job.PayloadJson);
        long importId = payload?.ImportId ?? 0;
        MetaImport import = await db.MetaImports.FirstOrDefaultAsync(i => i.Id == importId, cancellationToken)
            ?? throw new PermanentJobException("La importación ya no existe.");
        string text;
        await using (Stream stream = storage.OpenRead(import.StoragePath))
        {
            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = await reader.ReadToEndAsync(cancellationToken);
        }

        MetaCsvResult result = MetaCsvParser.Parse(text);
        if (result.MissingColumns.Count > 0)
        {
            import.Status = MediaProcessingStatus.Failed;
            import.Notes = $"Al archivo le faltan columnas: {string.Join(", ", result.MissingColumns)}.";
            await db.SaveChangesAsync(cancellationToken);
            throw new PermanentJobException(import.Notes);
        }

        await execution.ReportAsync(20, "Guardando las métricas", cancellationToken);
        await db.InTransactionAsync(
            async () =>
            {
                await SaveAsync(result.Rows, result.Currency ?? import.DefaultCurrency, cancellationToken);
                import.Status = MediaProcessingStatus.Ready;
                import.Rows = result.Rows.Count;
                import.Skipped = result.Skipped;
                import.Ads = result.Rows.Select(r => r.AdId).Distinct().Count();
                import.FirstDate = result.Rows.Count == 0 ? null : result.Rows.Min(r => r.Date);
                import.LastDate = result.Rows.Count == 0 ? null : result.Rows.Max(r => r.Date);
                string notes = string.Join(' ', result.Problems);
                import.Notes = notes.Length == 0 ? null : (notes.Length > 2000 ? notes[..2000] : notes);
                await db.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);

        // Con las métricas nuevas guardadas se buscan anomalías: es lo que le quita la autonomía al modo automático.
        await execution.ReportAsync(90, "Buscando anomalías", cancellationToken);
        await anomalies.ScanAsync(cancellationToken);
    }

    // Crea lo que falte de la estructura y deja una fila por anuncio y día. Si la fuente no trae el identificador de
    // la cuenta, la campaña o el conjunto, se usa su nombre como identificador para no duplicarlos entre importaciones.
    private async Task SaveAsync(List<MetaCsvRow> rows, string currency, CancellationToken cancellationToken)
    {
        Dictionary<string, MetaAdAccount> accounts = await db.MetaAdAccounts.ToDictionaryAsync(a => a.ExternalId, cancellationToken);
        Dictionary<string, MetaCampaign> campaigns = (await db.MetaCampaigns.ToListAsync(cancellationToken))
            .ToDictionary(c => $"{c.MetaAdAccountId}|{c.ExternalId}");
        Dictionary<string, MetaAdSet> adSets = (await db.MetaAdSets.ToListAsync(cancellationToken))
            .ToDictionary(s => $"{s.MetaCampaignId}|{s.ExternalId}");
        Dictionary<string, MetaAd> ads = await db.MetaAds.ToDictionaryAsync(a => a.ExternalId, cancellationToken);

        foreach (MetaCsvRow row in rows.Where(r => !ads.ContainsKey(r.AdId)).GroupBy(r => r.AdId).Select(g => g.First()))
        {
            string accountKey = row.AccountId ?? "csv";
            if (!accounts.TryGetValue(accountKey, out MetaAdAccount? account))
            {
                account = new MetaAdAccount { ExternalId = accountKey, Name = Limit(row.AccountName ?? "Cuenta importada", 200), Currency = currency };
                db.MetaAdAccounts.Add(account);
                await db.SaveChangesAsync(cancellationToken);
                accounts[accountKey] = account;
            }

            string campaignName = Limit(row.CampaignName ?? "Sin campaña", 300);
            string campaignExternal = Limit(row.CampaignId ?? $"nombre:{campaignName}", 300);
            if (!campaigns.TryGetValue($"{account.Id}|{campaignExternal}", out MetaCampaign? campaign))
            {
                campaign = new MetaCampaign { MetaAdAccountId = account.Id, ExternalId = campaignExternal, Name = campaignName };
                db.MetaCampaigns.Add(campaign);
                await db.SaveChangesAsync(cancellationToken);
                campaigns[$"{account.Id}|{campaignExternal}"] = campaign;
            }

            string adSetName = Limit(row.AdSetName ?? "Sin conjunto", 300);
            string adSetExternal = Limit(row.AdSetId ?? $"nombre:{adSetName}", 300);
            if (!adSets.TryGetValue($"{campaign.Id}|{adSetExternal}", out MetaAdSet? adSet))
            {
                adSet = new MetaAdSet { MetaCampaignId = campaign.Id, ExternalId = adSetExternal, Name = adSetName };
                db.MetaAdSets.Add(adSet);
                await db.SaveChangesAsync(cancellationToken);
                adSets[$"{campaign.Id}|{adSetExternal}"] = adSet;
            }

            MetaAd ad = new() { MetaAdSetId = adSet.Id, ExternalId = Limit(row.AdId, 100), Name = Limit(row.AdName, 300) };
            db.MetaAds.Add(ad);
            await db.SaveChangesAsync(cancellationToken);
            ads[row.AdId] = ad;
        }

        // Un día repetido dentro del mismo archivo: vale la última fila.
        List<MetaCsvRow> unique = rows.GroupBy(r => (r.AdId, r.Date)).Select(g => g.Last()).ToList();
        List<long> adIds = unique.Select(r => ads[r.AdId].Id).Distinct().ToList();
        DateOnly from = unique.Count == 0 ? default : unique.Min(r => r.Date);
        DateOnly to = unique.Count == 0 ? default : unique.Max(r => r.Date);
        Dictionary<(long, DateOnly), MetaInsightDaily> existing = (await db.MetaInsightsDaily
                .Where(i => adIds.Contains(i.MetaAdId) && i.Date >= from && i.Date <= to)
                .ToListAsync(cancellationToken))
            .ToDictionary(i => (i.MetaAdId, i.Date));
        foreach (MetaCsvRow row in unique)
        {
            long adId = ads[row.AdId].Id;
            if (!existing.TryGetValue((adId, row.Date), out MetaInsightDaily? insight))
            {
                insight = new MetaInsightDaily { MetaAdId = adId, Date = row.Date };
                db.MetaInsightsDaily.Add(insight);
            }

            insight.Spend = row.Spend;
            insight.Impressions = row.Impressions;
            insight.Clicks = row.Clicks;
            insight.VideoPlays = row.VideoPlays;
            insight.ThruPlays = row.ThruPlays;
            insight.Purchases = row.Purchases;
            insight.PurchaseValue = row.PurchaseValue;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Limit(string value, int max)
    {
        string text = value.Trim();
        return text.Length > max ? text[..max] : text;
    }
}
