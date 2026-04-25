// <copyright file="JsonUtils.cs" company="BitcoderCZ">
// Copyright (c) BitcoderCZ. All rights reserved.
// </copyright>

using System.Text.Json;
using ViennaDotNet.BuildplateRenderer.JsonConverters;

namespace ViennaDotNet.BuildplateRenderer.Utils;

internal static class JsonUtils
{
	private static readonly JsonSerializerOptions DefaultJsonOptions = new JsonSerializerOptions()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
	};

	static JsonUtils()
	{
		DefaultJsonOptions.Converters.Add(new JsonConverter_float2());
		DefaultJsonOptions.Converters.Add(new JsonConverter_int3());
		DefaultJsonOptions.Converters.Add(new JsonConverter_Vector3());
		DefaultJsonOptions.Converters.Add(new JsonConverter_float3());
		DefaultJsonOptions.Converters.Add(new JsonConverter_double3());
		DefaultJsonOptions.Converters.Add(new JsonConverter_UVCoordinates());
		DefaultJsonOptions.Converters.Add(new VariantModelArrayConverter());
	}

	public static T? DeserializeJson<T>(ReadOnlySpan<char> json)
		=> JsonSerializer.Deserialize<T>(json, DefaultJsonOptions);

	public static T? DeserializeJson<T>(ReadOnlySpan<byte> utf8Json)
		=> JsonSerializer.Deserialize<T>(utf8Json, DefaultJsonOptions);

	public static T? DeserializeJson<T>(Stream stream)
		=> JsonSerializer.Deserialize<T>(stream, DefaultJsonOptions);

	public static async Task<T?> DeserializeJsonAsync<T>(Stream stream, CancellationToken cancellationToken = default)
		=> await JsonSerializer.DeserializeAsync<T>(stream, DefaultJsonOptions, cancellationToken);

	public static string SerializeJson<T>(T value)
		=> JsonSerializer.Serialize(value, DefaultJsonOptions);
}
