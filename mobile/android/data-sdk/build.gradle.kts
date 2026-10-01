plugins { id("com.android.library") }
android {
    namespace = "cc.luoluoluo.yanzi.sdk"
    compileSdk = 35
    defaultConfig { minSdk = 26 }
    buildTypes { create("dev") { initWith(getByName("debug")) } }
}
