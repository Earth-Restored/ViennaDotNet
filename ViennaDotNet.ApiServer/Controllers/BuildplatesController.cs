using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Security.Claims;
using System.Text;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Types.Buildplates;
using ViennaDotNet.ApiServer.Types.Common;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.ObjectStore.Client;
using Buildplates = ViennaDotNet.DB.Models.Player.Buildplates;

namespace ViennaDotNet.ApiServer.Controllers
{
    [Authorize]
    [ApiVersion("1.1")]
    [Route("1/api/v{version:apiVersion}")]
    public class BuildplatesController : ControllerBase
    {
        private static EarthDB earthDB => Program.DB;
        private static ObjectStoreClient objectStoreClient => Program.objectStore;
        private static BuildplateInstancesManager buildplateInstancesManager => Program.buildplateInstancesManager;

        [HttpGet]
        [Route("buildplates")]
        public IActionResult GetBuildplates()
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId))
                return BadRequest();

            Buildplates buildplatesModel;
            try
            {
                EarthDB.Results results = new EarthDB.Query(false)
                    .Get("buildplates", playerId, typeof(Buildplates))
                    .Execute(earthDB);
                buildplatesModel = (Buildplates)results.Get("buildplates").Value;
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }

            // not null is ensured in .Where
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.
            OwnedBuildplate[] ownedBuildplates = buildplatesModel.getBuildplates().Select(buildplateEntry =>
            {
                byte[]? previewData = objectStoreClient.get(buildplateEntry.buildplate.previewObjectId).Task.Result as byte[]/*.join()*/;
                if (previewData == null)
                {
                    Log.Error($"Preview object {buildplateEntry.buildplate.previewObjectId} for buildplate {buildplateEntry.id} could not be loaded from object store");
                    return null;
                }
                string model = Encoding.ASCII.GetString(previewData);
                return new OwnedBuildplate(
                    buildplateEntry.id,
                    "00000000-0000-0000-0000-000000000000",
                    new Dimension(buildplateEntry.buildplate.size, buildplateEntry.buildplate.size),
                    new Offset(0, buildplateEntry.buildplate.offset, 0),
                    buildplateEntry.buildplate.scale,
                    OwnedBuildplate.Type.SURVIVAL,
                    SurfaceOrientation.HORIZONTAL,
                    model,
                    0,    // TODO
                    false,    // TODO
                    0,    // TODO
                    false,    // TODO
                    TimeFormatter.FormatTime(buildplateEntry.buildplate.lastModified),
                    0,    // TODO
                    ""
                );
            }).Where(ownedBuildplate => ownedBuildplate != null).ToArray();
