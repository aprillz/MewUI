using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;

namespace Aprillz.MewUI.VisualStudio.Session
{
    /// <summary>
    /// Resolves preview-capable executable projects on disk (plan.md 4.5.1/4.7): the project
    /// nearest to the active document first, otherwise executable projects under the solution.
    /// Members that read the file system block on IO; call them off the UI thread.
    /// </summary>
    internal static class ProjectLocator
    {
        private static readonly Regex _outputTypePattern =
            new Regex(@"<OutputType>\s*(WinExe|Exe)\s*</OutputType>", RegexOptions.IgnoreCase);

        // Build outputs and tool state never hold the user's projects but can be huge.
        private static readonly HashSet<string> _skippedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "node_modules", ".git", ".vs",
        };

        internal static string FindNearestProject(string filePath, string rootDirectory)
        {
            string directory = SafeGetDirectoryName(filePath);
            while (directory != null)
            {
                string project = SafeEnumerateFiles(directory, "*.csproj").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (project != null)
                {
                    return project;
                }
                if (rootDirectory != null && PathsEqual(directory, rootDirectory))
                {
                    return null;
                }
                directory = SafeGetDirectoryName(directory);
            }
            return null;
        }

        internal static List<string> FindExecutableProjects(string rootDirectory)
        {
            var results = new List<string>();
            if (rootDirectory == null || !Directory.Exists(rootDirectory))
            {
                return results;
            }
            var pending = new Stack<string>();
            pending.Push(rootDirectory);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string project in SafeEnumerateFiles(directory, "*.csproj"))
                {
                    if (IsExecutableProject(project))
                    {
                        results.Add(project);
                    }
                }
                foreach (DirectoryInfo child in SafeEnumerateDirectories(directory))
                {
                    // Junctions are skipped: one pointing at an ancestor would make the walk endless.
                    if (!_skippedDirectories.Contains(child.Name) && (child.Attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(child.FullName);
                    }
                }
            }
            results.Sort(StringComparer.OrdinalIgnoreCase);
            return results;
        }

        /// <summary>
        /// Builds the project picker contents. AutoStart only ever gets a project of the loaded
        /// solution: the active document's project, else the solution's only executable project.
        /// The folder walk fills the picker only while the solution reports no projects at all.
        /// </summary>
        internal static ProjectCandidates ResolveCandidates(
            IReadOnlyList<string> solutionProjects,
            string solutionDirectory,
            string activeDocumentPath,
            string activeDocumentProject)
        {
            List<string> solutionExecutables = solutionProjects
                .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && IsExecutableProject(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string activeProject = activeDocumentProject;
            if (activeProject == null && activeDocumentPath != null)
            {
                activeProject = FindNearestProject(activeDocumentPath, solutionDirectory);
            }
            string activeExecutable = activeProject != null && IsExecutableProject(activeProject) ? activeProject : null;
            string activeInSolution = activeExecutable == null
                ? null
                : solutionExecutables.FirstOrDefault(path => PathsEqual(path, activeExecutable));

            List<string> projects = solutionProjects.Count == 0
                ? FindExecutableProjects(solutionDirectory)
                : new List<string>(solutionExecutables);
            if (activeExecutable != null && !projects.Any(path => PathsEqual(path, activeExecutable)))
            {
                projects.Insert(0, activeExecutable);
            }

            string autoStartProject;
            if (activeInSolution != null)
            {
                autoStartProject = activeInSolution;
            }
            else
            {
                autoStartProject = solutionExecutables.Count == 1 ? solutionExecutables[0] : null;
            }

            return new ProjectCandidates(projects, solutionExecutables, activeExecutable, autoStartProject);
        }

        internal static bool IsExecutableProject(string projectPath)
        {
            try
            {
                return _outputTypePattern.IsMatch(File.ReadAllText(projectPath));
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool PathsEqual(string first, string second)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(first).TrimEnd('\\', '/'),
                    Path.GetFullPath(second).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string SafeGetDirectoryName(string path)
        {
            try
            {
                return Path.GetDirectoryName(path);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static List<string> SafeEnumerateFiles(string directory, string pattern)
        {
            try
            {
                return Directory.EnumerateFiles(directory, pattern).ToList();
            }
            catch (Exception error) when (IsAccessError(error))
            {
                return new List<string>();
            }
        }

        private static List<DirectoryInfo> SafeEnumerateDirectories(string directory)
        {
            try
            {
                return new DirectoryInfo(directory).EnumerateDirectories().ToList();
            }
            catch (Exception error) when (IsAccessError(error))
            {
                return new List<DirectoryInfo>();
            }
        }

        private static bool IsAccessError(Exception error) =>
            error is IOException || error is UnauthorizedAccessException || error is SecurityException
            || error is ArgumentException || error is NotSupportedException;
    }

    internal sealed class ProjectCandidates
    {
        public ProjectCandidates(
            List<string> projects, List<string> solutionExecutables, string preferred, string autoStartProject)
        {
            Projects = projects;
            SolutionExecutables = solutionExecutables;
            Preferred = preferred;
            AutoStartProject = autoStartProject;
        }

        /// <summary>Executable projects for the picker.</summary>
        public List<string> Projects { get; }

        /// <summary>Executable projects of the loaded solution.</summary>
        public List<string> SolutionExecutables { get; }

        /// <summary>The project to preselect when the picker has no selection yet, or null.</summary>
        public string Preferred { get; }

        /// <summary>The project AutoStart may start without asking, or null when the choice is ambiguous.</summary>
        public string AutoStartProject { get; }
    }
}
