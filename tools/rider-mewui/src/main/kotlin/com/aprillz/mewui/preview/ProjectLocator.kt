// Resolves preview-capable executable projects on disk (plan.md 4.5.1/4.7): the project
// nearest to the active document first, otherwise executable projects under the solution.
// Also resolves the dotnet host the way the IDE resolves external tools. Every function here
// touches the disk (and the shell environment on macOS), so callers stay off the EDT.
package com.aprillz.mewui.preview

import com.intellij.execution.configurations.PathEnvironmentVariableUtil
import com.intellij.openapi.util.SystemInfo
import com.intellij.util.EnvironmentUtil
import java.io.File

object ProjectLocator {
    private val outputTypePattern = Regex("<OutputType>\\s*(WinExe|Exe)\\s*</OutputType>", RegexOption.IGNORE_CASE)
    private val skippedDirectories = setOf("bin", "obj", "node_modules", ".git", ".vs", ".idea")

    fun findNearestProject(filePath: String, rootDirectory: String?): String? {
        var directory: File? = File(filePath).parentFile
        val root = rootDirectory?.let { File(it).absoluteFile }
        while (directory != null) {
            val project = directory.listFiles { file -> file.extension.equals("csproj", ignoreCase = true) }
                ?.minByOrNull { it.name }
            if (project != null) {
                return project.absolutePath
            }
            if (root != null && directory.absoluteFile == root) {
                return null
            }
            directory = directory.parentFile
        }
        return null
    }

    fun findExecutableProjects(rootDirectory: String?): List<String> {
        val root = rootDirectory?.let(::File) ?: return emptyList()
        if (!root.isDirectory) {
            return emptyList()
        }
        return root.walkTopDown()
            .onEnter { directory -> directory == root || directory.name !in skippedDirectories }
            .filter { it.isFile && it.extension.equals("csproj", ignoreCase = true) }
            .filter { isExecutableProject(it.absolutePath) }
            .map { it.absolutePath }
            .sorted()
            .toList()
    }

    fun isExecutableProject(projectPath: String): Boolean =
        runCatching { outputTypePattern.containsMatchIn(File(projectPath).readText()) }.getOrDefault(false)
}

object DotnetLocator {
    /**
     * Finds dotnet on the IDE's shell PATH (a Dock-launched IDE on macOS gets the login shell's
     * environment from the platform, not the JVM's), then DOTNET_ROOT, then the default install
     * locations. Blocks while the platform loads the shell environment.
     */
    fun resolve(): DotnetHost {
        val environment = HashMap(EnvironmentUtil.getEnvironmentMap())
        val executableName = if (SystemInfo.isWindows) "dotnet.exe" else "dotnet"
        val executable = PathEnvironmentVariableUtil.findExecutableInPathOnAnyOS("dotnet")
            ?: environment["DOTNET_ROOT"]?.let { root -> File(root, executableName) }?.takeIf(File::canExecute)
            ?: installLocations(executableName).firstOrNull(File::canExecute)
            ?: return DotnetHost("dotnet", environment)

        // dotnet watch and MSBuild start further dotnet processes by name; keep the resolved host on PATH.
        val pathKey = environment.keys.firstOrNull { key -> key.equals("PATH", ignoreCase = true) } ?: "PATH"
        val directory = executable.absoluteFile.parentFile
        val path = environment[pathKey].orEmpty()
        if (path.split(File.pathSeparatorChar).none { entry -> entry.isNotEmpty() && File(entry).absoluteFile == directory }) {
            environment[pathKey] = if (path.isEmpty()) directory.path else directory.path + File.pathSeparator + path
        }
        return DotnetHost(executable.absolutePath, environment)
    }

    private fun installLocations(executableName: String): List<File> {
        val home = System.getProperty("user.home")
        return when {
            SystemInfo.isWindows -> listOfNotNull(
                System.getenv("ProgramFiles")?.let { directory -> File(directory, "dotnet\\$executableName") },
                System.getenv("ProgramW6432")?.let { directory -> File(directory, "dotnet\\$executableName") },
                System.getenv("LOCALAPPDATA")?.let { directory -> File(directory, "Microsoft\\dotnet\\$executableName") },
                File(home, ".dotnet\\$executableName"),
            )
            SystemInfo.isMac -> listOf(
                File("/usr/local/share/dotnet/dotnet"),
                File("/opt/homebrew/bin/dotnet"),
                File("/usr/local/bin/dotnet"),
                File(home, ".dotnet/dotnet"),
            )
            else -> listOf(
                File("/usr/share/dotnet/dotnet"),
                File("/usr/lib/dotnet/dotnet"),
                File("/usr/lib64/dotnet/dotnet"),
                File("/usr/bin/dotnet"),
                File("/snap/bin/dotnet"),
                File(home, ".dotnet/dotnet"),
            )
        }
    }
}
