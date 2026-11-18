using AVFoundation;
using Foundation;
using UIKit;

namespace SonaFly
{
    [Register("AppDelegate")]
    public class AppDelegate : MauiUIApplicationDelegate
    {
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
        {
            ConfigureAudioSession();
            return base.FinishedLaunching(application, launchOptions);
        }

        /// <summary>
        /// Declares the app a music player. With the default category iOS silences the
        /// app when the screen locks, the app goes to the background, or the ring switch
        /// is set to silent. Together with the "audio" background mode in Info.plist,
        /// the playback category keeps the music going in all three cases.
        /// </summary>
        /// <remarks>
        /// The session is not activated here: that would cut off whatever another app is
        /// playing as soon as this one opens. Starting playback activates it.
        /// </remarks>
        private static void ConfigureAudioSession()
        {
            var error = AVAudioSession.SharedInstance().SetCategory(AVAudioSessionCategory.Playback);
            if (error != null)
            {
                System.Diagnostics.Debug.WriteLine($"Unable to set the audio session category: {error.LocalizedDescription}");
            }
        }
    }
}
