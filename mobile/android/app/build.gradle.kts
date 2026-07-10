plugins {
    id("com.android.application")
}

android {
    namespace = "cc.luoluoluo.yanzi.mobile"
    compileSdk = 35

    defaultConfig {
        applicationId = "cc.luoluoluo.yanzi.mobile"
        minSdk = 26
        targetSdk = 35
        versionCode = 17
        versionName = "0.2.17"
    }

    signingConfigs {
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
    }
}

dependencies {
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("com.google.android.material:material:1.12.0")
    implementation("androidx.swiperefreshlayout:swiperefreshlayout:1.1.0")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("com.alphacephei:vosk-android:0.3.75")
}
