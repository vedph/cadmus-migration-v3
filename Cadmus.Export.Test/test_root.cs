using Cadmus.Export.Renderers;
using System;

var json = "{\"name\":\"test\"}";
var converter = new JsonToXmlConverter();
var result1 = converter.Convert(json);
Console.WriteLine("Without wrapping:");
Console.WriteLine(result1.ToString());

var wrappedJson = "{\"root\":" + json + "}";
var result2 = converter.Convert(wrappedJson);
Console.WriteLine("\nWith wrapping:");
Console.WriteLine(result2.ToString());
