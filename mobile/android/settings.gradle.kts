pluginManagement {
    repositories {
        maven { url = uri("https://maven.aliyun.com/repository/google") }
        maven { url = uri("https://maven.aliyun.com/repository/public") }
        maven { url = uri("https://maven.aliyun.com/repository/gradle-plugin") }
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        maven { url = uri("https://maven.aliyun.com/repository/google") }
        maven { url = uri("https://maven.aliyun.com/repository/public") }
        google()
        mavenCentral()
    }
}

rootProject.name = "YanziMobile"
include(":app")
include(":calendar")
include(":data-sdk")
// Companion sources stay in the user extension directory, like desktop extensions.
val albumRoot = file(System.getenv("LOCALAPPDATA") + "/OpenQuickHost/Extensions/yanzi-album/android")
if (albumRoot.resolve("build.gradle.kts").exists()) { include(":album"); project(":album").projectDir = albumRoot }
val notesRoot = file(System.getenv("LOCALAPPDATA") + "/OpenQuickHost/Extensions/yanzi-notes/android")
if (notesRoot.resolve("build.gradle.kts").exists()) { include(":notes"); project(":notes").projectDir = notesRoot }
