// <copyright file="JsonConverter_float3.cs" company="BitcoderCZ">
// Copyright (c) BitcoderCZ. All rights reserved.
// </copyright>

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Solace.BuildplateRenderer.Models.ResourcePacks;

namespace Solace.BuildplateRenderer.JsonConverters;

internal sealed class JsonConverter_UVCoordinates : JsonConverter<UVCoordinates>
{
	public override UVCoordinates Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType is JsonTokenType.StartArray)
		{
			float x1 = ReadNextFloat(ref reader);
			float y1 = ReadNextFloat(ref reader);
			float x2 = ReadNextFloat(ref reader);
			float y2 = ReadNextFloat(ref reader);

			if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
			{
				throw new JsonException("Expected EndArray for UVCoordinates.");
			}

			return new UVCoordinates(x1, y1, x2, y2);
		}

		throw new JsonException($"Unexpected token {reader.TokenType}. Expected StartObject or StartArray.");

		float ReadNextFloat(ref Utf8JsonReader r)
		{
			if (!r.Read() || r.TokenType != JsonTokenType.Number)
			{
				throw new JsonException("Expected number in UVCoordinates array.");
			}

			return r.GetSingle();
		}
	}

    public override void Write(Utf8JsonWriter writer, UVCoordinates value, JsonSerializerOptions options)
		=> throw new NotImplementedException();
}
