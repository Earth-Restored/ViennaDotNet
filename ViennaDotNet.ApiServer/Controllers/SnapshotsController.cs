using Microsoft.AspNetCore.Mvc;
using Serilog;
using System;
using System.Buffers.Text;
using System.Text;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.ObjectStore.Client;

namespace ViennaDotNet.ApiServer.Controllers
{
    [Route("buildplate")]
    public class SnapshotsController : ControllerBase
    {
        private static EarthDB earthDB => Program.DB;
        private static ObjectStoreClient objectStoreClient => Program.objectStore;
        private static BuildplatePreviewGenerator buildplatePreviewGenerator => Program.buildplatePreviewGenerator;

        [HttpGet]
        [Route("snapshot/{playerId}/{buildplateId}")]
        public IActionResult GetSnapshot(string playerId, string buildplateId)
        {
            Buildplates buildplates;
            try
            {
                EarthDB.Results results = new EarthDB.Query(false)
                    .Get("buildplates", playerId, typeof(Buildplates))
                    .Execute(earthDB);
                buildplates = (Buildplates)results.Get("buildplates").Value;
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }

            Buildplates.Buildplate? buildplate = buildplates.getBuildplate(buildplateId);

            if (buildplate == null)
                return NotFound();

            byte[]? serverData = (byte[]?)objectStoreClient.get(buildplate.serverDataObjectId).Task.Result;
            if (serverData == null)
            {
                Log.Error($"Data object {buildplate.serverDataObjectId} for buildplate {buildplateId} could not be loaded from object store");
                return StatusCode(500); // Internal Server Error
            }

            return File(serverData, "application/octet-stream");
        }

        [HttpPost]
        [Route("snapshot/{playerId}/{buildplateId}")]
        public async Task<IActionResult> PostSnapshot(string playerId, string buildplateId)
        {
            // TODO: it would be nicer to just send the data in binary/bytes form rather than as base64, but HttpServletRequest apparently doesn't provide that???
            byte[] serverData;
            try
            {
                serverData = Convert.FromBase64String(await Request.Body.ReadAsString());
            }
            catch
            {
                return BadRequest();
            }

            // request.timestamp
            long requestStartedOn = ((DateTime)HttpContext.Items["RequestStartedOn"]!).ToUnixTimeMilliseconds();

            Buildplates.Buildplate? buildplateUnsafeForPreviewGenerator;
            try
            {
                EarthDB.Results results = new EarthDB.Query(false)
                        .Get("buildplates", playerId, typeof(Buildplates))
                        .Execute(earthDB);
                buildplateUnsafeForPreviewGenerator = ((Buildplates)results.Get("buildplates").Value).getBuildplate(buildplateId);

                if (buildplateUnsafeForPreviewGenerator == null)
                    return NotFound();
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }

            string? preview = buildplatePreviewGenerator.generatePreview(buildplateUnsafeForPreviewGenerator, serverData);
            if (preview == null)
                Log.Warning("Could not generate preview for buildplate");

            string? serverDataObjectId = (string?)objectStoreClient.store(serverData).Task.Result;
            if (serverDataObjectId == null)
            {
                Log.Error($"Could not store new data object for buildplate {buildplateId} in object store");
                return StatusCode(500); // Internal Server Error
            }
            string? previewObjectId;
            if (preview != null)
            {
                previewObjectId = (string?)objectStoreClient.store(Encoding.ASCII.GetBytes(preview)).Task.Result;
                if (previewObjectId == null)
                    Log.Warning($"Could not store new preview object for buildplate {buildplateId} in object store");
            }
            else
                previewObjectId = null;

            try
            {
                EarthDB.Results results = new EarthDB.Query(true)
                    .Get("buildplates", playerId, typeof(Buildplates))
                    .Then(results1 =>
                    {
                        Buildplates buildplates = (Buildplates)results1.Get("buildplates").Value;
                        Buildplates.Buildplate? buildplate = buildplates.getBuildplate(buildplateId);
                        if (buildplate != null)
                        {
                            buildplate.lastModified = requestStartedOn;

                            string oldServerDataObjectId = buildplate.serverDataObjectId;
                            buildplate.serverDataObjectId = serverDataObjectId;

                            string oldPreviewObjectId;
                            if (previewObjectId != null)
                            {
                                oldPreviewObjectId = buildplate.previewObjectId;
                                buildplate.previewObjectId = previewObjectId;
                            }
                            else
                                oldPreviewObjectId = "";

                            return new EarthDB.Query(true)
                                .Update("buildplates", playerId, buildplates)
                                .Extra("exists", true)
                                .Extra("oldServerDataObjectId", oldServerDataObjectId)
                                .Extra("oldPreviewObjectId", oldPreviewObjectId);
                        }
                        else
                            return new EarthDB.Query(false)
                                .Extra("exists", false);
                    })
                    .Execute(earthDB);

                bool exists = (bool)results.getExtra("exists");
                if (exists)
                {
                    string oldServerDataObjectId = (string)results.getExtra("oldServerDataObjectId");
                    objectStoreClient.delete(oldServerDataObjectId);

                    string oldPreviewObjectId = (string)results.getExtra("oldPreviewObjectId");
                    if (!string.IsNullOrEmpty(oldPreviewObjectId))
                        objectStoreClient.delete(oldPreviewObjectId);

                    Log.Information($"Stored new snapshot for buildplate {buildplateId}");

                    return Ok();
                }

                else
                {
                    objectStoreClient.delete(serverDataObjectId);
                    return NotFound();
                }
            }
            catch (EarthDB.DatabaseException exception)
            {
                objectStoreClient.delete(serverDataObjectId);

                throw new ServerErrorException(exception);
            }
        }
    }
}
