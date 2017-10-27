﻿using Android.OS;
using System.Collections.Generic;
using Android.Widget;
using Android.Database;
using Android.Provider;
using MusicApp.Resources.values;
using Android.Content;
using Android;
using Android.Support.Design.Widget;
using Android.Views;
using Android.Content.PM;
using Android.Support.V4.App;
using Android.Support.V7.App;

using EventArgs = System.EventArgs;
using Square.Picasso;

namespace MusicApp.Resources.Portable_Class
{
    public class Browse : ListFragment
    {
        public static Browse instance;
        public static Context act;
        public static LayoutInflater inflater;
        public List<Song> musicList = new List<Song>();
        public List<Song> result;
        public Adapter adapter;
        public View emptyView;

        private View view;
        private string[] actions = new string[] { "Play", "Play Next", "Play Last", "Add To Playlist" };
        private bool isEmpty = false;


        public override void OnActivityCreated(Bundle savedInstanceState)
        {
            base.OnActivityCreated(savedInstanceState);
            act = Activity;
            inflater = LayoutInflater;
            emptyView = LayoutInflater.Inflate(Resource.Layout.NoSong, null);
            ListView.EmptyView = emptyView;

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

            if (ListView.Adapter == null)
                GetStoragePermission();
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
            act = null;
            inflater = null;
        }

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            View view = base.OnCreateView(inflater, container, savedInstanceState);
            this.view = view;
            view.SetPadding(0, 0, 0, MainActivity.paddingBot);
            return view;
        }

        public static Fragment NewInstance()
        {
            instance = new Browse { Arguments = new Bundle() };
            return instance;
        }

        void GetStoragePermission()
        {
            const string permission = Manifest.Permission.ReadExternalStorage;
            if (Android.Support.V4.Content.ContextCompat.CheckSelfPermission(Android.App.Application.Context, permission) == (int)Permission.Granted)
            {
                PopulateList();
                //if(FolderBrowse.instance != null)
                //    FolderBrowse.instance.PopulateList();
                return;
            }
            string[] permissions = new string[] { permission };
            RequestPermissions(permissions, 0);
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            switch (requestCode)
            {
                case 0:
                    if (grantResults[0] == Permission.Granted)
                        PopulateList();

                    else
                        Snackbar.Make(View , "Permission denied, can't list musics.", Snackbar.LengthShort).Show();

                    break;
            }
        }

