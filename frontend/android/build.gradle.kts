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

// Force every Android module (app + Flutter plugins) to compile against API 36. Plugin modules
// otherwise inherit flutter.compileSdkVersion (34), but some (e.g. flutter_plugin_android_lifecycle
// pulled in by file_picker) now require compileSdk >= 36. Set it reflectively via the "android"
// extension's setter, since AGP's DSL type isn't on the root build script's classpath.
subprojects {
    afterEvaluate {
        val androidExtension = extensions.findByName("android") ?: return@afterEvaluate
        androidExtension.javaClass.methods
            .firstOrNull { it.name == "setCompileSdk" && it.parameterTypes.size == 1 }
            ?.invoke(androidExtension, 36)
    }
}

tasks.register<Delete>("clean") {
    delete(rootProject.layout.buildDirectory)
}
