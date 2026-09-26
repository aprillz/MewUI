// MewUI Preview plugin for JetBrains Rider. Frontend-only: the session layer (dotnet watch
// spawn, loopback protocol server) runs in the JVM plugin process, mirroring the VSCode
// extension's node host; no ReSharper backend component is required.
plugins {
    // Kotlin must be able to read the target IDE's binary metadata (Rider 2026.2 ships 2.4).
    id("org.jetbrains.kotlin.jvm") version "2.4.0"
    id("org.jetbrains.intellij.platform") version "2.2.1"
}

group = "com.aprillz.mewui"
version = "0.1.0"

repositories {
    mavenCentral()
    intellijPlatform {
        defaultRepositories()
    }
}

dependencies {
    intellijPlatform {
        // -PriderHome=<install dir> compiles against a local Rider install (no SDK download).
        val riderHome = providers.gradleProperty("riderHome").orNull
        if (riderHome != null) {
            local(riderHome)
        } else {
            rider("2025.1")
        }
    }
}

kotlin {
    jvmToolchain(21)
}

intellijPlatform {
    pluginConfiguration {
        name = "MewUI Preview"
        ideaVersion {
            // Matches the compile target (Rider 2026.2); older IDEs cannot load the
            // Kotlin 2.4 metadata this build produces anyway.
            sinceBuild = "262"
            // No upper bound: the plugin is a pure JVM frontend with no ReSharper backend
            // component, so a new Rider release does not require a rebuild.
            untilBuild = provider { null }
        }
    }
    // The plugin contributes no settings UI; skip the headless-IDE indexing pass.
    buildSearchableOptions = false
}