        void PopulateList()
        {
            Android.Net.Uri musicUri = MediaStore.Audio.Media.ExternalContentUri;

            CursorLoader cursorLoader = new CursorLoader(Android.App.Application.Context, musicUri, null, null, null, null);
            ICursor musicCursor = (ICursor)cursorLoader.LoadInBackground();


            if (musicCursor != null && musicCursor.MoveToFirst())
            {
                int titleID = musicCursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Title);
                int artistID = musicCursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Artist);
                int albumID = musicCursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Album);
                int thisID = musicCursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Id);
                int pathID = musicCursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Data);
                do
                {
                    string Artist = musicCursor.GetString(artistID);
                    string Title = musicCursor.GetString(titleID);
                    string Album = musicCursor.GetString(albumID);
                    long AlbumArt = musicCursor.GetLong(musicCursor.GetColumnIndex(MediaStore.Audio.Albums.InterfaceConsts.AlbumId));
                    long id = musicCursor.GetLong(thisID);
                    string path = musicCursor.GetString(pathID);

                    if (Title == null)
                        Title = "Unknown Title";
                    if (Artist == null)
                        Artist = "Unknow Artist";
                    if (Album == null)
                        Album = "Unknow Album";

                    musicList.Add(new Song(Title, Artist, Album, AlbumArt, id, path));
                }
                while (musicCursor.MoveToNext());
                musicCursor.Close();
            }

            adapter = new Adapter(Android.App.Application.Context, Resource.Layout.SongList, musicList);
            ListAdapter = adapter;
            ListView.TextFilterEnabled = true;
            ListView.ItemClick += ListView_ItemClick;
            ListView.ItemLongClick += ListView_ItemLongClick;

            if (adapter == null || adapter.Count == 0)
            {
                isEmpty = true;
                Activity.AddContentView(emptyView, View.LayoutParameters);
            }
        }

        public void Search(string search)
        {
            result = new List<Song>();
            foreach(Song item in musicList)
            {
                if(item.GetName().ToLower().Contains(search.ToLower()) || item.GetArtist().ToLower().Contains(search.ToLower()))
                {
                    result.Add(item);
                }
            }
            ListAdapter = new Adapter(Android.App.Application.Context, Resource.Layout.SongList, result);
        }

        public void ListView_ItemClick(object sender, AdapterView.ItemClickEventArgs e)
        {
            Song item = musicList[e.Position];
            if (result != null)
                item = result[e.Position];

            Play(item);
        }

        public void ListView_ItemLongClick(object sender, AdapterView.ItemLongClickEventArgs e)
        {
            Song item = musicList[e.Position];
            if (result != null)
                item = result[e.Position];

            AlertDialog.Builder builder = new AlertDialog.Builder(Activity, Resource.Style.AppCompatAlertDialogStyle);
            builder.SetTitle("Pick an action");
            builder.SetItems(actions, (senderAlert, args) => 
            {
                switch (args.Which)
                {
                    case 0:
                        Play(item);
                        break;
                    case 1:
                        PlayNext(item);
                        break;
                    case 2:
                        PlayLast(item);
                        break;
                    case 3:
                        GetPlaylist(item);
                        break;
                    default:
                        break;
                }
            });
            builder.Show();
        } 

        public static void Play(Song item)
        {
            Context context = Android.App.Application.Context;
            Intent intent = new Intent(context, typeof(MusicPlayer));
            intent.PutExtra("file", item.GetPath());
            context.StartService(intent);
            MainActivity.instance.HideTabs();
            MainActivity.instance.HideSearch();
            MainActivity.instance.SupportFragmentManager.BeginTransaction().Replace(Resource.Id.contentView, Player.NewInstance()).Commit();
        }

        public static void PlayNext(Song item)
        {
            Context context = Android.App.Application.Context;
            Intent intent = new Intent(context, typeof(MusicPlayer));
            intent.PutExtra("file", item.GetPath());
            intent.SetAction("PlayNext");
            context.StartService(intent);
        }

        public static void PlayLast(Song item)
        {
            Context context = Android.App.Application.Context;
            Intent intent = new Intent(context, typeof(MusicPlayer));
            intent.PutExtra("file", item.GetPath());
            intent.SetAction("PlayLast");
            context.StartService(intent);
        }

        public static void GetPlaylist(Song item)
        {
            List<string> playList = new List<string>();
            List<long> playListId = new List<long>();
            playList.Add("Create a playlist");
            playListId.Add(0);

            Android.Net.Uri uri = MediaStore.Audio.Playlists.ExternalContentUri;
            CursorLoader loader = new CursorLoader(Android.App.Application.Context, uri, null, null, null, null);
            ICursor cursor = (ICursor)loader.LoadInBackground();

            if (cursor != null && cursor.MoveToFirst())
            {
                int nameID = cursor.GetColumnIndex(MediaStore.Audio.Playlists.InterfaceConsts.Name);
                int playlistID = cursor.GetColumnIndex(MediaStore.Audio.Playlists.InterfaceConsts.Id);
                do
                {
                    string name = cursor.GetString(nameID);
                    long id = cursor.GetLong(playlistID);
                    playList.Add(name);
                    playListId.Add(id);
                }
                while (cursor.MoveToNext());
                cursor.Close();
            }

            AlertDialog.Builder builder = new AlertDialog.Builder(act, Resource.Style.AppCompatAlertDialogStyle);
            builder.SetTitle("Add to a playlist");
            builder.SetItems(playList.ToArray(), (senderAlert, args) =>
            {
                AddToPlaylist(item, playList[args.Which], playListId[args.Which]);
            });
            builder.Show();
        }

        public static void AddToPlaylist(Song item, string playList, long playlistID)
        {
            if (playList == "Create a playlist")
                CreatePlalistDialog(item);

            else
            {
                ContentResolver resolver = act.ContentResolver;
                ContentValues value = new ContentValues();
                value.Put(MediaStore.Audio.Playlists.Members.AudioId, item.GetID());
                value.Put(MediaStore.Audio.Playlists.InterfaceConsts.Id, playlistID);
                value.Put(MediaStore.Audio.Playlists.Members.PlayOrder, 0);
                resolver.Insert(MediaStore.Audio.Playlists.Members.GetContentUri("external", playlistID), value);
            }
        }

        public static void CreatePlalistDialog(Song item)
        {
            AlertDialog.Builder builder = new AlertDialog.Builder(act, Resource.Style.AppCompatAlertDialogStyle);
            builder.SetTitle("Playlist name");
            View view = inflater.Inflate(Resource.Layout.CreatePlaylistDialog, null);
            builder.SetView(view);
            builder.SetNegativeButton("Cancel", (senderAlert, args) => { });
            builder.SetPositiveButton("Create", (senderAlert, args) => 
            {
                CreatePlaylist(view.FindViewById<EditText>(Resource.Id.playlistName).Text, item);
            });
            builder.Show();
        }

        public static void CreatePlaylist(string name, Song item)
        {
            ContentResolver resolver = act.ContentResolver;
            Android.Net.Uri uri = MediaStore.Audio.Playlists.ExternalContentUri;
            ContentValues value = new ContentValues();
            value.Put(MediaStore.Audio.Playlists.InterfaceConsts.Name, name);
            resolver.Insert(uri, value);

            string playList = "foo";
            long playlistID = 0;

            CursorLoader loader = new CursorLoader(Android.App.Application.Context, uri, null, null, null, null);
            ICursor cursor = (ICursor)loader.LoadInBackground();

            if (cursor != null && cursor.MoveToFirst())
            {
                int nameID = cursor.GetColumnIndex(MediaStore.Audio.Playlists.InterfaceConsts.Name);
                int getplaylistID = cursor.GetColumnIndex(MediaStore.Audio.Playlists.InterfaceConsts.Id);
                do
                {
                    string playlistName = cursor.GetString(nameID);
                    long id = cursor.GetLong(getplaylistID);

                    if (playlistName == name)
                        playlistID = id;
                }
                while (cursor.MoveToNext());
                cursor.Close();
            }

            AddToPlaylist(item, playList, playlistID);
        }
    }
}