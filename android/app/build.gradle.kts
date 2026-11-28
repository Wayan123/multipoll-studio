plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}
android {
    namespace = "id.multipoll.field"
    compileSdk = 36
    defaultConfig {
        applicationId = "id.multipoll.field"
        minSdk = 26
        targetSdk = 36
        versionCode = 1
        versionName = "0.1.0-pilot"
    }
    buildFeatures { compose = true }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}
kotlin { compilerOptions { jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17) } }
dependencies {
    implementation(platform("androidx.compose:compose-bom:2025.10.01"))
    implementation("androidx.compose.material3:material3")
    implementation("androidx.activity:activity-compose:1.11.0")
    implementation("com.github.mik3y:usb-serial-for-android:3.12.0")
    testImplementation("junit:junit:4.13.2")
}
