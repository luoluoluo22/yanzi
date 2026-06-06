using System;
using System.Reflection;
using Microsoft.Web.WebView2.Core;

var type = typeof(CoreWebView2Cookie);
var prop = type.GetProperty("Expires");
Console.WriteLine(prop.PropertyType.FullName);
