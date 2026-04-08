// <copyright file="JsonConverter_double3.cs" company="BitcoderCZ">
// Copyright (c) BitcoderCZ. All rights reserved.
// </copyright>

using System.Text.Json;
using System.Text.Json.Serialization;
using BitcoderCZ.Maths.Vectors;

namespace ViennaDotNet.BuildplateRenderer.JsonConverters;

internal sealed class JsonConverter_double3 : JsonConverter<double3>
{
	public override double3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartObject)
		{
			throw new JsonException($"Unexpected token {reader.TokenType}, expected StartObject.");
		}

		double x = 0, y = 0, z = 0;

		string propertyX = options.PropertyNamingPolicy?.ConvertName(nameof(double3.X)) ?? nameof(double3.X);
		string propertyY = options.PropertyNamingPolicy?.ConvertName(nameof(double3.Y)) ?? nameof(double3.Y);
		string propertyZ = options.PropertyNamingPolicy?.ConvertName(nameof(double3.Z)) ?? nameof(double3.Z);

		while (reader.Read())
		{
			if (reader.TokenType == JsonTokenType.EndObject)
			{
				return new double3(x, y, z);
			}

			if (reader.TokenType == JsonTokenType.PropertyName)
			{
				string? propertyName = reader.GetString();
				reader.Read();

				if (StringEquals(propertyName, propertyX))
				{
					x = reader.GetSingle();
				}
#pragma warning disable IDE0045 // Convert to conditional expression
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
#pragma warning restore IDE0045 // Convert to conditional expression
			}
		}

		throw new JsonException("Unexpected end of JSON.");

		bool StringEquals(string? a, string? b)
		{
			return a is null || b is null
				? a is null && b is null
				: options.PropertyNameCaseInsensitive
				? a.Equals(b, StringComparison.OrdinalIgnoreCase)
				: a.Equals(b, StringComparison.Ordinal);
		}
	}

	public override void Write(Utf8JsonWriter writer, double3 value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();

		string propertyX = options.PropertyNamingPolicy?.ConvertName(nameof(double3.X)) ?? nameof(double3.X);
		string propertyY = options.PropertyNamingPolicy?.ConvertName(nameof(double3.Y)) ?? nameof(double3.Y);
		string propertyZ = options.PropertyNamingPolicy?.ConvertName(nameof(double3.Z)) ?? nameof(double3.Z);

		writer.WriteNumber(propertyX, value.X);
		writer.WriteNumber(propertyY, value.Y);
		writer.WriteNumber(propertyZ, value.Z);

		writer.WriteEndObject();
	}
}
