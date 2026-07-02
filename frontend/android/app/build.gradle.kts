plugins {
    id("com.android.application")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

android {
    namespace = "com.courtly"
    // Pinned to 36: transitive plugins (e.g. flutter_plugin_android_lifecycle via file_picker)
    // now require compiling against Android API 36 or later.
    compileSdk = 36
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    defaultConfig {
        // TODO: Specify your own unique Application ID (https://developer.android.com/studio/build/application-id.html).
        applicationId = "com.courtly"
        // You can update the following values to match your application needs.
        // For more information, see: https://flutter.dev/to/review-gradle-config.
        minSdk = flutter.minSdkVersion
        targetSdk = flutter.targetSdkVersion
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    buildTypes {
        release {
            // Signing with the debug keys so the release build/run works without a keystore.
            signingConfig = signingConfigs.getByName("debug")
            // flutter_stripe references optional Stripe push-provisioning classes that are not
            // bundled, so R8 full-mode shrinking fails the release build ("Missing classes").
            // Code shrinking isn't required here, so disable it for a reliable release APK.
            isMinifyEnabled = false
            isShrinkResources = false
        }
    }
}

kotlin {
    compilerOptions {
        jvmTarget = org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17
    }
}

dependencies {
    // Provides the Theme.MaterialComponents.* parents used by NormalTheme, which
    // flutter_stripe's PaymentSheet (F26) requires as its host activity theme.
    implementation("com.google.android.material:material:1.12.0")
}

flutter {
    source = "../.."
}
