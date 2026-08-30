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

subprojects {
    // Some plugins (bonsoir 5.x among them) still declare compileSdk 33, which
    // current AndroidX transitive dependencies refuse at build time. Raising a
    // library's compileSdk is compile-time only, so force the floor up for
    // every plugin instead of pinning old AndroidX versions.
    val forceCompileSdkFloor: (Project) -> Unit = { p ->
        p.extensions.findByType(com.android.build.api.dsl.CommonExtension::class.java)?.let { android ->
            val current = android.compileSdk
            if (current != null && current < 36) {
                android.compileSdk = 36
            }
        }
    }

    // evaluationDependsOn(":app") above means :app is already evaluated when
    // this block runs — afterEvaluate would throw for it. Already-evaluated
    // projects get the check immediately (a no-op for :app, which is on the
    // app's own compileSdk anyway).
    if (state.executed) {
        forceCompileSdkFloor(this)
    } else {
        afterEvaluate { forceCompileSdkFloor(this) }
    }
}

tasks.register<Delete>("clean") {
    delete(rootProject.layout.buildDirectory)
}
