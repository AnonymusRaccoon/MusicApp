﻿using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Android.Support.V7.App;
using Android.Support.V4.App;
using System.Collections.Generic;
using Android.Provider;
using Android.Database;
using Android.Content.PM;
using Android.Support.Design.Widget;
using Android;
using Android.Net;
using MusicApp.Resources.values;
using Square.Picasso;

using EventArgs = System.EventArgs;

namespace MusicApp.Resources.Portable_Class
{
    public class Playlist : ListFragment
    {
        public static Playlist instance;
        public Adapter adapter;
        public View emptyView;

        private List<string> playList = new List<string>();
        private List<int> playListCount = new List<int>();
        private List<long> playlistId = new List<long>();
        private string[] actions = new string[] { "Random play", "Rename", "Delete" };
        private bool isEmpty = false;


        public override void OnActivityCreated(Bundle savedInstanceState)
        {
            base.OnActivityCreated(savedInstanceState);
            emptyView = LayoutInflater.Inflate(Resource.Layout.NoPlaylist, null);
            ListView.EmptyView = emptyView;

            if(ListView.Adapter == null)
                GetStoragePermission();

            if (MusicPlayer.isRunning)
            {
                Song current = MusicPlayer.queue[MusicPlayer.CurrentID()];

                RelativeLayout smallPlayer = Activity.FindViewById<RelativeLayout>(Resource.Id.smallPlayer);
                FrameLayout parent = (FrameLayout)smallPlayer.Parent;
                parent.Visibility = ViewStates.Visible;
                smallPlayer.Visibility = ViewStates.Visible;
                smallPlayer.FindViewById<TextView>(Resource.Id.spTitle).Text = current.GetName();
                smallPlayer.FindViewById<TextView>(Resource.Id.spArtist).Text = current.GetArtist();
                ImageView art = smallPlayer.FindViewById<ImageView>(Resource.Id.spArt);

                var songCover = Android.Net.Uri.Parse("content://media/external/audio/albumart");
                var songAlbumArtUri = ContentUris.WithAppendedId(songCover, current.GetAlbumArt());

                Picasso.With(Android.App.Application.Context).Load(songAlbumArtUri).Placeholder(Resource.Drawable.MusicIcon).Into(art);

                smallPlayer.FindViewById<ImageButton>(Resource.Id.spLast).Click += Last_Click;
                smallPlayer.FindViewById<ImageButton>(Resource.Id.spPlay).Click += Play_Click;
                smallPlayer.FindViewById<ImageButton>(Resource.Id.spNext).Click += Next_Click;
            }
        }

        private void Last_Click(object sender, EventArgs e)
        {
            Intent intent = new Intent(Android.App.Application.Context, typeof(MusicPlayer));
            intent.SetAction("Previus");
            Activity.StartService(intent);
        }

        private void Play_Click(object sender, EventArgs e)
        {
            Intent intent = new Intent(Android.App.Application.Context, typeof(MusicPlayer));
            intent.SetAction("Pause");
            Activity.StartService(intent);
        }

        private void Next_Click(object sender, EventArgs e)
        {
            Intent intent = new Intent(Android.App.Application.Context, typeof(MusicPlayer));
            intent.SetAction("Next");
            Activity.StartService(intent);
        }

        public override void OnDestroy()
        {
            RelativeLayout smallPlayer = Activity.FindViewById<RelativeLayout>(Resource.Id.smallPlayer);
            FrameLayout parent = (FrameLayout)smallPlayer.Parent;
            parent.Visibility = ViewStates.Gone;
            smallPlayer.FindViewById<ImageButton>(Resource.Id.spLast).Click -= Last_Click;
            smallPlayer.FindViewById<ImageButton>(Resource.Id.spPlay).Click -= Play_Click;
            smallPlayer.FindViewById<ImageButton>(Resource.Id.spNext).Click -= Next_Click;

            if (isEmpty)
            {
                ViewGroup rootView = Activity.FindViewById<ViewGroup>(Android.Resource.Id.Content);
                rootView.RemoveView(emptyView);
            }
            base.OnDestroy();
            instance = null;
        }

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            View view = base.OnCreateView(inflater, container, savedInstanceState);
            view.SetPadding(0, 100, 0, MainActivity.paddingBot);
            return view;
        }

        public static Fragment NewInstance()
        {
            instance = new Playlist { Arguments = new Bundle() };
            return instance;
        }

        void GetStoragePermission()
        {
            const string permission = Manifest.Permission.ReadExternalStorage;
            if (Android.Support.V4.Content.ContextCompat.CheckSelfPermission(Android.App.Application.Context, permission) == (int)Permission.Granted)
            {
                PopulateView();
                return;
            }
            string[] permissions = new string[] { Manifest.Permission.ReadExternalStorage };
            RequestPermissions(permissions, 0);
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            switch (requestCode)
            {
                case 0:
                    {
                        if (grantResults[0] == Permission.Granted)
                        {
                            PopulateView();
                        }
                        else
                        {
                            var snack = Snackbar.Make(View, "Permission denied, can't list musics.", Snackbar.LengthShort);
                            snack.Show();
                        }
                    }
                    break;
            }
        }

