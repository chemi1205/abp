using System;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.DependencyInjection;

namespace Volo.Abp.Cli.ProjectModification;

public class PackageSourceManager : ITransientDependency
{
    public ILogger<PackageSourceManager> Logger { get; set; }

    public PackageSourceManager()
    {
        Logger = NullLogger<PackageSourceManager>.Instance;
    }

    public void Add(string solutionFolder, string sourceKey, string sourceValue, params string[] packageMappingPatterns)
    {
        var nugetConfigPath = GetNugetConfigPath(solutionFolder);

        Logger.LogInformation($"Adding \"{sourceValue}\" ({sourceKey}) to nuget sources...");

        if (!File.Exists(nugetConfigPath))
        {
            File.WriteAllText(nugetConfigPath, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                          "<configuration>\n" +
                          "    <packageSources>\n" +
                          "        <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />\n" +
                          $"        <add key=\"{sourceKey}\" value=\"{sourceValue}\" />\n" +
                          "    </packageSources>\n" +
                          "</configuration>");
            return;
        }

        var fileContent = File.ReadAllText(nugetConfigPath);

        if (fileContent.Contains($"\"{sourceValue}\""))
        {
            return;
        }

        try
        {
            var doc = new XmlDocument() { PreserveWhitespace = true };

            doc.Load(GenerateStreamFromString(fileContent));

            var sourceNodes = doc.SelectNodes("/configuration/packageSources");

            var newNode = doc.CreateElement("add");

            var includeAttr = doc.CreateAttribute("key");
            includeAttr.Value = sourceKey;
            newNode.Attributes.Append(includeAttr);

            var versionAttr = doc.CreateAttribute("value");
            versionAttr.Value = sourceValue;
            newNode.Attributes.Append(versionAttr);

            sourceNodes?[0]?.AppendChild(newNode);

            if (packageMappingPatterns.Any())
            {
                ConfigurePackageSourceMappings(doc, sourceKey, packageMappingPatterns);
            }

            File.WriteAllText(nugetConfigPath, doc.OuterXml);
        }
        catch
        {
            Logger.LogWarning($"Adding \"{sourceValue}\" ({sourceKey}) to nuget sources FAILED.");
        }
    }

    protected virtual void ConfigurePackageSourceMappings(XmlDocument doc, string sourceKey, string[] packageMappingPatterns)
    {
        var packageMappingNodes = doc.SelectNodes("/configuration/packageSourceMapping");

        if (packageMappingNodes == null)
        {
            // If there is no packageSourceMapping node, leave it as it is.
            Logger.LogWarning($"<packageSourceMapping> node not found in 'NuGet.Config' file. Skipping adding patterns for '{sourceKey}' source.");
            return;
        }
        var packageSourceNode = doc.CreateElement("packageSource");
        var sourceAttr = doc.CreateAttribute("key");
        sourceAttr.Value = sourceKey;
        packageSourceNode.Attributes.Append(sourceAttr);
        packageMappingNodes[0]?.AppendChild(packageSourceNode);
        foreach (var pattern in packageMappingPatterns)
        {
            var packageNode = doc.CreateElement("package");
            var patternAttr = doc.CreateAttribute("pattern");
            patternAttr.Value = pattern;
            packageNode.Attributes.Append(patternAttr);
            packageSourceNode.AppendChild(packageNode);
        }
    }

    public void Remove(string solutionFolder, string sourceKey)
    {
        var nugetConfigPath = GetNugetConfigPath(solutionFolder);

        if (!File.Exists(nugetConfigPath))
        {
            return;
        }

        var fileContent = File.ReadAllText(nugetConfigPath);

        if (!fileContent.Contains($"\"{sourceKey}\""))
        {
            return;
        }

        Logger.LogInformation($"Removing \"{sourceKey}\" from nuget sources...");

        try
        {
            var doc = new XmlDocument() { PreserveWhitespace = true };

            doc.Load(GenerateStreamFromString(fileContent));

            var nodes = doc.SelectNodes($"/configuration/packageSources/add[@key='{sourceKey}']");

            if (nodes != null && nodes.Count > 0)
            {
                nodes[0].ParentNode.RemoveChild(nodes[0]);
            }

            File.WriteAllText(nugetConfigPath, doc.OuterXml);
        }
        catch
        {
            Logger.LogWarning($"Removing \"{sourceKey}\" from nuget sources FAILED.");
        }
    }

    private static string GetNugetConfigPath(string solutionFolder)
    {
        return Path.Combine(solutionFolder, "NuGet.Config");
    }

    private static Stream GenerateStreamFromString(string s)
    {
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream);
        writer.Write(s);
        writer.Flush();
        stream.Position = 0;
        return stream;
    }
}
