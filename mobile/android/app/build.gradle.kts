plugins {
    id("com.android.application")
}
providers.gradleProperty("YANZI_ANDROID_BUILD_ROOT").orNull?.let {
    layout.buildDirectory.set(file(it))
}
val pushConfigPath = providers.gradleProperty("YANZI_FCM_CONFIG").orNull ?: System.getenv("YANZI_FCM_CONFIG")
val pushConfigFile = pushConfigPath?.let { file(it) }
val fcmEnabled = pushConfigFile?.isFile == true

android {
    testBuildType = "dev"
    namespace = "cc.luoluoluo.yanzi.mobile"
    compileSdk = 35

    buildFeatures {
        buildConfig = true
    }

    defaultConfig {
        testInstrumentationRunner = "cc.luoluoluo.yanzi.mobile.LanRecoveryTest"
        applicationId = "cc.luoluoluo.yanzi.mobile"
        minSdk = 26
        targetSdk = 35
        versionCode = 46
        versionName = "0.2.46"
        providers.gradleProperty("YANZI_ANDROID_VERSION_CODE").orNull?.let {
            val candidate = it.toIntOrNull()
            require(candidate != null && candidate > 0) { "Invalid candidate Android version code" }
            versionCode = candidate
        }
        providers.gradleProperty("YANZI_ANDROID_VERSION_NAME").orNull?.let {
            require(it.matches(Regex("[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?"))) { "Invalid candidate Android version name" }
            versionName = it
        }
        manifestPlaceholders["fcmEnabled"] = fcmEnabled.toString()
        if (fcmEnabled) {
            val config = pushConfigFile!!.readText().replace("\\", "\\\\").replace("\"", "\\\"").replace("\r", "").replace("\n", "")
            buildConfigField("String", "FCM_CONFIG", "\"$config\"")
        }
    }

    signingConfigs {
        create("devStable") {
            val configuredDevStoreFile =
                providers.gradleProperty("YANZI_ANDROID_DEV_KEYSTORE").orNull
                    ?: System.getenv("YANZI_ANDROID_DEV_KEYSTORE")
                    ?: "C:/Users/Administrator/.android/debug.keystore"

            val devStore = file(configuredDevStoreFile)
            if (devStore.exists()) {
                storeFile = devStore
                storePassword = "android"
                keyAlias = "androiddebugkey"
                keyPassword = "android"
            } else {
                initWith(getByName("debug"))
            }
        }

        create("release") {
            val configuredStoreFile = providers.gradleProperty("YANZI_ANDROID_KEYSTORE").orNull
                ?: System.getenv("YANZI_ANDROID_KEYSTORE")
            val configuredStorePassword = providers.gradleProperty("YANZI_ANDROID_KEYSTORE_PASSWORD").orNull
                ?: System.getenv("YANZI_ANDROID_KEYSTORE_PASSWORD")
            val configuredKeyAlias = providers.gradleProperty("YANZI_ANDROID_KEY_ALIAS").orNull
                ?: System.getenv("YANZI_ANDROID_KEY_ALIAS")
            val configuredKeyPassword = providers.gradleProperty("YANZI_ANDROID_KEY_PASSWORD").orNull
                ?: System.getenv("YANZI_ANDROID_KEY_PASSWORD")

            if (!configuredStoreFile.isNullOrBlank() &&
                !configuredStorePassword.isNullOrBlank() &&
                !configuredKeyAlias.isNullOrBlank()) {
                storeFile = file(configuredStoreFile)
                storePassword = configuredStorePassword
                keyAlias = configuredKeyAlias
                keyPassword = configuredKeyPassword ?: configuredStorePassword
            } else {
                initWith(getByName("debug"))
            }
        }
    }

    buildTypes {
        getByName("release") {
            signingConfig = signingConfigs.getByName("release")
        }

        create("dev") {
            initWith(getByName("debug"))
            applicationIdSuffix = ".dev"
            versionNameSuffix = "-dev"
            isDebuggable = true
            signingConfig = signingConfigs.getByName("devStable")
            matchingFallbacks += listOf("debug")
        }
    }
    if (fcmEnabled) sourceSets.getByName("main").java.srcDir("src/push/java")
}

dependencies {
    implementation(project(":data-sdk"))
    if (fcmEnabled) implementation("com.google.firebase:firebase-messaging:24.1.0")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("com.google.android.material:material:1.12.0")
    implementation("androidx.swiperefreshlayout:swiperefreshlayout:1.1.0")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("com.alphacephei:vosk-android:0.3.75")
}

// Bundle mobile definitions from extension-owned files; never duplicate script sources.
val mobileExtensionRoot = rootProject.file("../../extensions")
val bundledExtensions = layout.buildDirectory.dir("generated/bundled-extensions")
val bundleMobileExtensions by tasks.registering {
    inputs.files(fileTree(mobileExtensionRoot) { include("*/mobile.json", "*/mobile.js") })
    outputs.dir(bundledExtensions)
    doLast {
        val assets = bundledExtensions.get().asFile.resolve("mobile-extensions")
        assets.mkdirs()
        mobileExtensionRoot.listFiles()?.filter { it.isDirectory }?.forEach { folder ->
            val definition = folder.resolve("mobile.json")
            if (definition.isFile) {
                @Suppress("UNCHECKED_CAST")
                val metadata = groovy.json.JsonSlurper().parse(definition) as MutableMap<String, Any>
                val source = folder.resolve(metadata["entry"] as String).canonicalFile
                require(source.toPath().startsWith(folder.canonicalFile.toPath())) { "Mobile entry outside extension" }
                metadata["script"] = mapOf("source" to source.readText())
                assets.resolve(folder.name + ".json").writeText(groovy.json.JsonOutput.toJson(metadata))
            }
        }
    }
}
android.sourceSets.getByName("main").assets.srcDir(bundledExtensions)
tasks.named("preBuild") { dependsOn(bundleMobileExtensions) }
