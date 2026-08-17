using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Security;
using UnityEditor;

public class AddReadmeToProjectAndAsmdefs : AssetPostprocessor
{
    private static string OnGeneratedCSProject(string path, string content)
    {
        string projectName = Path.GetFileNameWithoutExtension(path);

        UnityEngine.Debug.Log($"Generating {projectName}");

        string[] mdFiles = GetMarkdownFilesForProject(projectName);

        if (mdFiles.Length == 0)
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

            // Escape XML characters in the path.
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

    private static string[] GetMarkdownFilesForProject(string projectName)
    {
        string asmdefFolder = FindAssemblyFolder(projectName);

        // This is the important part:
        //
        // If an asmdef exists for this project, only include markdown
        // files belonging to that asmdef.
        if (asmdefFolder != null)
        {
            return Directory.GetFiles(
                asmdefFolder,
                "*.md",
                SearchOption.AllDirectories);
        }

        // No matching asmdef.
        //
        // This normally means Assembly-CSharp (or another Unity-generated
        // project without an asmdef). Search Assets, but don't include
        // markdown files that belong to another asmdef.
        if (projectName == "Assembly-CSharp")
        {
            return GetMarkdownFilesOutsideAsmdefs();
        }

        return new string[0];
    }

    private static string[] GetMarkdownFilesOutsideAsmdefs()
    {
        string[] mdFiles = Directory.GetFiles(
            "Assets",
            "*.md",
            SearchOption.AllDirectories);

        string[] asmdefFiles = Directory.GetFiles(
            "Assets",
            "*.asmdef",
            SearchOption.AllDirectories);

        var result = new List<string>();

        foreach (string mdFile in mdFiles)
        {
            bool belongsToAsmdef = false;

            foreach (string asmdef in asmdefFiles)
            {
                string asmdefFolder = Path.GetDirectoryName(
                    Path.GetFullPath(asmdef));

                string fullMdPath = Path.GetFullPath(mdFile);

                if (IsPathInside(fullMdPath, asmdefFolder))
                {
                    belongsToAsmdef = true;
                    break;
                }
            }

            if (!belongsToAsmdef)
                result.Add(mdFile);
        }

        return result.ToArray();
    }

    private static bool IsPathInside(string path, string directory)
    {
        path = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar);

        directory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar);

        return path.StartsWith(
            directory + Path.DirectorySeparatorChar,
            System.StringComparison.OrdinalIgnoreCase);
    }

    private static string FindAssemblyFolder(string assemblyName)
    {
        string[] asmdefs = Directory.GetFiles(
            "Assets",
            "*.asmdef",
            SearchOption.AllDirectories);

        foreach (string asmdef in asmdefs)
        {
            string json = File.ReadAllText(asmdef);

            Match match = Regex.Match(
                json,
                "\"name\"\\s*:\\s*\"([^\"]+)\"");

            if (!match.Success)
                continue;

            if (match.Groups[1].Value != assemblyName)
                continue;

            return Path.GetDirectoryName(asmdef);
        }

        return null;
    }
}