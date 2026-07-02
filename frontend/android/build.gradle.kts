allprojects {
    repositories {
        google()
        mavenCentral()
    }
}

val newBuildDir: Directory =
    rootProject.layout.buildDirectory
        .dir("../../build")
        .get()
rootProject.layout.buildDirectory.value(newBuildDir)

subprojects {
    val newSubprojectBuildDir: Directory = newBuildDir.dir(project.name)
    project.layout.buildDirectory.value(newSubprojectBuildDir)
}
subprojects {
    project.evaluationDependsOn(":app")
}

// Force the Flutter plugin modules to compile against API 36. They otherwise inherit
// flutter.compileSdkVersion (34), but some (e.g. flutter_plugin_android_lifecycle pulled in by
// file_picker) now require compileSdk >= 36. The :app module already pins 36 in its own build file
// and is force-evaluated by evaluationDependsOn(":app") above — registering afterEvaluate on an
// already-evaluated project is illegal, so skip it. Set reflectively since AGP's DSL type isn't on
// the root build script's classpath.
subprojects {
    if (project.name != "app" && !project.state.executed) {
        afterEvaluate {
            val androidExtension = extensions.findByName("android") ?: return@afterEvaluate
            androidExtension.javaClass.methods
                .firstOrNull { it.name == "setCompileSdk" && it.parameterTypes.size == 1 }
                ?.invoke(androidExtension, 36)
            // Disable release lint on plugin modules too — flutter_stripe's lint classpath pulls an
            // unpublished Stripe transitive dependency (play-services-tapandpay) that fails to resolve.
            runCatching {
                val lint = androidExtension.javaClass.getMethod("getLint").invoke(androidExtension)
                lint.javaClass.methods.firstOrNull { it.name == "setCheckReleaseBuilds" }?.invoke(lint, false)
                lint.javaClass.methods.firstOrNull { it.name == "setAbortOnError" }?.invoke(lint, false)
            }
        }
    }
}

// Belt-and-suspenders: ensure no lint task actually runs in CI. The release lint classpath can't be
// resolved (the Stripe transitive dependency above), so disabling the tasks skips that resolution.
gradle.taskGraph.whenReady {
    allTasks.forEach { task ->
        if (task.name.startsWith("lint")) {
            task.enabled = false
        }
    }
}

tasks.register<Delete>("clean") {
    delete(rootProject.layout.buildDirectory)
}
