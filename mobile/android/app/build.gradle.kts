plugins {
    id("com.android.application")
}
val pushConfigPath = providers.gradleProperty("YANZI_FCM_CONFIG").orNull ?: System.getenv("YANZI_FCM_CONFIG")
val pushConfigFile = pushConfigPath?.let { file(it) }
val fcmEnabled = pushConfigFile?.isFile == true

android {
    namespace = "cc.luoluoluo.yanzi.mobile"
    compileSdk = 35

    buildFeatures {
        buildConfig = true
    }

    defaultConfig {
        applicationId = "cc.luoluoluo.yanzi.mobile"
        minSdk = 26
        targetSdk = 35
        versionCode = 24
        versionName = "0.2.24"
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
    if (fcmEnabled) implementation("com.google.firebase:firebase-messaging:24.1.0")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("com.google.android.material:material:1.12.0")
    implementation("androidx.swiperefreshlayout:swiperefreshlayout:1.1.0")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("com.alphacephei:vosk-android:0.3.75")
}
