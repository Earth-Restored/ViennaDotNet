// <copyright file="JsonConverter_float3.cs" company="BitcoderCZ">
// Copyright (c) BitcoderCZ. All rights reserved.
// </copyright>

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ViennaDotNet.BuildplateRenderer.JsonConverters;

internal sealed class JsonConverter_Vector3 : JsonConverter<Vector3>
{
	public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType is JsonTokenType.StartArray)
		{
			float x = ReadNextFloat(ref reader);
			float y = ReadNextFloat(ref reader);
			float z = ReadNextFloat(ref reader);

			if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
			{
				throw new JsonException("Expected EndArray for Vector3.");
			}

			return new Vector3(x, y, z);
		}

		if (reader.TokenType is JsonTokenType.StartObject)
		{
			float x = 0, y = 0, z = 0;

			string propertyX = options.PropertyNamingPolicy?.ConvertName(nameof(Vector3.X)) ?? nameof(Vector3.X);
			string propertyY = options.PropertyNamingPolicy?.ConvertName(nameof(Vector3.Y)) ?? nameof(Vector3.Y);
			string propertyZ = options.PropertyNamingPolicy?.ConvertName(nameof(Vector3.Z)) ?? nameof(Vector3.Z);

			while (reader.Read())
			{
				if (reader.TokenType is JsonTokenType.EndObject)
				{
					return new Vector3(x, y, z);
				}

				if (reader.TokenType is JsonTokenType.PropertyName)
				{
					string? propertyName = reader.GetString();
					reader.Read();

					if (StringEquals(propertyName, propertyX))
					{
						x = reader.GetSingle();
					}
					else if (StringEquals(propertyName, propertyY))
					{
						y = reader.GetSingle();
					}
					else if (StringEquals(propertyName, propertyZ))
					{
						z = reader.GetSingle();
					}
					else
					{
						throw new JsonException($"Unknown property {propertyName}");
					}
				}
			}
		}

		throw new JsonException($"Unexpected token {reader.TokenType}. Expected StartObject or StartArray.");

		float ReadNextFloat(ref Utf8JsonReader r)
		{
			if (!r.Read() || r.TokenType != JsonTokenType.Number)
			{
				throw new JsonException("Expected number in Vector3 array.");
			}

			return r.GetSingle();
		}

		bool StringEquals(string? a, string? b)
		{
			return a is null || b is null
				? a is null && b is null
				: options.PropertyNameCaseInsensitive
				? a.Equals(b, StringComparison.OrdinalIgnoreCase)
				: a.Equals(b, StringComparison.Ordinal);
		}
	}

	public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();

		string propertyX = options.PropertyNamingPolicy?.ConvertName(nameof(Vector3.X)) ?? nameof(Vector3.X);
		string propertyY = options.PropertyNamingPolicy?.ConvertName(nameof(Vector3.Y)) ?? nameof(Vector3.Y);
		string propertyZ = options.PropertyNamingPolicy?.ConvertName(nameof(Vector3.Z)) ?? nameof(Vector3.Z);

		writer.WriteNumber(propertyX, value.X);
		writer.WriteNumber(propertyY, value.Y);
		writer.WriteNumber(propertyZ, value.Z);

		writer.WriteEndObject();
	}
}
