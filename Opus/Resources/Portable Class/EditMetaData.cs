﻿using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Preferences;
using Android.Provider;
using Android.Runtime;
using Android.Support.Design.Widget;
using Android.Support.V7.App;
using Android.Util;
using Android.Views;
using Android.Widget;
using Opus.Resources.values;
using Square.Picasso;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using TagLib;
using YoutubeExplode;
using YoutubeExplode.Models;

namespace Opus.Resources.Portable_Class
{
    [Activity(Label = "EditMetaData", Theme = "@style/Theme", WindowSoftInputMode = SoftInput.AdjustResize|SoftInput.StateHidden)]
    public class EditMetaData : AppCompatActivity
    {
        public static EditMetaData instance;
        public Song song;

        private TextView title, artist, album, youtubeID;
        private ImageView albumArt;
        private Android.Net.Uri artURI;
        private bool tempFile = false;
        private bool hasPermission = false;
        private const int RequestCode = 8539;
        private const int PickerRequestCode = 9852;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            if(MainActivity.Theme == 1)
                SetTheme(Resource.Style.DarkTheme);

            SetContentView(Resource.Layout.EditMetaData);
            Window.SetStatusBarColor(Android.Graphics.Color.Argb(70, 00, 00, 00));

            instance = this;
            song = (Song) Intent.GetStringExtra("Song");

            Android.Support.V7.Widget.Toolbar toolbar = FindViewById<Android.Support.V7.Widget.Toolbar>(Resource.Id.backToolbar);
            DisplayMetrics metrics = new DisplayMetrics();
            WindowManager.DefaultDisplay.GetMetrics(metrics);
            ((View)toolbar.Parent.Parent).LayoutParameters.Height = metrics.WidthPixels;
            toolbar.Parent.RequestLayout();
            toolbar.LayoutParameters.Height = metrics.WidthPixels / 3;
            toolbar.RequestLayout();

            if (MainActivity.Theme == 1)
            {
                toolbar.PopupTheme = Resource.Style.DarkPopup;
            }

            SetSupportActionBar(toolbar);
            SupportActionBar.SetDisplayShowTitleEnabled(false);
            SupportActionBar.SetDisplayHomeAsUpEnabled(true);

            title = FindViewById<TextView>(Resource.Id.metadataTitle);
            artist = FindViewById<TextView>(Resource.Id.metadataArtist);
            album = FindViewById<TextView>(Resource.Id.metadataAlbum);
            youtubeID = FindViewById<TextView>(Resource.Id.metadataYID);
            albumArt = FindViewById<ImageView>(Resource.Id.metadataArt);

            FloatingActionButton fab = FindViewById<FloatingActionButton>(Resource.Id.metadataFAB);
            fab.Click += async (sender, e) => { await ValidateChanges(); };

            title.Text = song.Title;
            artist.Text = song.Artist;
            album.Text = song.Album;
            youtubeID.Text = song.YoutubeID;
            albumArt.Click += AlbumArt_Click;

            if (song.AlbumArt == -1 || song.IsYt)
            {
                var songAlbumArtUri = Android.Net.Uri.Parse(song.Album);
                Picasso.With(Application.Context).Load(songAlbumArtUri).Placeholder(Resource.Drawable.noAlbum).Transform(new RemoveBlackBorder(true)).Into(albumArt);
            }
            else
            {
                var songCover = Android.Net.Uri.Parse("content://media/external/audio/albumart");
                var songAlbumArtUri = ContentUris.WithAppendedId(songCover, song.AlbumArt);

                Picasso.With(Application.Context).Load(songAlbumArtUri).Placeholder(Resource.Drawable.noAlbum).Resize(400, 400).CenterCrop().Into(albumArt);
            }
        }

        private void AlbumArt_Click(object sender, System.EventArgs e)
        {
            new Android.Support.V7.App.AlertDialog.Builder(this, MainActivity.dialogTheme)
                .SetTitle(Resource.String.change_albumart)
                .SetItems(new string[] { GetString(Resource.String.pick_album_local), GetString(Resource.String.download_albumart) }, (senderAlert, args) =>  
                {
                    switch(args.Which)
                    {
                        case 0:
                            PickAnAlbumArtLocally();
                            break;
                        case 1:
                            DownloadMetaDataFromYT(true);
                            break;
                        default:
                            break;
                    }
                    
                }).Show();
        }