#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.

            string resp = JsonConvert.SerializeObject(new EarthApiResponse(ownedBuildplates));
            return Content(resp, "application/json");
        }

        [HttpPost]
        [Route("multiplayer/buildplate/{buildplateId}/instances")]
        public async Task<IActionResult> CreateInstance(string buildplateId)
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId))
                return BadRequest();

            Buildplates.Buildplate? buildplate;
            try
            {
                EarthDB.Results results = new EarthDB.Query(false)
                    .Get("buildplates", playerId, typeof(Buildplates))
                    .Execute(earthDB);
                buildplate = ((Buildplates)results.Get("buildplates").Value).getBuildplate(buildplateId);
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }

            if (buildplate is null)
                return BadRequest();

            string? instanceId = buildplateInstancesManager.startBuildplateInstance(playerId, buildplateId, buildplate.night);
            if (instanceId is null)
                return BadRequest();

            BuildplateInstancesManager.InstanceInfo? instanceInfo = buildplateInstancesManager.getInstanceInfo(instanceId);
            if (instanceInfo is null)
                return BadRequest();

            BuildplateInstance buildplateInstance = instanceInfoToApiResponse(buildplate, instanceInfo);

            string resp = JsonConvert.SerializeObject(new EarthApiResponse(buildplateInstance));
            return Content(resp, "application/json");
        }

        // TODO: should we restrict this to matching player ID?
        [HttpGet]
        [Route("multiplayer/partitions/{partitionId}/instances/{instanceId}")]
        public async Task<IActionResult> GetInstanceStatus(string partitionId, string instanceId)
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId))
                return BadRequest();

            BuildplateInstancesManager.InstanceInfo? instanceInfo = buildplateInstancesManager.getInstanceInfo(instanceId);
            if (instanceInfo is null)
                return NotFound();

            Buildplates.Buildplate? buildplate;
            try
            {
                EarthDB.Results results = new EarthDB.Query(false)
                        .Get("buildplates", playerId, typeof(Buildplates))
                        .Execute(earthDB);
                buildplate = ((Buildplates)results.Get("buildplates").Value).getBuildplate(instanceInfo.buildplateId);
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }

            if (buildplate is null)
                return NotFound();

            // TODO: the client is supposed to poll until the buildplate server is ready, but instead it just crashes if we tell it that the buildplate server is not ready yet
            // TODO: so instead we just stall the request until it's ready, this is really ugly and eventually we need to figure out why it's crashing and implement this properly
            // TODO: this also relies on the buildplate server starting in less than ~20 seconds as the client will eventually time out the HTTP request and crash anyway
            //BuildplateInstance buildplateInstance = instanceInfoToApiResponse(buildplate, instanceInfo);
            BuildplateInstancesManager.InstanceInfo? instanceInfo1;
            int waitCount = 0;
            do
            {
                instanceInfo1 = buildplateInstancesManager.getInstanceInfo(instanceId);
                if (instanceInfo1 is null)
                    return NotFound();

                if (!instanceInfo1.ready)
                {
                    try
                    {
                        // TODO: Thread.Sleep instead?
                        await Task.Delay(1000);
                        //Thread.sleep(1000);
                    }
                    catch (ThreadAbortException)
                    {
                        continue;
                    }

                    waitCount++;
                }
            }
            while (!instanceInfo1.ready && waitCount < 30);
            BuildplateInstance buildplateInstance = instanceInfoToApiResponse(buildplate, instanceInfo1);

            string resp = JsonConvert.SerializeObject(new EarthApiResponse(buildplateInstance));
            return Content(resp, "application/json");
        }

        private static BuildplateInstance instanceInfoToApiResponse(Buildplates.Buildplate buildplate, BuildplateInstancesManager.InstanceInfo instanceInfo)
        {
            return new BuildplateInstance(
                instanceInfo.instanceId,
                "00000000-0000-0000-0000-000000000000",
                "d.projectearth.dev",    // TODO
                instanceInfo.address,
                instanceInfo.port,
                instanceInfo.ready,
                instanceInfo.ready ? BuildplateInstance.ApplicationStatus.READY : BuildplateInstance.ApplicationStatus.UNKNOWN,
                instanceInfo.ready ? BuildplateInstance.ServerStatus.RUNNING : BuildplateInstance.ServerStatus.RUNNING,
                JsonConvert.SerializeObject(new Dictionary<string, object>()
                {
                    { "buildplateid", instanceInfo.buildplateId }
                }),
                new BuildplateInstance.GameplayMetadata(
                    instanceInfo.buildplateId,
                    "00000000-0000-0000-0000-000000000000",
                    instanceInfo.playerId,
                    "2020.1217.02",
                    "CK06Yzm2",    // TODO
                    new Dimension(buildplate.size, buildplate.size),
                    new Offset(0, buildplate.offset, 0),
                    buildplate.scale,
                    false,    // TODO
                    BuildplateInstance.GameplayMetadata.GameplayMode.BUILDPLATE,    // TODO
                    SurfaceOrientation.HORIZONTAL,
                    null,
                    null,    // TODO
                    new BuildplateInstance.GameplayMetadata.ShutdownBehavior[] 
                    { 
                        BuildplateInstance.GameplayMetadata.ShutdownBehavior.ALL_PLAYERS_QUIT,
                        BuildplateInstance.GameplayMetadata.ShutdownBehavior.HOST_PLAYER_QUITS 
                    },
                    new BuildplateInstance.GameplayMetadata.SnapshotOptions(
                        BuildplateInstance.GameplayMetadata.SnapshotOptions.SnapshotWorldStorage.BUILDPLATE,
                        new BuildplateInstance.GameplayMetadata.SnapshotOptions.SaveState(
                                false,
                                false,
                                false,
                                true,
                                true,
                                true
                            ),
                        BuildplateInstance.GameplayMetadata.SnapshotOptions.SnapshotTriggerConditions.NONE,
                        new BuildplateInstance.GameplayMetadata.SnapshotOptions.TriggerCondition[] 
                        { 
                            BuildplateInstance.GameplayMetadata.SnapshotOptions.TriggerCondition.INTERVAL, BuildplateInstance.GameplayMetadata.SnapshotOptions.TriggerCondition.PLAYER_EXITS },
                        TimeFormatter.FormatDuration(30 * 1000)
                    ),
                    new Dictionary<string, object>()
                ),
                "776932eeeb69",
                //new Coordinate(50.99636722700025f, -0.7234904312500047f)
                new Coordinate(0.0f, 0.0f)    // TODO
            );
        }
    }
}
