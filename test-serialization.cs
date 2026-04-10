using System.Text.Json;

var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

// Test 1: Dictionary with nullable int values
var dict1 = new Dictionary<string, object?>();
dict1["staleThresholdSeconds"] = 120;
dict1["playerCacheExpirationSeconds"] = 900;
var json1 = JsonSerializer.Serialize(dict1, options);
Console.WriteLine("Test 1 (both values): " + json1);

// Test 2: Dictionary with only one value
var dict2 = new Dictionary<string, object?>();
dict2["staleThresholdSeconds"] = 120;
var json2 = JsonSerializer.Serialize(dict2, options);
Console.WriteLine("Test 2 (one value): " + json2);

// Test 3: Empty dictionary (no overrides)
var dict3 = new Dictionary<string, object?>();
var json3 = JsonSerializer.Serialize(dict3, options);
Console.WriteLine("Test 3 (no overrides): " + json3);

// Test 4: Deserialize and check roundtrip
var doc = JsonDocument.Parse(json1);
var root = doc.RootElement;
var stale = root.TryGetProperty("staleThresholdSeconds", out var prop) && prop.ValueKind == JsonValueKind.Number
    ? prop.GetInt32()
    : (int?)null;
Console.WriteLine("Test 4 (deserialize): staleThresholdSeconds = " + stale);
