using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

public class AddReadmeToProjectAndAsmdefs : AssetPostprocessor
{
    private static string OnGeneratedCSProject(string path, string content)
    {
        string projectName = Path.GetFileNameWithoutExtension(path);

        UnityEngine.Debug.Log($"Generating {projectName}");

        List<string> mdFiles = GetMarkdownFilesForProject(projectName);

        if (mdFiles.Count == 0)
            return content;

        var builder = new StringBuilder();

        builder.AppendLine();
        builder.AppendLine("  <ItemGroup>");

        foreach (string file in mdFiles)
        {
            string relativePath = Path.GetRelativePath(
                Directory.GetCurrentDirectory(),
                file);

            relativePath = relativePath.Replace('\\', '/');
            relativePath = SecurityElement.Escape(relativePath);

            builder.AppendLine(
                $"    <None Include=\"{relativePath}\" />");
        }

        builder.AppendLine("  </ItemGroup>");

        return Regex.Replace(
            content,
            @"</Project>",
            builder + "\n</Project>");
    }

    private static List<string> GetMarkdownFilesForProject(string projectName)
    {
        var result = new List<string>();

        string[] mdFiles = Directory.GetFiles(
            "Assets",
            "*.md",
            SearchOption.AllDirectories);

        foreach (string mdFile in mdFiles)
        {
            string assemblyName = FindOwningAssembly(mdFile);

            // No asmdef -> Assembly-CSharp
            if (assemblyName == null)
            {
                if (projectName == "Assembly-CSharp")
                    result.Add(mdFile);

                continue;
            }

            // Has asmdef -> only add to its project
            if (assemblyName == projectName)
                result.Add(mdFile);
        }

        return result;
    }

    private static string FindOwningAssembly(string file)
    {
        string directory = Path.GetDirectoryName(
            Path.GetFullPath(file));

        while (!string.IsNullOrEmpty(directory))
        {
            string[] asmdefs = Directory.GetFiles(
                directory,
                "*.asmdef",
                SearchOption.TopDirectoryOnly);

            if (asmdefs.Length > 0)
            {
                // The nearest asmdef owns this file.
                //
                // Normally there will only be one asmdef in a directory.
                // If there are multiple, use the first one with a valid name.
                foreach (string asmdef in asmdefs)
                {
                    string assemblyName = ReadAssemblyName(asmdef);

                    if (!string.IsNullOrEmpty(assemblyName))
                        return assemblyName;
                }
            }

            string parent = Path.GetDirectoryName(directory);

            if (string.Equals(
                    parent,
                    directory,
                    StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            directory = parent;
        }

        return null;
    }

    private static string ReadAssemblyName(string asmdef)
    {
        string json = File.ReadAllText(asmdef);

        Match match = Regex.Match(
            json,
            "\"name\"\\s*:\\s*\"([^\"]+)\"");

        if (!match.Success)
            return null;

        return match.Groups[1].Value;
    }
}