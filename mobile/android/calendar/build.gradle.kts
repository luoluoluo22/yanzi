plugins { id("com.android.application") }
android {
    namespace = "cc.luoluoluo.yanzi.calendar"
    compileSdk = 35
    useLibrary("android.test.runner")
    useLibrary("android.test.base")
    useLibrary("android.test.mock")
    buildFeatures { buildConfig = true }
    testBuildType = "dev"
    defaultConfig {
        applicationId = "cc.luoluoluo.yanzi.calendar"
        minSdk = 26
        targetSdk = 35
        versionCode = 2
        versionName = "0.1.1"
        resValue("string", "app_name", "燕子日历")
        testInstrumentationRunner = "android.test.InstrumentationTestRunner"
        manifestPlaceholders["hostPackage"] = "cc.luoluoluo.yanzi.mobile"
        buildConfigField("String", "HOST_PACKAGE", "\"cc.luoluoluo.yanzi.mobile\"")
    }
    signingConfigs {
        create("devStable") {
            storeFile = file(providers.gradleProperty("YANZI_ANDROID_DEV_KEYSTORE").orNull
                ?: System.getenv("YANZI_ANDROID_DEV_KEYSTORE") ?: "C:/Users/Administrator/.android/debug.keystore")
            storePassword = "android"; keyAlias = "androiddebugkey"; keyPassword = "android"
        }
        create("release") {
            val path = providers.gradleProperty("YANZI_ANDROID_KEYSTORE").orNull ?: System.getenv("YANZI_ANDROID_KEYSTORE")
            if (!path.isNullOrBlank()) {
                storeFile = file(path)
                storePassword = providers.gradleProperty("YANZI_ANDROID_KEYSTORE_PASSWORD").orNull ?: System.getenv("YANZI_ANDROID_KEYSTORE_PASSWORD")
                keyAlias = providers.gradleProperty("YANZI_ANDROID_KEY_ALIAS").orNull ?: System.getenv("YANZI_ANDROID_KEY_ALIAS")
                keyPassword = providers.gradleProperty("YANZI_ANDROID_KEY_PASSWORD").orNull ?: System.getenv("YANZI_ANDROID_KEY_PASSWORD") ?: storePassword
            } else initWith(getByName("debug"))
        }
    }
    buildTypes {
        getByName("release") { signingConfig = signingConfigs.getByName("release") }
        create("dev") {
            initWith(getByName("debug")); applicationIdSuffix = ".dev"; versionNameSuffix = "-dev"
            signingConfig = signingConfigs.getByName("devStable")
            resValue("string", "app_name", "燕子日历 Dev")
            manifestPlaceholders["hostPackage"] = "cc.luoluoluo.yanzi.mobile.dev"
            buildConfigField("String", "HOST_PACKAGE", "\"cc.luoluoluo.yanzi.mobile.dev\"")
        }
    }
}
dependencies { implementation(project(":data-sdk")) }
