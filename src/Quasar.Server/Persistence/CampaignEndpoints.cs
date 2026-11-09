namespace Quasar.Server.Persistence;

internal static class CampaignEndpoints
{
    public static void MapCampaignEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/campaigns");
        api.MapGet("/status", (LabSession session, PersistenceSettings settings) =>
        {
            var status = session.CampaignStatus();
            return Results.Ok(new
            {
                status.InstanceId,
                status.Revision,
                status.Running,
                status.Dirty,
                status.Campaign,
                status.LastCheckpoint,
                status.RejectedCheckpoints,
                status.RecoveredFromCampaignId,
                status.RecoveryError,
                status.AutosaveError,
                settings.Autosave,
                settings.SaveOnShutdown
            });
        });
        api.MapGet("", (ICampaignStore store, ILogger<CampaignLifecycle> logger) => Respond(logger, () =>
            Results.Ok(store.ListCampaigns().Select(entry => new
            {
                entry.Id,
                entry.Campaign,
                Error = entry.Error is null ? null : "Campaign metadata could not be read."
            }))));
        api.MapGet("/{campaignId:guid}/checkpoints", (Guid campaignId, ICampaignStore store,
            ILogger<CampaignLifecycle> logger) => Respond(logger, () => Results.Ok(store.DiscoverCheckpoints(campaignId))));

        api.MapPost("/new", (NewCampaignRequest request, CampaignLifecycle lifecycle, ILogger<CampaignLifecycle> logger) =>
            Respond(logger, () => Results.Json(lifecycle.Create(request.Version, request.DisplayName), statusCode: 201)));
        api.MapPost("/save", (SessionVersion version, CampaignLifecycle lifecycle, ILogger<CampaignLifecycle> logger) =>
            Respond(logger, () => Results.Json(lifecycle.Save(version, "Manual save"), statusCode: 201)));
        api.MapPost("/save-named", (NamedSaveRequest request, CampaignLifecycle lifecycle, ILogger<CampaignLifecycle> logger) =>
            Respond(logger, () => Results.Json(lifecycle.Save(request.Version, request.Label), statusCode: 201)));
        api.MapPost("/load", (LoadCheckpointRequest request, CampaignLifecycle lifecycle, ILogger<CampaignLifecycle> logger) =>
            Respond(logger, () => Results.Ok(lifecycle.Load(request.Version, request.CampaignId, request.CheckpointId))));
        api.MapPost("/continue", (ContinueCampaignRequest request, CampaignLifecycle lifecycle, ILogger<CampaignLifecycle> logger) =>
            Respond(logger, () => Results.Ok(lifecycle.Continue(request.Version, request.CampaignId))));
    }

    internal static IResult Unavailable(CampaignUnavailableException error) => Results.Json(new
    {
        code = error.RecoveryFailed ? "recovery_failed" : "no_active_campaign",
        error = error.Message
    }, statusCode: error.RecoveryFailed ? 422 : 409);

    private static IResult Respond(ILogger logger, Func<IResult> operation)
    {
        try { return operation(); }
        catch (CampaignUnavailableException error) { return Unavailable(error); }
        catch (CheckpointConflictException)
        {
            return Results.Conflict(new { code = "checkpoint_conflict", error = "The stored campaign changed. Reload it before saving again." });
        }
        catch (InvalidOperationException error)
        {
            return Results.Conflict(new { code = "session_conflict", error = error.Message });
        }
        catch (ArgumentException error)
        {
            return Results.BadRequest(new { code = "invalid_request", error = error.Message });
        }
        catch (Exception error) when (error is FileNotFoundException or KeyNotFoundException)
        {
            return Results.NotFound(new { code = "not_found", error = "Campaign or checkpoint not found." });
        }
        catch (Exception error) when (CampaignLifecycle.IsStorageFailure(error))
        {
            logger.LogWarning(error, "Campaign operation failed");
            var invalid = CampaignLifecycle.IsInvalidData(error);
            return Results.Json(new
            {
                code = invalid ? "invalid_campaign_data" : "storage_unavailable",
                error = invalid ? "Campaign or checkpoint data is corrupt or incompatible." : "Campaign storage is unavailable."
            }, statusCode: invalid ? 422 : 503);
        }
    }

    private sealed record NewCampaignRequest(string InstanceId, long Revision, string DisplayName)
    {
        public SessionVersion Version => new(InstanceId, Revision);
    }

    private sealed record NamedSaveRequest(string InstanceId, long Revision, string Label)
    {
        public SessionVersion Version => new(InstanceId, Revision);
    }

    private sealed record LoadCheckpointRequest(string InstanceId, long Revision, Guid CampaignId, Guid CheckpointId)
    {
        public SessionVersion Version => new(InstanceId, Revision);
    }

    private sealed record ContinueCampaignRequest(string InstanceId, long Revision, Guid CampaignId)
    {
        public SessionVersion Version => new(InstanceId, Revision);
    }
}
