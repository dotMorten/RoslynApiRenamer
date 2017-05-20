using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using System.Threading.Tasks;

namespace RoslynApiRefactor
{
    class Program
    {
        private static Solution solutionToAnalyze;
        private static Project sampleProjectToAnalyze;
        private static Dictionary<string, string> TypeRenameList = new Dictionary<string, string>();

        static void Main(string[] args)
        {
            // TODO: Make these arguments instead
            RefactorSolution(@"c:\Github\Xamarin\Xamarin.Forms\Xamarin.Forms.sln", @"RenameList.Xamarin.Forms.txt");
            Console.ReadKey();
        }

        private static async void RefactorSolution(string pathToSolution, string renameList)
        {
            MSBuildWorkspace workspace = MSBuildWorkspace.Create();
            solutionToAnalyze = await RunSpinnerAsync(workspace.OpenSolutionAsync(pathToSolution), "Loading solution... ");
            sampleProjectToAnalyze = solutionToAnalyze.Projects.Where((proj) => proj.Name == "Xamarin.Forms.Core").FirstOrDefault();

            Compilation compilation = await RunSpinnerAsync(sampleProjectToAnalyze.GetCompilationAsync(), "Compiling project...");

            // Dictionary<string, INamedTypeSymbol> symbols = new Dictionary<string, INamedTypeSymbol>();
            // FindAllSymbols(compilation.Assembly.GlobalNamespace, symbols);
            var sol = solutionToAnalyze;
            using (StreamReader sr = new StreamReader(renameList))
            {
                while (!sr.EndOfStream)
                {
                    var entry = sr.ReadLine();
                    bool changed = false;
                    if (entry.StartsWith("T:"))
                    {
                        var vals = entry.Substring(2).Split(';');
                        if(vals.Length != 2)
                        {
                            Console.WriteLine($"ERR: Invalid entry '{entry}'");
                            continue;
                        }
                        var className = vals[0];
                        var newName = vals[1];
                        var symbol = FindSymbol(className, compilation.Assembly.GlobalNamespace);
                        if(symbol == null)
                        {
                            Console.WriteLine($"ERR: Symbol {className} not found");
                            continue;
                        }
                        sol = await RunSpinnerAsync(Microsoft.CodeAnalysis.Rename.Renamer.RenameSymbolAsync(sol, symbol, newName, null), $"Renaming {className} => {newName}");
                        TypeRenameList[className] = className.Substring(0, className.LastIndexOf('.') + 1) + newName;
                        changed = true;
                    }
                    else if (entry.StartsWith("P:")) //properties
                    {
                        var vals = entry.Substring(2).Split(';');
                        if (vals.Length != 2)
                        {
                            Console.WriteLine($"ERR: Invalid entry '{entry}'");
                            continue;
                        }
                        var fullName = vals[0];
                        var className = fullName.Substring(0, fullName.LastIndexOf("."));
                        var propertyName = fullName.Substring(fullName.LastIndexOf(".") + 1);
                        var newName = vals[1];
                        var symbol = FindSymbol(className, propertyName, compilation.Assembly.GlobalNamespace);
                        if (symbol == null)
                        {
                            Console.WriteLine($"ERR: Symbol {fullName} not found");
                            continue;
                        }
                        sol = await RunSpinnerAsync(Microsoft.CodeAnalysis.Rename.Renamer.RenameSymbolAsync(sol, symbol, newName, null), $"Renaming {fullName} => {newName}");
                        if(symbol.Kind == SymbolKind.Property)
                        {
                            // check if dependency property, and if so fix field definition
                            sampleProjectToAnalyze = sol.Projects.Where((proj) => proj.Name == "Xamarin.Forms.Core").FirstOrDefault();
                            compilation = await sampleProjectToAnalyze.GetCompilationAsync();
                            changed = false;
                            var dp = FindSymbol(className, propertyName + "Property", compilation.Assembly.GlobalNamespace) as IFieldSymbol;
                            if(dp != null && (dp.Type.Name == "BindableProperty" || dp.Type.Name == "DependencyProperty"))
                            {
                                sol = await RunSpinnerAsync(Microsoft.CodeAnalysis.Rename.Renamer.RenameSymbolAsync(sol, dp, newName + "Property", null), $"Renaming {fullName}Property => {newName}Property");
                                //TODO: The string value inside the DependencyProperty needs to be updated as well.
                                changed = true;
                            }
                        }
                        changed = true;
                    }
                    if (changed)
                    {
                        sampleProjectToAnalyze = sol.Projects.Where((proj) => proj.Name == "Xamarin.Forms.Core").FirstOrDefault();
                        compilation = await sampleProjectToAnalyze.GetCompilationAsync();
                    }
                }
                Console.WriteLine("Saving workspace...");
                bool success = workspace.TryApplyChanges(sol);
                Console.WriteLine("Done!");
            }
        }
        private static ISymbol FindSymbol(string classname, string memberName, INamespaceSymbol namespaceSymbol)
        {
            var c = FindSymbol(classname, namespaceSymbol);
            if(c != null)
            {
                foreach(var member in c.GetMembers())
                {
                    if (member.Name == memberName)
                        return member;
                }
            }
            return null;
        }
        private static INamedTypeSymbol FindSymbol(string classname, INamespaceSymbol namespaceSymbol)
        {
            if (TypeRenameList.ContainsKey(classname))
                classname = TypeRenameList[classname];
            foreach (var type in namespaceSymbol.GetTypeMembers())
            {
                if (type.ContainingNamespace + "." + type.Name == classname)
                    return type;
            }
            foreach (var childNs in namespaceSymbol.GetNamespaceMembers())
            {
                var t = FindSymbol(classname, childNs);
                if (t != null)
                    return t;
            }
            return null;
        }

        private static void FindAllSymbols(INamespaceSymbol namespaceSymbol, Dictionary<string, INamedTypeSymbol> classes)
        {
            foreach (var type in namespaceSymbol.GetTypeMembers())
            {
                classes[type.ContainingNamespace + "." + type.Name] = type;
            }
            foreach (var childNs in namespaceSymbol.GetNamespaceMembers())
            {
                FindAllSymbols(childNs, classes);
            }
        }
        
        private static async Task<T> RunSpinnerAsync<T>(Task<T> task, string message)
        {
            char[] chars = new[] { '|', '/', '-', '\\' };
            int i = 0;
            Console.Write($"\r{message} {chars[0]}");
            while (!task.IsCompleted)
            {
                Console.Write($"\r{message} {chars[(++i) % 4]}");
                await Task.Delay(50);
            }
            Console.WriteLine("\r" + message + "  ");
            return await task;
        }
    }
}
