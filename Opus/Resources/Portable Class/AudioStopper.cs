﻿using Android.App;
using Android.Content;
using Android.Media;

namespace Opus.Resources.Portable_Class
{
    [IntentFilter(new[] { AudioManager.ActionAudioBecomingNoisy })]
    public class AudioStopper : BroadcastReceiver
    {
        public override void OnReceive(Context context, Intent intent)
        {
            if (intent.Action != AudioManager.ActionAudioBecomingNoisy)
                return;

            MusicPlayer.ShouldResumePlayback = false;
            Intent musicIntent = new Intent(Application.Context, typeof(MusicPlayer));
            musicIntent.SetAction("ForcePause");
            Application.Context.StartService(musicIntent);
        }
    }
}