        void PickAnAlbumArtLocally()
        {
            Intent intent = new Intent(Intent.ActionPick, MediaStore.Images.Media.ExternalContentUri);
            StartActivityForResult(intent, PickerRequestCode);
        }

        protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            if(requestCode == PickerRequestCode)
            {
                if(resultCode == Result.Ok)
                {
                    Android.Net.Uri uri = data.Data;
                    Picasso.With(Application.Context).Load(uri).Placeholder(Resource.Drawable.noAlbum).Into(albumArt);
                    if (tempFile)
                    {
                        tempFile = false;
                        System.IO.File.Delete(artURI.Path);
                    }
                    artURI = uri;
                }
            }
        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            MenuInflater.Inflate(Resource.Menu.metaData_items, menu);
            return base.OnCreateOptionsMenu(menu);
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            if (item.ItemId == Android.Resource.Id.Home)
            {
                LeaveAndValidate();
                return true;
            }
            if (item.ItemId == Resource.Id.downloadMDfromYT)
            {
                DownloadMetaDataFromYT(false);
                return true;
            }
            if(item.ItemId == Resource.Id.undoChange)
            {
                UndoChange();
                return true;
            }
            return false;
        }

        async void LeaveAndValidate()
        {
            await ValidateChanges();
            Finish();
        }

        async Task ValidateChanges()
        {
            if (song.Title == title.Text && song.Artist == artist.Text && song.YoutubeID == youtubeID.Text && song.Album == album.Text && artURI == null)
                return;

            const string permission = Manifest.Permission.WriteExternalStorage;
            hasPermission = Android.Support.V4.Content.ContextCompat.CheckSelfPermission(this, permission) == (int)Permission.Granted;
            if (!hasPermission)
            {
                string[] permissions = new string[] { permission };
                RequestPermissions(permissions, RequestCode);

                while (!hasPermission)
                    await Task.Delay(1000);
            }

            Stream stream = new FileStream(song.Path, FileMode.Open, FileAccess.ReadWrite);
            var meta = TagLib.File.Create(new StreamFileAbstraction(song.Path, stream, stream));

            meta.Tag.Title = title.Text;
            meta.Tag.Performers = new string[] { artist.Text };
            meta.Tag.Album = album.Text;
            meta.Tag.Comment = youtubeID.Text;

            if (artURI != null)
            {
                IPicture[] pictures = new IPicture[1];

                Android.Graphics.Bitmap bitmap = null;
                if (tempFile)
                {
                    await Task.Run(() => 
                    {
                        bitmap = Picasso.With(this).Load(artURI).Transform(new RemoveBlackBorder(true)).Get();
                    });
                }
                else
                {
                    await Task.Run(() =>
                    {
                        bitmap = Picasso.With(this).Load(artURI).Get();
                    });
                }

                MemoryStream memoryStream = new MemoryStream();
                bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg, 100, memoryStream);
                byte[] data = memoryStream.ToArray();
                pictures[0] = new Picture(data);
                meta.Tag.Pictures = pictures;

                if(!tempFile)
                    artURI = null;

                ContentResolver.Delete(ContentUris.WithAppendedId(Android.Net.Uri.Parse("content://media/external/audio/albumart"), song.AlbumArt), null, null);
            }

            meta.Save();
            stream.Dispose();

            if (tempFile)
            {
                tempFile = false;
                System.IO.File.Delete(artURI.Path);
                artURI = null;
            }

            await Task.Delay(10);
            Android.Media.MediaScannerConnection.ScanFile(this, new string[] { song.Path }, null, null);

            Toast.MakeText(this, Resource.String.changes_saved, ToastLength.Short).Show();
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            if (requestCode == RequestCode)
            {
                if (grantResults.Length > 0)
                {
                    if (grantResults[0] == Permission.Granted)
                        hasPermission = true;
                    else
                        Snackbar.Make(FindViewById<View>(Resource.Id.contentView), Resource.String.no_permission, Snackbar.LengthShort).Show();
                }
            }
        }

        async void DownloadMetaDataFromYT(bool onlyArt)
        {
            if (song.YoutubeID == "")
            {
                Toast.MakeText(this, Resource.String.metdata_error_noid, ToastLength.Short).Show();
                return;
            }

            const string permission = Manifest.Permission.WriteExternalStorage;
            if (Android.Support.V4.Content.ContextCompat.CheckSelfPermission(this, permission) != (int)Permission.Granted)
            {
                string[] permissions = new string[] { permission };
                RequestPermissions(permissions, 2659);

                await Task.Delay(1000);
                while (Android.Support.V4.Content.ContextCompat.CheckSelfPermission(this, permission) != (int)Permission.Granted)
                    await Task.Delay(500);
            }

            YoutubeClient client = new YoutubeClient();
            Video video = await client.GetVideoAsync(youtubeID.Text);
            if (!onlyArt)
            {
                title.Text = video.Title;
                artist.Text = video.Author;
                album.Text = video.Title + " - " + video.Author;
            }

            string[] thumbnails = new string[] { video.Thumbnails.MaxResUrl, video.Thumbnails.StandardResUrl, video.Thumbnails.HighResUrl };

            for (int i = 0; i < 3; i++)
            {
                ISharedPreferences prefManager = PreferenceManager.GetDefaultSharedPreferences(this);
                string tempArt = Path.Combine(prefManager.GetString("downloadPath", Environment.GetExternalStoragePublicDirectory(Environment.DirectoryMusic).ToString()), "albumArt" + Path.GetExtension(thumbnails[i]));
                if (System.IO.File.Exists(tempArt))
                {
                    await Task.Run(() => { System.IO.File.Delete(tempArt); });
                }

                bool? canContinue = false;
                WebClient webClient = new WebClient();
                webClient.DownloadDataCompleted += (sender, e) =>
                {
                    System.Console.WriteLine("&Error with thumb " + i + ": "  + e.Error);
                    if (e.Error == null)
                    {
                        System.Console.WriteLine("&Error = null");
                        System.IO.File.WriteAllBytes(tempArt, e.Result);

                        Android.Net.Uri uri = Android.Net.Uri.FromFile(new Java.IO.File(tempArt));
                        Picasso.With(this).Load(uri).Placeholder(Resource.Drawable.noAlbum).Transform(new RemoveBlackBorder(true)).MemoryPolicy(MemoryPolicy.NoCache, MemoryPolicy.NoStore).Into(albumArt);
                        artURI = uri;
                        tempFile = true;
                        canContinue = null;
                    }
                    else
                        canContinue = true;
                };
                try
                {
                    await webClient.DownloadDataTaskAsync(new System.Uri(thumbnails[i]));
                }
                catch { } //catch 404 errors

                while (canContinue == false)
                    await Task.Delay(10);

                if (canContinue == null)
                    return;
            }
        }

        void UndoChange()
        {
            title.Text = song.Title;
            artist.Text = song.Artist;
            album.Text = song.Album;
            youtubeID.Text = song.YoutubeID;

            if (song.AlbumArt == -1 || song.IsYt)
            {
                var songAlbumArtUri = Android.Net.Uri.Parse(song.Album);
                Picasso.With(Application.Context).Load(songAlbumArtUri).Placeholder(Resource.Drawable.noAlbum).Resize(400, 400).CenterCrop().Into(albumArt);
            }
            else
            {
                var songCover = Android.Net.Uri.Parse("content://media/external/audio/albumart");
                var songAlbumArtUri = ContentUris.WithAppendedId(songCover, song.AlbumArt);

                Picasso.With(Application.Context).Load(songAlbumArtUri).Placeholder(Resource.Drawable.noAlbum).Resize(400, 400).CenterCrop().Into(albumArt);
            }

            albumArt = null;
            tempFile = false;
        }

        protected override void OnResume()
        {
            base.OnResume();
            instance = this;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}