        void PopulateView()
        {
            Uri uri = MediaStore.Audio.Playlists.ExternalContentUri;
            CursorLoader loader = new CursorLoader(Android.App.Application.Context, uri, null, null, null, null);
            ICursor cursor = (ICursor)loader.LoadInBackground();

            if (cursor != null && cursor.MoveToFirst())
            {
                int nameID = cursor.GetColumnIndex(MediaStore.Audio.Playlists.InterfaceConsts.Name);
                //int countID = cursor.GetColumnIndex(MediaStore.Audio.Playlists.InterfaceConsts.Count);
                int listID = cursor.GetColumnIndex(MediaStore.Audio.Playlists.InterfaceConsts.Id);
                do
                {
                    string name = cursor.GetString(nameID);
                    //int count = cursor.GetInt(countID);
                    long id = cursor.GetLong(listID);
                    playList.Add(name);
                    //playListCount.Add(count);
                    playlistId.Add(id);

                }
                while (cursor.MoveToNext());
                cursor.Close();
            }
            //ListAdapter = new TwoLineAdapter(Android.App.Application.Context, Resource.Layout.TwoLineLayout, playList, playListCount);
            ListAdapter = new ArrayAdapter(Android.App.Application.Context, Resource.Layout.PlaylistList, playList);
            ListView.TextFilterEnabled = true;
            ListView.ItemClick += ListView_ItemClick;
            ListView.ItemLongClick += ListView_ItemLongClick;

            if (adapter == null || adapter.Count == 0)
            {
                isEmpty = true;
                Activity.AddContentView(emptyView, View.LayoutParameters);
            }
        }

        private void ListView_ItemClick(object sender, AdapterView.ItemClickEventArgs e)
        {
            AppCompatActivity act = (AppCompatActivity)Activity;
            act.SupportActionBar.SetHomeButtonEnabled(true);
            act.SupportActionBar.SetDisplayHomeAsUpEnabled(true);
            act.SupportActionBar.Title = playList[e.Position];
            FragmentTransaction transaction = FragmentManager.BeginTransaction();
            transaction.Replace(Resource.Id.contentView, PlaylistTracks.NewInstance(playlistId[e.Position]));
            transaction.AddToBackStack(null);
            transaction.Commit();
        }

        private void ListView_ItemLongClick(object sender, AdapterView.ItemLongClickEventArgs e)
        {
            AlertDialog.Builder builder = new AlertDialog.Builder(Activity, Resource.Style.AppCompatAlertDialogStyle);
            builder.SetTitle("Pick an action");
            builder.SetItems(actions, (senderAlert, args) => 
            {
                switch (args.Which)
                {
                    case 0:
                        RandomPlay(playlistId[e.Position]);
                        break;
                    case 1:
                        Rename(e.Position, playlistId[e.Position]);
                        break;
                    case 2:
                        RemovePlaylist(e.Position, playlistId[e.Position]);
                        break;
                    default:
                        break;
                }
            });
            builder.Show();
        }

        void RandomPlay(long playlistID)
        {
            List<string> tracksPath = new List<string>();
            Uri musicUri = MediaStore.Audio.Playlists.Members.GetContentUri("external", playlistID);

            CursorLoader cursorLoader = new CursorLoader(Android.App.Application.Context, musicUri, null, null, null, null);
            ICursor musicCursor = (ICursor)cursorLoader.LoadInBackground();


            if (musicCursor != null && musicCursor.MoveToFirst())
            {
                int pathID = musicCursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Data);
                do
                {
                    string path = musicCursor.GetString(pathID);

                    tracksPath.Add(path);
                }
                while (musicCursor.MoveToNext());
                musicCursor.Close();
            }

            Intent intent = new Intent(Android.App.Application.Context, typeof(MusicPlayer));
            intent.PutStringArrayListExtra("files", tracksPath);
            intent.SetAction("RandomPlay");
            Activity.StartService(intent);
        }

        void Rename(int position, long playlistID)
        {
            AlertDialog.Builder builder = new AlertDialog.Builder(Activity, Resource.Style.AppCompatAlertDialogStyle);
            builder.SetTitle("Playlist name");
            View view = LayoutInflater.Inflate(Resource.Layout.CreatePlaylistDialog, null);
            builder.SetView(view);
            builder.SetNegativeButton("Cancel", (senderAlert, args) => { });
            builder.SetPositiveButton("Rename", (senderAlert, args) =>
            {
                RenamePlaylist(position, view.FindViewById<EditText>(Resource.Id.playlistName).Text, playlistID);
            });
            builder.Show();
        }

        void RenamePlaylist(int position, string name, long playlistID)
        {
            ContentResolver resolver = Activity.ContentResolver;
            Uri uri = MediaStore.Audio.Playlists.ExternalContentUri;
            ContentValues value = new ContentValues();
            value.Put(MediaStore.Audio.Playlists.InterfaceConsts.Name, name);
            resolver.Update(MediaStore.Audio.Playlists.ExternalContentUri, value, MediaStore.Audio.Playlists.InterfaceConsts.Id + "=?", new string[] { playlistID.ToString() });
            playList[position] = name;
            ListAdapter = new ArrayAdapter(Android.App.Application.Context, Resource.Layout.PlaylistList, playList);
            if (ListAdapter.Count == 0)
            {
                isEmpty = true;
                Activity.AddContentView(emptyView, View.LayoutParameters);
            }
        }

        void RemovePlaylist(int position, long playlistID)
        {
            ContentResolver resolver = Activity.ContentResolver;
            Uri uri = MediaStore.Audio.Playlists.ExternalContentUri;
            resolver.Delete(MediaStore.Audio.Playlists.ExternalContentUri, MediaStore.Audio.Playlists.InterfaceConsts.Id + "=?", new string[] { playlistID.ToString() });
            playList.RemoveAt(position);
            playlistId.RemoveAt(position);
            ListAdapter = new ArrayAdapter(Android.App.Application.Context, Resource.Layout.PlaylistList, playList);
            if(ListAdapter.Count == 0)
            {
                isEmpty = true;
                Activity.AddContentView(emptyView, View.LayoutParameters);
            }
        }
    }
}