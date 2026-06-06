using System;
using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

class Program {
    static void Main() {
        var source = File.ReadAllText("C:\Users\Administrator\AppData\Local\OpenQuickHost\Extensions\clipboard-history\main.cs");
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create("Test", new[] { syntaxTree }, new[] { 
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Windows.Window).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Windows.Controls.Control).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Windows.Forms.Form).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Windows.Input.ICommand).Assembly.Location)
        });
        var emitResult = compilation.Emit("test.dll");
        foreach(var diag in emitResult.Diagnostics) {
            Console.WriteLine(diag);
        }
        if (emitResult.Success) {
            Console.WriteLine("Compilation OK");
        }
    }
}
