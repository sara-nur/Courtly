import Flutter
import UIKit

// Classic UIApplicationDelegate window lifecycle (scene lifecycle opted out in
// Info.plist). flutter_stripe's PaymentSheet (F26) presents from the app
// delegate's window, which is nil under the scene-based lifecycle — so we keep
// the classic window here and register plugins the classic way.
@main
@objc class AppDelegate: FlutterAppDelegate {
  override func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?
  ) -> Bool {
    GeneratedPluginRegistrant.register(with: self)
    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
  }
}
