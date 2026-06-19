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
        versionCode = 8
        versionName = "0.2.8"
    }
}

dependencies {
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("com.google.android.material:material:1.12.0")
    implementation("androidx.swiperefreshlayout:swiperefreshlayout:1.1.0")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("com.alphacephei:vosk-android:0.3.75")
}
