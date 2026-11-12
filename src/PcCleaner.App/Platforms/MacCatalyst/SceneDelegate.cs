using Foundation;

namespace PcCleaner.App;

/// <summary>
/// The window-scene delegate named in Info.plist's UIApplicationSceneManifest. UIKit refuses to launch an app
/// built with the current Mac Catalyst SDK unless it adopts the UIScene life cycle; MAUI's base class does
/// all the work, so this only exists to be registered under the name the manifest uses.
/// </summary>
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
}
