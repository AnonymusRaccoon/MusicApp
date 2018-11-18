﻿using Android;
using Android.Content;
using Android.Content.PM;
using Android.Database;
using Android.Net;
using Android.OS;
using Android.Provider;
using Android.Support.Design.Widget;
using Android.Support.V4.App;
using Android.Support.V7.App;
using Android.Support.V7.Widget;
using Android.Views;
using Android.Widget;
using MusicApp.Resources.values;
using SQLite;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TagLib;
using Color = Android.Graphics.Color;

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
        public bool focused = true;

        private View view;
        private readonly string[] actions = new string[] { "Play", "Play Next", "Play Last", "Add To Playlist", "Edit Metadata" };
        private bool isEmpty = false;


        public override void OnActivityCreated(Bundle savedInstanceState)
        {
            base.OnActivityCreated(savedInstanceState);
            act = Activity;
            inflater = LayoutInflater;
            emptyView = LayoutInflater.Inflate(Resource.Layout.NoSong, null);
            ListView.EmptyView = emptyView;
            MainActivity.instance.contentRefresh.Refresh += OnRefresh;
            ListView.Scroll += MainActivity.instance.Scroll;
            ListView.NestedScrollingEnabled = true;

            if (ListView.Adapter == null)
                MainActivity.instance.GetStoragePermission();
        }

        public override void OnDestroy()
        {
            MainActivity.instance.contentRefresh.Refresh -= OnRefresh;
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
            return view;
        }

        public static Fragment NewInstance()
        {
            instance = new Browse { Arguments = new Bundle() };
            return instance;
        }

        public void PopulateList()
        {
            musicList = new List<Song>();

            Uri musicUri = MediaStore.Audio.Media.ExternalContentUri;

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

                    musicList.Add(new Song(Title, Artist, Album, null, AlbumArt, id, path));
                }
                while (musicCursor.MoveToNext());
                musicCursor.Close();
            }

            List<Song> songList = musicList.OrderBy(x => x.Title).ToList();
            musicList = songList;
            int listPadding = 0;
            if (adapter != null)
                listPadding = adapter.listPadding;
            adapter = new Adapter(Android.App.Application.Context, Resource.Layout.SongList, musicList)
            {
                listPadding = listPadding
            };
            ListAdapter = adapter;
            ListView.TextFilterEnabled = true;
            ListView.ItemClick += ListView_ItemClick;
            ListView.ItemLongClick += ListView_ItemLongClick;

            if (adapter == null || adapter.Count == 0)
            {
                isEmpty = true;
                Activity.AddContentView(emptyView, View.LayoutParameters);
            }

            //if (MainActivity.paddingBot > MainActivity.defaultPaddingBot && adapter.listPadding == 0)
            //    adapter.listPadding = MainActivity.paddingBot - MainActivity.defaultPaddingBot;

            if(result != null)
            {
                if (adapter != null)
                    listPadding = adapter.listPadding;
                adapter = new Adapter(Android.App.Application.Context, Resource.Layout.SongList, result)
                {
                    listPadding = listPadding
                };
                ListAdapter = adapter;
            }
        }

        private void OnRefresh(object sender, System.EventArgs e)
        {
            if (!focused)
                return;
            Refresh();
            MainActivity.instance.contentRefresh.Refreshing = false;
        }

        public void Refresh()
        {
            PopulateList();
        }

        public void Search(string search)
        {
            result = new List<Song>();
            foreach(Song item in musicList)
            {
                if(item.Title.ToLower().Contains(search.ToLower()) || item.Artist.ToLower().Contains(search.ToLower()))
                {
                    result.Add(item);
                }
            }
            int listPadding = 0;
            if (adapter != null)
                listPadding = adapter.listPadding;
            adapter = new Adapter(Android.App.Application.Context, Resource.Layout.SongList, result)
            {
                listPadding = listPadding
            };
            ListAdapter = adapter;
        }

        public void ListView_ItemClick(object sender, AdapterView.ItemClickEventArgs e)
        {
            Song item = musicList[e.Position];
            if (result != null)
                item = result[e.Position];

            item = CompleteItem(item);

            Play(item, ListView.GetChildAt(e.Position - ListView.FirstVisiblePosition).FindViewById<ImageView>(Resource.Id.albumArt));
        }

        private void ListView_ItemLongClick(object sender, AdapterView.ItemLongClickEventArgs e)
        {
            Song item = musicList[e.Position];
            if (result != null)
                item = result[e.Position];

            More(item, e.Position);
        } 

        public void More(Song item, int position)
        {
            item = CompleteItem(item);

            AlertDialog.Builder builder = new AlertDialog.Builder(Activity, MainActivity.dialogTheme);
            builder.SetTitle("Pick an action");
            builder.SetItems(actions, (senderAlert, args) =>
            {
                switch (args.Which)
                {
                    case 0:
                        Play(item, ListView.GetChildAt(position - ListView.FirstVisiblePosition).FindViewById<ImageView>(Resource.Id.albumArt));
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
                    case 4:
                        EditMetadata(item, "Browse", ListView.OnSaveInstanceState());
                        break;
                    default:
                        break;
                }
            });
            builder.Show();
        }

        public static Song GetSong(string filePath)
        {
            string Title = "Unknow";
            string Artist = "Unknow";
            long AlbumArt = 0;
            long id = 0;
            string path;
            Uri musicUri = MediaStore.Audio.Media.ExternalContentUri;

            if (filePath.StartsWith("content://"))
                musicUri = Uri.Parse(filePath);

            CursorLoader cursorLoader = new CursorLoader(Android.App.Application.Context, musicUri, null, null, null, null);
            ICursor musicCursor = (ICursor)cursorLoader.LoadInBackground();
            if (musicCursor != null && musicCursor.MoveToFirst())
            {
                int titleID = musicCursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Title);
                int artistID = musicCursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Artist);
                int thisID = musicCursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Id);
                int pathID = musicCursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Data);
                do
                {
                    path = musicCursor.GetString(pathID);

                    if (path == filePath || filePath.StartsWith("content://"))
                    {
                        Artist = musicCursor.GetString(artistID);
                        Title = musicCursor.GetString(titleID);
                        AlbumArt = musicCursor.GetLong(musicCursor.GetColumnIndex(MediaStore.Audio.Albums.InterfaceConsts.AlbumId));
                        id = musicCursor.GetLong(thisID);

                        if (Title == null)
                            Title = "Unknown Title";
                        if (Artist == null)
                            Artist = "Unknow Artist";
                        break;
                    }
                }
                while (musicCursor.MoveToNext());
                musicCursor.Close();
            }
            return new Song(Title, Artist, null, null, AlbumArt, id, filePath);
        }

        public static Song CompleteItem(Song item)
        {
            item.youtubeID = GetYtID(item.Path);
            return item;
        }

        public static string GetYtID(string path)
        {
            Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            var meta = TagLib.File.Create(new StreamFileAbstraction(path, stream, stream));
            string ytID = meta.Tag.Comment;
            stream.Dispose();
            return ytID;
        }

        public static void Play(Song item, View albumArt)
        {
            MusicPlayer.queue?.Clear();
            MusicPlayer.currentID = -1;

            Context context = Android.App.Application.Context;
            Intent intent = new Intent(context, typeof(MusicPlayer));
            intent.PutExtra("file", item.Path);
            context.StartService(intent);

            MainActivity.instance.ShowSmallPlayer();
            MainActivity.instance.ShowPlayer();
            MusicPlayer.UpdateQueueDataBase();
        }

        public static void PlayNext(Song item)
        {
            Context context = Android.App.Application.Context;
            Intent intent = new Intent(context, typeof(MusicPlayer));
            intent.PutExtra("file", item.Path);
            intent.SetAction("PlayNext");
            context.StartService(intent);
        }

        public static void PlayLast(Song item)
        {
            Context context = Android.App.Application.Context;
            Intent intent = new Intent(context, typeof(MusicPlayer));
            intent.PutExtra("file", item.Path);
            intent.SetAction("PlayLast");
            context.StartService(intent);
        }

        public static bool SongIsContained(long audioID, long playlistID)
        {
            Uri uri = MediaStore.Audio.Playlists.Members.GetContentUri("external", playlistID);
            CursorLoader loader = new CursorLoader(Android.App.Application.Context, uri, null, null, null, null);
            ICursor cursor = (ICursor)loader.LoadInBackground();

            if (cursor != null && cursor.MoveToFirst())
            {
                int idColumn = cursor.GetColumnIndex(MediaStore.Audio.Playlists.Members.AudioId);
                do
                {
                    long id = cursor.GetLong(idColumn);
                    if (id == audioID)
                        return true;
                }
                while (cursor.MoveToNext());
                cursor.Close();
            }
            return false;
        }

        public static /*async*/ void GetPlaylist(Song item)
        {
            List<PlaylistItem> LocalPlaylists = new List<PlaylistItem>();

            Uri uri = MediaStore.Audio.Playlists.ExternalContentUri;
            CursorLoader loader = new CursorLoader(Android.App.Application.Context, uri, null, null, null, null);
            ICursor cursor = (ICursor)loader.LoadInBackground();

            if (cursor != null && cursor.MoveToFirst())
            {
                int nameID = cursor.GetColumnIndex(MediaStore.Audio.Playlists.InterfaceConsts.Name);
                int pathID = cursor.GetColumnIndex(MediaStore.Audio.Playlists.InterfaceConsts.Data);
                int playlistID = cursor.GetColumnIndex(MediaStore.Audio.Playlists.InterfaceConsts.Id);
                do
                {
                    string name = cursor.GetString(nameID);
                    long id = cursor.GetLong(playlistID);
                    PlaylistItem playlist = new PlaylistItem(name, id)
                    {
                        SongContained = SongIsContained(item.Id, id)
                    };
                    LocalPlaylists.Add(playlist);
                }
                while (cursor.MoveToNext());
                cursor.Close();
            }
            List<PlaylistItem> YoutubePlaylists = new List<PlaylistItem>
            {
                new PlaylistItem("Loading", null)
            };

            View Layout = inflater.Inflate(Resource.Layout.AddToPlaylistLayout, null);
            if(MainActivity.Theme == 1)
            {
                Layout.FindViewById<ImageView>(Resource.Id.leftIcon).SetColorFilter(Color.White);
                Layout.FindViewById<View>(Resource.Id.divider).SetBackgroundColor(Color.White);
            }
            AlertDialog.Builder builder = new AlertDialog.Builder(act, MainActivity.dialogTheme);
            builder.SetTitle("Add to playlists");
            builder.SetView(Layout);
            RecyclerView ListView = Layout.FindViewById<RecyclerView>(Resource.Id.recycler);
            ListView.SetLayoutManager(new LinearLayoutManager(MainActivity.instance));
            AddToPlaylistAdapter adapter = new AddToPlaylistAdapter(LocalPlaylists, YoutubePlaylists);
            ListView.SetAdapter(adapter);
            adapter.ItemClick += async (sender, position) => 
            {
                AddToPlaylistHolder holder = (AddToPlaylistHolder)ListView.GetChildViewHolder(ListView.GetChildAt(position));
                bool add = !holder.Added.Checked;
                holder.Added.Checked = add;

                bool Local = position < LocalPlaylists.Count;
                PlaylistItem playlist = Local ? LocalPlaylists[position] : YoutubePlaylists[position - LocalPlaylists.Count];
                if (add)
                {
                    if (Local)
                        AddToPlaylist(item, playlist.Name, playlist.LocalID);
                }
                else
                {
                    if (playlist.SyncState == SyncState.True && playlist.YoutubeID != null && playlist.LocalID != 0)
                    {
                        if (item.TrackID == null)
                            item = await PlaylistTracks.CompleteItem(item, playlist.YoutubeID);
                    }
                    SnackbarCallback callback = new SnackbarCallback(item, playlist.LocalID);

                    Snackbar snackBar = Snackbar.Make(MainActivity.instance.FindViewById(Resource.Id.snackBar), (item.Title.Length > 20 ? item.Title.Substring(0, 17) + "..." : item.Title) + " has been removed from the playlist.", Snackbar.LengthLong)
                        .SetAction("Undo", (v) => { callback.canceled = true; });
                    snackBar.AddCallback(callback);
                    snackBar.View.FindViewById<TextView>(Resource.Id.snackbar_text).SetTextColor(Color.White);
                    snackBar.Show();
                }
            };
            builder.SetPositiveButton("OK", (sender, e) => { });
            AlertDialog dialog = builder.Create();
            Layout.FindViewById<LinearLayout>(Resource.Id.CreatePlaylist).Click += (sender, e) => { dialog.Dismiss(); CreatePlalistDialog(item); };
            dialog.Show();
        }

        public async static Task CheckWritePermission()
        {
            const string permission = Manifest.Permission.WriteExternalStorage;
            if (Android.Support.V4.Content.ContextCompat.CheckSelfPermission(act, permission) != (int)Permission.Granted)
            {
                string[] permissions = new string[] { permission };
                MainActivity.instance.RequestPermissions(permissions, 2659);

                await Task.Delay(1000);
                while (Android.Support.V4.Content.ContextCompat.CheckSelfPermission(act, permission) != (int)Permission.Granted)
                    await Task.Delay(500);
            }
            return;
        }

        public async static void AddToPlaylist(Song item, string playList, long LocalID, int position = -1, bool SyncBehave = true, bool saveAsSynced = false)
        {
            if(LocalID == -1)
            {
                LocalID = GetPlaylistID(playList);
                if (LocalID == -1)
                    CreatePlaylist(playList, item, saveAsSynced);
                else
                    AddToPlaylist(item, playList, LocalID, position);
            }
            else
            {
                await CheckWritePermission();

                ContentResolver resolver = act.ContentResolver;
                ContentValues value = new ContentValues();
                value.Put(MediaStore.Audio.Playlists.Members.AudioId, item.Id);
                value.Put(MediaStore.Audio.Playlists.Members.PlayOrder, 0);
                resolver.Insert(MediaStore.Audio.Playlists.Members.GetContentUri("external", LocalID), value);

                //Check if this playlist is synced, if it his, add the song to the youtube playlist
                if (SyncBehave)
                {
                    PlaylistItem SyncedPlaylist = null;
                    await Task.Run(() =>
                    {
                        SQLiteConnection db = new SQLiteConnection(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), "SyncedPlaylists.sqlite"));
                        db.CreateTable<PlaylistItem>();

                        SyncedPlaylist = db.Table<PlaylistItem>().ToList().Find(x => x.LocalID == LocalID);
                    });

                    if (SyncedPlaylist != null)
                    {
                        if (SyncedPlaylist.YoutubeID != null && SyncedPlaylist.HasWritePermission)
                        {
                            Song song = CompleteItem(item);
                            if (song.youtubeID != null)
                                YoutubeEngine.AddToPlaylist(song, playList, SyncedPlaylist.YoutubeID, MainActivity.instance, false);
                            else
                                Toast.MakeText(MainActivity.instance, "Can't find this song on youtube, it has only been added to the local playlist.", ToastLength.Long).Show();
                        }
                        else
                            Toast.MakeText(MainActivity.instance, "Playlist has not finished syncing yet, can't add this song to the youtube playlist (but has been added locally). Please check on the playlist view for more details.", ToastLength.Long).Show();
                    }
                }
            }
        }

        public static void CreatePlalistDialog(Song item)
        {
            AlertDialog.Builder builder = new AlertDialog.Builder(act, MainActivity.dialogTheme);
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

        public async static void CreatePlaylist(string name, Song item, bool syncedPlaylist = false)
        {
            await CheckWritePermission();

            ContentResolver resolver = act.ContentResolver;
            Uri uri = MediaStore.Audio.Playlists.ExternalContentUri;
            ContentValues value = new ContentValues();
            value.Put(MediaStore.Audio.Playlists.InterfaceConsts.Name, name);
            resolver.Insert(uri, value);

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

            AddToPlaylist(item, name, playlistID);

            if (syncedPlaylist)
            {
                await Task.Run(() =>
                {
                    SQLiteConnection db = new SQLiteConnection(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), "SyncedPlaylists.sqlite"));
                    db.CreateTable<PlaylistItem>();
                    db.InsertOrReplace(new PlaylistItem(name, playlistID, null));
                });
            }
        }

        public static long GetPlaylistID(string playlistName)
        {
            Uri uri = MediaStore.Audio.Playlists.ExternalContentUri;
            CursorLoader loader = new CursorLoader(Android.App.Application.Context, uri, null, null, null, null);
            ICursor cursor = (ICursor)loader.LoadInBackground();

            if (cursor != null && cursor.MoveToFirst())
            {
                int nameID = cursor.GetColumnIndex(MediaStore.Audio.Playlists.InterfaceConsts.Name);
                int plID = cursor.GetColumnIndex(MediaStore.Audio.Playlists.InterfaceConsts.Id);
                do
                {
                    string name = cursor.GetString(nameID);

                    if (name != playlistName)
                        continue;

                    System.Console.WriteLine("&Playlist exist");
                    return cursor.GetLong(plID);
                }
                while (cursor.MoveToNext());
                cursor.Close();
            }
            return -1;
        }

        public static void EditMetadata(Song item, string sender, IParcelable parcelable)
        {
            MainActivity.instance.HideTabs();
            MainActivity.parcelableSender = sender;
            MainActivity.parcelable = parcelable;
            Intent intent = new Intent(Android.App.Application.Context, typeof(EditMetaData));
            intent.PutExtra("Song", item.ToString());
            MainActivity.instance.StartActivity(intent);
        }

        public override void OnResume()
        {
            base.OnResume();
            instance = this;
            if(MainActivity.parcelable != null && MainActivity.parcelableSender == "Browse")
            {
                ListView.OnRestoreInstanceState(MainActivity.parcelable);
                MainActivity.parcelable = null;
                MainActivity.parcelableSender = null;
            }
        }
    }
}