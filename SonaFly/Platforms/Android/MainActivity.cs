using Android.App;
using Android.Content.PM;
using Android.Media;
using Android.OS;

namespace SonaFly
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        private AudioManager? _audioManager;
        private AudioFocusRequestClass? _focusRequest;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            RequestAudioFocus();
        }

        /// <summary>
        /// Request audio focus with AUDIOFOCUS_GAIN so notifications only duck our volume
        /// instead of pausing playback entirely.
        /// </summary>
        private void RequestAudioFocus()
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;

            _audioManager = (AudioManager?)GetSystemService(AudioService);
            if (_audioManager == null) return;

            var focusChangeListener = new AudioFocusChangeListener();

            _focusRequest = new AudioFocusRequestClass.Builder(AudioFocus.Gain)
                .SetAudioAttributes(new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.Media)!
                    .SetContentType(AudioContentType.Music)!
                    .Build()!)
                .SetAcceptsDelayedFocusGain(true)
                .SetWillPauseWhenDucked(false) // KEY: don't pause, just duck volume
                .SetOnAudioFocusChangeListener(focusChangeListener)
                .Build();

            _audioManager.RequestAudioFocus(_focusRequest);
        }

        protected override void OnDestroy()
        {
            if (_audioManager != null && _focusRequest != null)
                _audioManager.AbandonAudioFocusRequest(_focusRequest);

            base.OnDestroy();
        }

        private class AudioFocusChangeListener : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
        {
            public void OnAudioFocusChange(AudioFocus focusChange)
            {
                // We intentionally do nothing here — we never want to pause.
                // The SetWillPauseWhenDucked(false) flag already tells Android
                // to just lower our volume on transient focus loss (notifications)
                // and restore it automatically when the interruption ends.
            }
        }
    }
}
