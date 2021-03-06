﻿using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Gms.Auth.Api;
using Android.Gms.Auth.Api.SignIn;
using Android.Gms.Cast.Framework;
using Android.Gms.Cast.Framework.Media;
using Android.Gms.Common;
using Android.Gms.Common.Apis;
using Android.Graphics;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Android.Support.Design.Widget;
using Android.Support.V4.App;
using Android.Support.V4.Content;
using Android.Support.V4.Widget;
using Android.Support.V7.App;
using Android.Support.V7.Preferences;
using Android.Views;
using Android.Widget;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Opus.Adapter;
using Opus.Api;
using Opus.Api.Services;
using Opus.DataStructure;
using Opus.Fragments;
using Opus.Others;
using Square.Picasso;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using YoutubeExplode;
using Environment = Android.OS.Environment;
using Fragment = Android.Support.V4.App.Fragment;
using Playlist = Opus.Fragments.Playlist;
//using Request = Square.OkHttp.Request;
using SearchView = Android.Support.V7.Widget.SearchView;
using Toolbar = Android.Support.V7.Widget.Toolbar;
using TransportType = Android.Net.TransportType;
using Uri = Android.Net.Uri;

namespace Opus
{
    [Activity(Label = "Opus", MainLauncher = true, Icon = "@drawable/Icon", Theme = "@style/SplashScreen", ScreenOrientation = ScreenOrientation.Portrait, LaunchMode = LaunchMode.SingleTask, ResizeableActivity = true)]
    [IntentFilter(new[] {Intent.ActionSend }, Categories = new[] { Intent.CategoryDefault }, DataHost = "www.youtube.com", DataMimeType = "text/*")]
    [IntentFilter(new[] {Intent.ActionSend }, Categories = new[] { Intent.CategoryDefault }, DataHost = "m.youtube.com", DataMimeType = "text/plain")]
    [IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault }, DataMimeTypes = new[] { "audio/*", "application/ogg", "application/x-ogg", "application/itunes" })]
    public class MainActivity : AppCompatActivity, GoogleApiClient.IOnConnectionFailedListener, IResultCallback, IMenuItemOnActionExpandListener, View.IOnFocusChangeListener, ISessionManagerListener
    {
        public static MainActivity instance;
        public static int dialogTheme;

        public bool NoToolbarMenu = false;
        public IMenu menu;
        public SwipeRefreshLayout contentRefresh;
        public bool Paused = false;

        public bool SkipStop = false;
        public PlayerBehavior SheetBehavior;

        public const int RequestCode = 8539;
        private const int WriteRequestCode = 2659;
        public const int NotifUpdateID = 4626;
        private const string versionURI = "https://raw.githubusercontent.com/AnonymusRaccoon/Opus/master/Opus/Assets/Version.txt";

        public static GoogleSignInAccount account;
        private Intent AskIntent;
        public bool waitingForYoutube;
        private DateTime? NextRefreshDate;
        private bool? PermissionGot;
        public bool ResumeKiller;

        public static CastContext CastContext;


        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            LoadTheme(this);
            SetContentView(Resource.Layout.Main);
            instance = this;

            var bottomNavigation = FindViewById<BottomNavigationView>(Resource.Id.bottomView);
            bottomNavigation.NavigationItemSelected += PreNavigate;

            SetSupportActionBar(FindViewById<Toolbar>(Resource.Id.toolbar));
            SupportActionBar.SetDisplayShowTitleEnabled(false);

            contentRefresh = FindViewById<SwipeRefreshLayout>(Resource.Id.contentRefresh);

            if(savedInstanceState == null)
                Navigate(Resource.Id.musicLayout);

            SheetBehavior = (PlayerBehavior)BottomSheetBehavior.From(FindViewById(Resource.Id.playerSheet));
            SheetBehavior.State = BottomSheetBehavior.StateCollapsed;
            SheetBehavior.SetBottomSheetCallback(new PlayerCallback(this));

            PrepareSmallPlayer();
            if (MusicPlayer.queue == null || MusicPlayer.queue.Count == 0)
                MusicPlayer.RetrieveQueueFromDataBase();
            else if(SheetBehavior.State != BottomSheetBehavior.StateExpanded)
                ShowSmallPlayer();

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                NotificationManager notificationManager = (NotificationManager)GetSystemService(NotificationService);
                NotificationChannel channel = new NotificationChannel("Opus.Channel", "Default Channel", NotificationImportance.Low)
                {
                    Description = "Channel used for download progress and music control notification.",
                    LockscreenVisibility = NotificationVisibility.Public
                };
                channel.EnableVibration(false);
                channel.EnableLights(false);
                notificationManager.CreateNotificationChannel(channel);
            }

            CheckForUpdate(this, false);
            HandleIntent(Intent);
            Login(true);
            YoutubeManager.SyncPlaylists();
        }

        private void HandleIntent(Intent intent)
        {
            if (intent.Action == "Sleep")
            {
                ShowPlayer();
                Player.instance.SleepDialog();
            }
            else if (intent.Action == "Player")
            {
                ShowPlayer();
                Player.instance.RefreshPlayer();
            }
            else if (intent.Action == Intent.ActionView && intent.Data != null)
            {
                MusicPlayer.queue.Clear();
                Intent inte = new Intent(this, typeof(MusicPlayer));
                inte.PutExtra("file", intent.Data.ToString());
                StartService(inte);

                ShowPlayer();
            }
            else if (intent.Action == Intent.ActionSend)
            {
                if (YoutubeClient.TryParseVideoId(intent.GetStringExtra(Intent.ExtraText), out string videoID))
                {
                    Intent inten = new Intent(this, typeof(MusicPlayer));
                    inten.SetAction("YoutubePlay");
                    inten.PutExtra("action", "Play");
                    inten.PutExtra("file", videoID);
                    StartService(inten);
                }
                else
                {
                    Toast.MakeText(this, Resource.String.cant_play_non_youtube, ToastLength.Short).Show();
                    Finish();
                }
            }
        }

        protected override void OnStart()
        {
            base.OnStart();

            if (GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(this) == ConnectionResult.Success)
            {
                CastContext = CastContext.GetSharedInstance(this);
                CastContext.SessionManager.AddSessionManagerListener(this);
            }
            else
                CastContext = null;
        }

        protected override void OnResume()
        {
            base.OnResume();
            instance = this;
            Paused = false;

            if ((CastContext == null || CastContext.SessionManager.CurrentSession == null) && MusicPlayer.CurrentID() == -1)
                MusicPlayer.currentID = MusicPlayer.RetrieveQueueSlot();
            else if (MusicPlayer.UseCastPlayer)
                MusicPlayer.GetQueueFromCast();

            if (SearchableActivity.instance != null)
            {
                IMenuItem searchItem = menu.FindItem(Resource.Id.search);
                SearchView searchView = (SearchView)searchItem.ActionView;
                searchView.ClearFocus();

                if(SearchableActivity.instance.SearchQuery != null && SearchableActivity.instance.SearchQuery != "")
                {
                    if (YoutubeSearch.instances == null || SearchableActivity.instance.SearchQuery != YoutubeSearch.instances[0].querryType) //We don't want to redo the query if the user already searched for the exact same query.
                    {
                        HideFilter();
                        SupportFragmentManager.BeginTransaction().Replace(Resource.Id.contentView, Pager.NewInstance(SearchableActivity.instance.SearchQuery, 0)).AddToBackStack("Youtube").Commit();
                    }
                }
                SearchableActivity.instance = null;
            }

            if (SheetBehavior != null && SheetBehavior.State == BottomSheetBehavior.StateExpanded)
                FindViewById<NestedScrollView>(Resource.Id.playerSheet).Visibility = ViewStates.Visible;
        }

        protected override void OnDestroy()
        {
            YoutubeSearch.instances = null;

            if (MusicPlayer.instance != null && !MusicPlayer.isRunning && Preferences.instance == null && EditMetaData.instance == null)
            {
                Intent intent = new Intent(this, typeof(MusicPlayer));
                intent.SetAction("Stop");
                StartService(intent);
            }
            base.OnDestroy();
        }

        protected override void OnPause()
        {
            base.OnPause();
            Paused = true;
        }

        public override void OnBackPressed()
        {
            if (Player.instance?.DrawerLayout.IsDrawerOpen((int)GravityFlags.Start) == true)
                Player.instance?.DrawerLayout.CloseDrawer((int)GravityFlags.Start);
            else if (SheetBehavior.State == BottomSheetBehavior.StateExpanded)
                SheetBehavior.State = BottomSheetBehavior.StateCollapsed;
            else
                base.OnBackPressed();
        }

        protected override void OnNewIntent(Intent intent)
        {
            base.OnNewIntent(intent);
            HandleIntent(intent);
        }

        public static int GetThemeID(Context context)
        {
            ISharedPreferences pref = PreferenceManager.GetDefaultSharedPreferences(context);
            return pref.GetInt("theme", 0);
        }

        public static void LoadTheme(Context context)
        {
            int themeRes;
            switch (GetThemeID(context))
            {
                case 0:
                default:
                    themeRes = Resource.Style.Theme;
                    dialogTheme = Resource.Style.AppCompatAlertDialogStyle;
                    break;
                case 1:
                    themeRes = Resource.Style.DarkTheme;
                    dialogTheme = Resource.Style.AppCompatDarkAlertDialogStyle;
                    break;
                case 2:
                    themeRes = Resource.Style.BlackTheme;
                    dialogTheme = Resource.Style.AppCompatDarkAlertDialogStyle;
                    break;
            }
            context.SetTheme(themeRes);
        }
        //UI PART

        #region Toolbar menu (right items)
        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            if (NoToolbarMenu)
            {
                menu = null;
                return base.OnCreateOptionsMenu(menu);
            }

            MenuInflater.Inflate(Resource.Menu.toolbar_menu, menu);
            this.menu = menu;

            if(account != null)
                Picasso.With(this).Load(account.PhotoUrl).Transform(new CircleTransformation()).Into(new AccountTarget());

            menu.FindItem(Resource.Id.search).SetOnActionExpandListener(this);
            ((SearchView)menu.FindItem(Resource.Id.search).ActionView).SetOnQueryTextFocusChangeListener(this);
            ((SearchView)menu.FindItem(Resource.Id.search).ActionView).QueryHint = GetString(Resource.String.youtube_search);
            ((SearchView)menu.FindItem(Resource.Id.filter).ActionView).QueryHint = GetString(Resource.String.filter_hint);

            CastButtonFactory.SetUpMediaRouteButton(this, menu, Resource.Id.media_route_menu_item);
            return base.OnCreateOptionsMenu(menu);
        }

        public void AddFilterListener(EventHandler<SearchView.QueryTextChangeEventArgs> textChanged)
        {
            if (menu == null)
                return;

            var item = menu.FindItem(Resource.Id.filter);
            var filterView = item.ActionView.JavaCast<SearchView>();
            filterView.QueryTextChange += textChanged;
        }

        public void RemoveFilterListener(EventHandler<SearchView.QueryTextChangeEventArgs> textChanged)
        {
            var item = menu.FindItem(Resource.Id.filter);
            var filterView = item.ActionView.JavaCast<SearchView>();
            filterView.QueryTextChange -= textChanged;
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            if(item.ItemId == Android.Resource.Id.Home)
            {
                if (PlaylistTracks.instance != null  || FolderTracks.instance != null || ChannelDetails.instance != null)
                {
                    for (int i = 0; i < SupportFragmentManager.BackStackEntryCount; i++)
                    {
                        Console.WriteLine("&Back stack entry " + i + ": " + SupportFragmentManager.GetBackStackEntryAt(i));
                    }

                    SupportFragmentManager.PopBackStack();
                    Console.WriteLine("&YoutubeEngine instance: " + YoutubeSearch.instances);
                    
                    if (YoutubeSearch.instances != null)
                    {
                        Console.WriteLine("&Doing youtube back");
                        FindViewById<TabLayout>(Resource.Id.tabs).Visibility = ViewStates.Visible;
                        SearchView searchView = (SearchView)menu.FindItem(Resource.Id.search).ActionView;
                        searchView.Focusable = false;
                        menu.FindItem(Resource.Id.search).ExpandActionView();
                        searchView.SetQuery(YoutubeSearch.instances[0].Query, false);
                        searchView.ClearFocus();

                        //int selectedTab = 0;
                        //for (int i = 0; i < YoutubeSearch.instances.Length; i++)
                        //{
                        //    if (YoutubeSearch.instances[i].IsFocused)
                        //        selectedTab = i;
                        //}
                    }
                }
                else if (YoutubeSearch.instances != null)
                {
                    var searchView = menu.FindItem(Resource.Id.search).ActionView.JavaCast<SearchView>();
                    menu.FindItem(Resource.Id.search).CollapseActionView();
                    searchView.ClearFocus();
                    searchView.Iconified = true;
                    searchView.SetQuery("", false);
                    SupportActionBar.SetDisplayHomeAsUpEnabled(false);
                    YoutubeSearch.instances = null;
                }
            }
            else if(item.ItemId == Resource.Id.search)
            {
                menu.FindItem(Resource.Id.filter).CollapseActionView();
            }
            else if(item.ItemId == Resource.Id.settings)
            {
                Intent intent = new Intent(Application.Context, typeof(Preferences));
                StartActivity(intent);
            }
            return base.OnOptionsItemSelected(item);
        }

        public bool OnMenuItemActionCollapse(IMenuItem item) //Youtube search collapse
        {
            if (YoutubeSearch.instances == null || !item.ActionView.Focusable)
                return true;

            SupportFragmentManager.PopBackStack();
            return true;
        }

        public bool OnMenuItemActionExpand(IMenuItem item)
        {
            return true;
        }

        public void OnFocusChange(View v, bool hasFocus)
        {
            if (hasFocus && v.Focusable)
            {
                Bundle animation = ActivityOptionsCompat.MakeCustomAnimation(this, Android.Resource.Animation.FadeIn, Android.Resource.Animation.FadeOut).ToBundle();
                StartActivity(new Intent(this, typeof(SearchableActivity)), animation);
            }
        }

        public void CancelSearch() //SearchableActivity is finishing and no search has been made
        {
            IMenuItem searchItem = menu.FindItem(Resource.Id.search);
            searchItem.CollapseActionView();
        }

        public void HideFilter()
        {
            if (menu == null)
                return;

            var item = menu.FindItem(Resource.Id.filter);
            var searchItem = item.ActionView;
            var searchView = searchItem.JavaCast<SearchView>();

            searchView.ClearFocus();
            searchView.OnActionViewCollapsed();

            item.SetVisible(false);
            item.CollapseActionView();
        }

        public void DisplayFilter()
        {
            var item = menu.FindItem(Resource.Id.filter);
            item.SetVisible(true);
            item.CollapseActionView();
            var searchItem = item.ActionView;
            var searchView = searchItem.JavaCast<SearchView>();

            searchView.ClearFocus();
            searchView.OnActionViewCollapsed();
        }
        #endregion

        #region BottomNavigation
        private void PreNavigate(object sender, BottomNavigationView.NavigationItemSelectedEventArgs e)
        {
            Navigate(e.Item.ItemId);
        }

        public void Navigate(int layout)
        {
            contentRefresh.Refreshing = false;

            if(menu?.FindItem(Resource.Id.search)?.IsActionViewExpanded == true)
            {
                var searchView = menu.FindItem(Resource.Id.search).ActionView.JavaCast<SearchView>();
                menu.FindItem(Resource.Id.search).CollapseActionView();
                searchView.ClearFocus();
                searchView.Iconified = true;
                searchView.SetQuery("", false);
            }

            if (YoutubeSearch.instances != null)
            {
                SupportActionBar.SetDisplayHomeAsUpEnabled(false);
                SupportFragmentManager.PopBackStack(null, Android.Support.V4.App.FragmentManager.PopBackStackInclusive);
            }

            if(FindViewById(Resource.Id.toolbarLogo) != null)
                FindViewById(Resource.Id.toolbarLogo).Visibility = ViewStates.Visible;

            if (PlaylistTracks.instance != null)
            {
                SupportFragmentManager.BeginTransaction().Remove(PlaylistTracks.instance).Commit();
                SupportFragmentManager.PopBackStack(null, Android.Support.V4.App.FragmentManager.PopBackStackInclusive);
            }

            if (ChannelDetails.instance != null)
            {
                SupportFragmentManager.BeginTransaction().Remove(ChannelDetails.instance).Commit();
                SupportFragmentManager.PopBackStack(null, Android.Support.V4.App.FragmentManager.PopBackStackInclusive);
            }

            Fragment fragment = null;
            switch (layout)
            {
                case Resource.Id.musicLayout:
                    if (Home.instance != null && Home.instance.ListView != null)
                    {
                        Home.instance.ListView.ScrollToPosition(0);
                        return;
                    }

                    HideFilter();
                    fragment = Home.NewInstance();
                    break;

                case Resource.Id.browseLayout:
                    if (Browse.instance != null && Pager.instance != null && Browse.instance.ListView != null)
                    {
                        Pager.instance.ScrollToFirst();
                        Browse.instance.ListView.ScrollToPosition(0);
                        return;
                    }

                    DisplayFilter();
                    fragment = Pager.NewInstance(0, 0);
                    break;

                case Resource.Id.playlistLayout:
                    if (Playlist.instance != null)
                    {
                        Playlist.instance.ListView.ScrollToPosition(0);
                        return;
                    }

                    HideFilter();
                    fragment = Playlist.NewInstance();
                    break;
            }

            if (fragment == null)
                return;

            SupportFragmentManager.BeginTransaction().Replace(Resource.Id.contentView, fragment).SetCustomAnimations(Android.Resource.Animation.FadeIn, Android.Resource.Animation.FadeOut).Commit();
        }
        #endregion

        #region SmallPlayer
        public void PrepareSmallPlayer()
        {
            FrameLayout smallPlayer = FindViewById<FrameLayout>(Resource.Id.smallPlayer);
            if (!smallPlayer.FindViewById<ImageButton>(Resource.Id.spPlay).HasOnClickListeners)
            {
                smallPlayer.FindViewById<ImageButton>(Resource.Id.spLast).Click += Last_Click;
                smallPlayer.FindViewById<ImageButton>(Resource.Id.spPlay).Click += Play_Click;
                smallPlayer.FindViewById<ImageButton>(Resource.Id.spNext).Click += Next_Click;

                smallPlayer.FindViewById<LinearLayout>(Resource.Id.spContainer).Click += Container_Click;
            }
        }

        private void Last_Click(object sender, EventArgs e)
        {
            Intent intent = new Intent(Application.Context, typeof(MusicPlayer));
            intent.SetAction("Previus");
            StartService(intent);
        }

        private void Play_Click(object sender, EventArgs e)
        {
            if (Player.errorState == true)
            {
                MusicPlayer.instance?.Resume();
                Player.errorState = false;
                return;
            }

            Intent intent = new Intent(Application.Context, typeof(MusicPlayer));
            intent.SetAction("Pause");
            StartService(intent);
        }

        private void Next_Click(object sender, EventArgs e)
        {
            Intent intent = new Intent(Application.Context, typeof(MusicPlayer));
            intent.SetAction("Next");
            StartService(intent);
        }

        private void Container_Click(object sender, EventArgs e)
        {
            ShowSmallPlayer();
            ShowPlayer();
        }

        public void ShowPlayer()
        {
            FindViewById<NestedScrollView>(Resource.Id.playerSheet).Visibility = ViewStates.Visible;
            FindViewById<NestedScrollView>(Resource.Id.playerSheet).TranslationY = 0;
            FindViewById<BottomNavigationView>(Resource.Id.bottomView).TranslationY = DpToPx(56);
            FindViewById(Resource.Id.playerContainer).Alpha = 1;
            FindViewById(Resource.Id.smallPlayer).Alpha = 0;
            SheetBehavior.State = BottomSheetBehavior.StateExpanded;
            FindViewById<FrameLayout>(Resource.Id.contentView).SetPadding(0, 0, 0, DpToPx(70));
        }

        public void ShowSmallPlayer()
        {
            FindViewById<NestedScrollView>(Resource.Id.playerSheet).Visibility = ViewStates.Visible;
            FindViewById(Resource.Id.playerContainer).Alpha = 0;
            FindViewById(Resource.Id.smallPlayer).Alpha = 1;
            SheetBehavior.State = BottomSheetBehavior.StateCollapsed;
            Player.instance.RefreshPlayer();
            FindViewById<FrameLayout>(Resource.Id.contentView).SetPadding(0, 0, 0, DpToPx(70));
        }

        public void HideSmallPlayer()
        {
            SkipStop = true;
            FindViewById<FrameLayout>(Resource.Id.contentView).SetPadding(0, 0, 0, 0);
            SheetBehavior.State = BottomSheetBehavior.StateHidden;
            FindViewById<NestedScrollView>(Resource.Id.playerSheet).Visibility = ViewStates.Gone;
        }
        #endregion

        #region More Menues
        public async void More(Song item, Action overridedPlayAction = null, BottomSheetAction endAction = null)
        {
            if (!item.IsYt)
                item = LocalManager.CompleteItem(item);

            BottomSheetDialog bottomSheet = new BottomSheetDialog(this);
            View bottomView = LayoutInflater.Inflate(Resource.Layout.BottomSheet, null);
            bottomView.FindViewById<TextView>(Resource.Id.bsTitle).Text = item.Title;
            bottomView.FindViewById<TextView>(Resource.Id.bsArtist).Text = item.Artist;
            bottomSheet.SetContentView(bottomView);
            if (item.AlbumArt == -1 || item.IsYt)
            {
                Picasso.With(this).Load(item.Album).Placeholder(Resource.Drawable.noAlbum).Transform(new RemoveBlackBorder(true)).Into(bottomView.FindViewById<ImageView>(Resource.Id.bsArt));
            }
            else
            {
                var songCover = Uri.Parse("content://media/external/audio/albumart");
                var songAlbumArtUri = ContentUris.WithAppendedId(songCover, item.AlbumArt);

                Picasso.With(this).Load(songAlbumArtUri).Placeholder(Resource.Drawable.noAlbum).Resize(400, 400).CenterCrop().Into(bottomView.FindViewById<ImageView>(Resource.Id.bsArt));
            }

            List<BottomSheetAction> actions = new List<BottomSheetAction>
            {
                new BottomSheetAction(Resource.Drawable.Play, Resources.GetString(Resource.String.play), (sender, eventArg) => 
                {
                    if(overridedPlayAction == null)
                        SongManager.Play(item);
                    else
                        overridedPlayAction.Invoke();
                    bottomSheet.Dismiss();
                }),
                new BottomSheetAction(Resource.Drawable.PlaylistPlay, Resources.GetString(Resource.String.play_next), (sender, eventArg) => 
                {
                    SongManager.PlayNext(item);
                    bottomSheet.Dismiss();
                }),
                new BottomSheetAction(Resource.Drawable.Queue, Resources.GetString(Resource.String.play_last), (sender, eventArg) => 
                {
                    SongManager.PlayLast(item);
                    bottomSheet.Dismiss();
                }),
                new BottomSheetAction(Resource.Drawable.PlaylistAdd, Resources.GetString(Resource.String.add_to_playlist), (sender, eventArg) => 
                {
                    PlaylistManager.AddSongToPlaylistDialog(item);
                    bottomSheet.Dismiss();
                }),
            };

            if (await SongManager.IsFavorite(item))
                actions.Add(new BottomSheetAction(Resource.Drawable.Fav, Resources.GetString(Resource.String.unfav), (sender, eventArg) => { SongManager.UnFav(item); bottomSheet.Dismiss(); }));
            else
                actions.Add(new BottomSheetAction(Resource.Drawable.Unfav, Resources.GetString(Resource.String.fav), (sender, eventArg) => { SongManager.Fav(item); bottomSheet.Dismiss(); }));

            if (!item.IsYt)
            {
                actions.Add(new BottomSheetAction(Resource.Drawable.Edit, Resources.GetString(Resource.String.edit_metadata), (sender, eventArg) =>
                {
                    LocalManager.EditMetadata(item);
                    bottomSheet.Dismiss();
                }));
            }
            else
            {
                if (item.ChannelID != null)
                {
                    actions.Add(new BottomSheetAction(Resource.Drawable.account, Resources.GetString(Resource.String.goto_channel), (sender, eventArg) =>
                    {
                        if(YoutubeSearch.instances != null)
                        {
                            menu.FindItem(Resource.Id.search).ActionView.Focusable = false;
                            menu.FindItem(Resource.Id.search).CollapseActionView();
                            menu.FindItem(Resource.Id.search).ActionView.Focusable = true;
                            FindViewById<TabLayout>(Resource.Id.tabs).Visibility = ViewStates.Gone;
                        }
                        ChannelManager.OpenChannelTab(item.ChannelID);
                        bottomSheet.Dismiss();
                    }));
                }

                actions.AddRange(new BottomSheetAction[]
                {
                    new BottomSheetAction(Resource.Drawable.PlayCircle, Resources.GetString(Resource.String.create_mix_from_song), (sender, eventArg) =>
                    {
                        YoutubeManager.CreateMixFromSong(item);
                        bottomSheet.Dismiss();
                    }),
                    new BottomSheetAction(Resource.Drawable.Download, Resources.GetString(Resource.String.download), (sender, eventArg) =>
                    {
                        YoutubeManager.Download(new[] { item });
                        bottomSheet.Dismiss();
                    })
                });
            }

            if (endAction != null)
            {
                actions.Add(new BottomSheetAction(endAction)
                {
                    action = (sender, eventArg) =>
                    {
                        endAction.action.Invoke(sender, eventArg);
                        bottomSheet.Dismiss();
                    }
                });
            }

            bottomSheet.FindViewById<ListView>(Resource.Id.bsItems).Adapter = new BottomSheetAdapter(this, Resource.Layout.BottomSheetText, actions);
            bottomSheet.Show();
        }
        #endregion

        #region Snackbars
        //public void YoutubeEndPointChanged()
        //{
        //    FindViewById<ProgressBar>(Resource.Id.ytProgress).Visibility = ViewStates.Gone;
        //    Snackbar snackBar = Snackbar.Make(FindViewById(Resource.Id.snackBar), Resource.String.youtube_endpoint, Snackbar.LengthLong);
        //    snackBar.View.FindViewById<TextView>(Resource.Id.snackbar_text).SetTextColor(Color.White);
        //    snackBar.Show();

        //    Player.instance.Ready();
        //}

        public void Timout()
        {
            Snackbar snackBar = Snackbar.Make(FindViewById(Resource.Id.snackBar), Resource.String.timout, Snackbar.LengthLong);
            snackBar.View.FindViewById<TextView>(Resource.Id.snackbar_text).SetTextColor(Color.White);
            snackBar.Show();
        }

        public void UnknowError(ErrorCode code, Action action = null, int Length = Snackbar.LengthIndefinite)
        {
            Snackbar snackBar = Snackbar.Make(FindViewById(Resource.Id.snackBar), GetString(Resource.String.unknow) + " (" + code + ")", Length);
            if (action != null)
                snackBar.SetAction("Try Again", (sender) => { action.Invoke(); snackBar.Dismiss(); });
            else
                snackBar.SetAction("Ok", (sender) => { snackBar.Dismiss(); });
            snackBar.View.FindViewById<TextView>(Resource.Id.snackbar_text).SetTextColor(Color.White);
            snackBar.Show();
        }

        public void Unplayable(ErrorCode code, string title, string msg)
        {
            if (msg.Contains("country"))
            {
                Snackbar snackBar = Snackbar.Make(FindViewById(Resource.Id.snackBar), title + " " + GetString(Resource.String.country_blocked), Snackbar.LengthLong);
                snackBar.View.FindViewById<TextView>(Resource.Id.snackbar_text).SetTextColor(Color.White);
                snackBar.Show();
            }
            else if (msg.Contains("not available"))
            {
                Snackbar snackBar = Snackbar.Make(FindViewById(Resource.Id.snackBar), title + " " + GetString(Resource.String.not_streamable), Snackbar.LengthLong);
                snackBar.View.FindViewById<TextView>(Resource.Id.snackbar_text).SetTextColor(Color.White);
                snackBar.Show();
            }
            else
                UnknowError(code);
        }

        public void NotStreamable(string title)
        {
            Snackbar snackBar = Snackbar.Make(FindViewById(Resource.Id.snackBar), title + GetString(Resource.String.not_streamable), Snackbar.LengthLong);
            snackBar.View.FindViewById<TextView>(Resource.Id.snackbar_text).SetTextColor(Color.White);
            snackBar.Show();
        }
        #endregion

        #region Updater
        public async static void CheckForUpdate(Activity activity, bool displayToast)
        {
            if (!HasInternet())
            {
                if (displayToast)
                {
                    if (instance != null && !instance.Paused)
                    {
                        Snackbar snackBar = Snackbar.Make(instance.FindViewById(Resource.Id.snackBar), activity.GetString(Resource.String.update_no_internet), Snackbar.LengthLong);
                        snackBar.View.FindViewById<TextView>(Resource.Id.snackbar_text).SetTextColor(Color.White);
                        snackBar.Show();
                    }
                    if (Preferences.instance != null)
                    {
                        Snackbar snackBar = Snackbar.Make(Preferences.instance.FindViewById(Android.Resource.Id.Content), activity.GetString(Resource.String.update_no_internet), Snackbar.LengthLong);
                        snackBar.View.FindViewById<TextView>(Resource.Id.snackbar_text).SetTextColor(Color.White);
                        snackBar.Show();
                    }
                    else
                        Toast.MakeText(Application.Context, Resource.String.update_no_internet, ToastLength.Short).Show();
                }
                return;
            }

            string VersionAsset;
            AssetManager assets = Application.Context.Assets;
            using (StreamReader sr = new StreamReader(assets.Open("Version.txt")))
            {
                VersionAsset = sr.ReadToEnd();
            }

            string versionID = VersionAsset.Substring(9, 5);
            versionID = versionID.Remove(1, 1);
            int version = int.Parse(versionID.Remove(2, 1));

            string gitVersionID;
            int gitVersion;
            string downloadPath;
            bool beta = false;

            using (WebClient client = new WebClient())
            {
                string GitVersion = await client.DownloadStringTaskAsync(new System.Uri(versionURI));
                gitVersionID = GitVersion.Substring(9, 5);
                string gitID = gitVersionID.Remove(1, 1);
                gitVersion = int.Parse(gitID.Remove(2, 1));
                bool.TryParse(GitVersion.Substring(GitVersion.IndexOf("Beta: ") + 6, GitVersion.IndexOf("Link: ")), out beta);
                downloadPath = GitVersion.Substring(GitVersion.IndexOf("Link: ") + 6);
            }

            if (gitVersion > version && !beta)
            {
                Android.Support.V7.App.AlertDialog.Builder builder = new Android.Support.V7.App.AlertDialog.Builder(activity, dialogTheme);
                builder.SetTitle(activity.GetString(Resource.String.update, gitVersionID));
                builder.SetMessage(activity.GetString(Resource.String.update_message));
                builder.SetPositiveButton(activity.GetString(Resource.String.ok), (sender, e) => { InstallUpdate(gitVersionID, false, downloadPath); });
                builder.SetNegativeButton(activity.GetString(Resource.String.later), (sender, e) => { });
                builder.Show();
            }
            else if (displayToast)
            {
                if (!beta)
                {
                    if ((instance != null && !instance.Paused) || Preferences.instance != null)
                    {
                        Snackbar snackBar;
                        if (Preferences.instance != null)
                            snackBar = Snackbar.Make(Preferences.instance.FindViewById(Android.Resource.Id.Content), activity.GetString(Resource.String.up_to_date), Snackbar.LengthLong);
                        else
                            snackBar = Snackbar.Make(instance.FindViewById(Resource.Id.snackBar), activity.GetString(Resource.String.up_to_date), Snackbar.LengthLong);
                        snackBar.View.FindViewById<TextView>(Resource.Id.snackbar_text).SetTextColor(Color.White);
                        snackBar.Show();
                    }
                    else
                        Toast.MakeText(Application.Context, Resource.String.up_to_date, ToastLength.Short).Show();
                }
                else
                {
                    if ((instance != null && !instance.Paused) || Preferences.instance != null)
                    {
                        Snackbar snackBar;
                        if (Preferences.instance != null)
                            snackBar = Snackbar.Make(Preferences.instance.FindViewById(Android.Resource.Id.Content), activity.GetString(Resource.String.beta_available), Snackbar.LengthLong);
                        else
                            snackBar = Snackbar.Make(instance.FindViewById(Resource.Id.snackBar), activity.GetString(Resource.String.beta_available), Snackbar.LengthLong);
                        snackBar.SetAction(activity.GetString(Resource.String.download), (sender) =>
                        {
                            InstallUpdate(gitVersionID, true, downloadPath);
                        });
                        snackBar.View.FindViewById<TextView>(Resource.Id.snackbar_text).SetTextColor(Color.White);
                        snackBar.Show();
                    }
                    else
                        Toast.MakeText(Application.Context, Resource.String.beta_available, ToastLength.Short).Show();
                }
            }
        }

        public async static void InstallUpdate(string version, bool beta, string downloadPath)
        {
            if (await instance.GetWritePermission())
            {
                string localPath = Environment.GetExternalStoragePublicDirectory(Environment.DirectoryDownloads).AbsolutePath + "/Opus-v" + version + (beta ? "-beta" : "") + ".apk";

                Toast.MakeText(Application.Context, Application.Context.GetString(Resource.String.downloading_update), ToastLength.Short).Show();

                NotificationCompat.Builder notification = new NotificationCompat.Builder(Application.Context, "Opus.Channel")
                    .SetVisibility(NotificationCompat.VisibilityPublic)
                    .SetSmallIcon(Resource.Drawable.NotificationIcon)
                    .SetContentTitle(Application.Context.GetString(Resource.String.updating))
                    .SetOngoing(true);

                NotificationManager notificationManager = (NotificationManager)Application.Context.GetSystemService(NotificationService);
                notificationManager.Notify(NotifUpdateID, notification.Build());

                using (WebClient client = new WebClient())
                {
                    await client.DownloadFileTaskAsync(downloadPath, localPath);
                }

                notificationManager.Cancel(NotifUpdateID);

                Intent intent = new Intent(Intent.ActionView);
                intent.SetDataAndType(FileProvider.GetUriForFile(instance, Application.Context.PackageName + ".provider", new Java.IO.File(localPath)), "application/vnd.android.package-archive");
                intent.SetFlags(ActivityFlags.NewTask);
                intent.AddFlags(ActivityFlags.GrantReadUriPermission);
                Application.Context.StartActivity(intent);
            }
        }
        #endregion

        //API PART THAT NEED CONTEXT TO WORK (SO THEY ARE HERE)

        #region Login with google services and creation of the youtube service object
        public void Login(bool canAsk = true, bool skipSilentLog = false, bool skipLastSigned = false)
        {
            if(canAsk && skipSilentLog && skipLastSigned)
            {
                Snackbar snackBar = Snackbar.Make(FindViewById(Resource.Id.snackBar), Resource.String.login_disabled, Snackbar.LengthLong);
                snackBar.View.FindViewById<TextView>(Resource.Id.snackbar_text).SetTextColor(Color.White);
                snackBar.Show();
            }
            //waitingForYoutube = true;

            //if (!skipLastSigned)
            //{
            //    if (account == null)
            //        account = GoogleSignIn.GetLastSignedInAccount(this);


            //    if (account != null)
            //    {
            //        CreateYoutube();
            //        return;
            //    }
            //}

            //This will be used only when the access has been revoked, when the refresh token has been lost or for the first loggin. 
            //In each case we want a refresh token so we call RequestServerAuthCode with true as the second parameter.
            //GoogleSignInOptions gso = new GoogleSignInOptions.Builder(GoogleSignInOptions.DefaultSignIn)
            //    .RequestIdToken(GetString(Resource.String.clientID))
            //    .RequestServerAuthCode(GetString(Resource.String.clientID), true)
            //    .RequestScopes(new Scope(YouTubeService.Scope.Youtube))
            //    .Build();

            //GoogleApiClient googleClient = new GoogleApiClient.Builder(this)
            //    .AddApi(Auth.GOOGLE_SIGN_IN_API, gso)
            //    .Build();

            //googleClient.Connect();

            //if (!skipSilentLog)
            //{
            //    OptionalPendingResult silentLog = Auth.GoogleSignInApi.SilentSignIn(googleClient);
            //    if (silentLog.IsDone)
            //    {
            //        GoogleSignInResult result = (GoogleSignInResult)silentLog.Get();
            //        if (result.IsSuccess)
            //        {
            //            account = result.SignInAccount;
            //            RunOnUiThread(() => { Picasso.With(this).Load(account.PhotoUrl).Transform(new CircleTransformation()).Into(new AccountTarget()); });
            //            CreateYoutube();
            //        }
            //    }
            //    else if (silentLog != null)
            //    {
            //        AskIntent = Auth.GoogleSignInApi.GetSignInIntent(googleClient);
            //        silentLog.SetResultCallback(this);
            //    }
            //    else if (canAsk)
            //    {
            //        ResumeKiller = true;
            //        StartActivityForResult(Auth.GoogleSignInApi.GetSignInIntent(googleClient), 1598);
            //    }
            //}
            //else if (canAsk)
            //{
            //ResumeKiller = true;
            //StartActivityForResult(Auth.GoogleSignInApi.GetSignInIntent(googleClient), 1598);
            //}
            //else
            //{
            CreateYoutube(false);
            //}
        }

        protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (requestCode == 1598)
            {
                GoogleSignInResult result = Auth.GoogleSignInApi.GetSignInResultFromIntent(data);
                Console.WriteLine("&Result: " + result.ToString());
                if (result.IsSuccess)
                {
                    account = result.SignInAccount;
                    RunOnUiThread(() => { Picasso.With(this).Load(account.PhotoUrl).Transform(new CircleTransformation()).Into(new AccountTarget()); });
                    CreateYoutube();
                }
                else
                {
                    Console.WriteLine("&Loging error: " + result.Status);
                    waitingForYoutube = false;
                    CreateYoutube(false);
                }
            }
        }

        public void OnResult(Java.Lang.Object result) //Silent log result
        {
            account = ((GoogleSignInResult)result).SignInAccount;
            if (account != null)
            {
                RunOnUiThread(() => { Picasso.With(this).Load(account.PhotoUrl).Transform(new CircleTransformation()).Into(new AccountTarget()); });
                CreateYoutube();
            }
            else if (AskIntent != null)
            {
                ResumeKiller = true;
                StartActivityForResult(AskIntent, 1598);
                AskIntent = null;
            }
            else
            {
                CreateYoutube(false);
            }
        }

        public /*async*/ void CreateYoutube(bool UseToken = true)
        {
            //if(/*!UseToken &&*/ YoutubeManager.YoutubeService == null)
            //{
            YoutubeManager.YoutubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = GetString(Resource.String.yt_api_key),
                ApplicationName = "Opus"
            });
            YoutubeManager.IsUsingAPI = true;
            NextRefreshDate = DateTime.MaxValue;
            Console.WriteLine("&Youtube service created - " + YoutubeManager.YoutubeService);
            return;
            //}

            //YoutubeManager.IsUsingAPI = false;
            //NextRefreshDate = null;
            //ISharedPreferences prefManager = PreferenceManager.GetDefaultSharedPreferences(this);
            //string refreshToken = prefManager.GetString("refresh-token", null);
            //Console.WriteLine("&Current refresh token: " + refreshToken);

            //This method do not return refresh-token if the app has already been aprouved by google for this user, should force request
            //if (refreshToken == null)
            //{
            //Console.WriteLine("&Getting refresh-token and creating a youtube service");
            //Console.WriteLine("&Code = " + account.ServerAuthCode);

            //if (account.ServerAuthCode == null)
            //{
            //    Login(true, false, true);
            //    return;
            //}

            //Dictionary<string, string> fields = new Dictionary<string, string>
            //{
            //    { "grant_type", "authorization_code" },
            //    { "client_id", GetString(Resource.String.clientID) },
            //    { "client_secret", GetString(Resource.String.clientSecret) },
            //    { "redirect_uri", "" },
            //    { "code", account.ServerAuthCode },
            //    { "id_token", account.IdToken },
            //};

            //var items = from kvp in fields
            //            select kvp.Key + "=" + kvp.Value;

            //string content = string.Join("&", items);

            //try
            //{
            //    HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://www.googleapis.com/oauth2/v4/token");
            //    request.Host = "www.googleapis.com";

            //    request.Method = "POST";
            //    request.ContentType = "application/x-www-form-urlencoded";
            //    request.ContentLength = content.Length;

            //    using (StreamWriter writer = new StreamWriter(request.GetRequestStream()))
            //    {
            //        writer.Write(content);
            //    }

            //    Console.WriteLine("&Content: " + content);

            //    HttpWebResponse resp = (HttpWebResponse)await request.GetResponseAsync();

            //    string response;
            //    using (StreamReader responseReader = new StreamReader(request.GetResponse().GetResponseStream()))
            //    {
            //        response = responseReader.ReadToEnd();
            //    }
            //    Console.WriteLine("&Response: " + response);

            //    JToken json = JObject.Parse(response);
            //    GoogleCredential credential = GoogleCredential.FromAccessToken((string)json.SelectToken("access_token"));
            //    YoutubeManager.YoutubeService = new YouTubeService(new BaseClientService.Initializer()
            //    {
            //        HttpClientInitializer = credential,
            //        ApplicationName = "Opus"
            //    });

            //    refreshToken = (string)json.SelectToken("refresh_token");
            //    if (refreshToken != null)
            //    {
            //        ISharedPreferencesEditor editor = prefManager.Edit();
            //        editor.PutString("refresh-token", refreshToken);
            //        editor.Apply();
            //    }

            //    int expireIn = (int)json.SelectToken("expires_in");
            //    NextRefreshDate = DateTime.UtcNow.AddSeconds(expireIn - 30); //Should refresh a bit before the expiration of the acess token
            //}
            //catch (WebException ex)
            //{
            //    Console.WriteLine("&Refresh token get error: " + ex.Message);
            //    CreateYoutube(false);
            //    UnknowError(new Action(() => { CreateYoutube(); }));
            //}
            //}
            //else if (account != null)
            //{
            //    Console.WriteLine("&Getting a new access-token and creating a youtube service");
            //    Dictionary<string, string> fields = new Dictionary<string, string>
            //    {
            //        { "refresh_token", refreshToken },
            //        { "client_id", GetString(Resource.String.clientID) },
            //        { "client_secret", GetString(Resource.String.clientSecret) },
            //        { "grant_type", "refresh_token" },
            //    };

            //    var items = from kvp in fields
            //                select kvp.Key + "=" + kvp.Value;

            //    string content = string.Join("&", items);

            //    try
            //    {
            //        HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://www.googleapis.com/oauth2/v4/token");
            //        request.Host = "www.googleapis.com";

            //        request.Method = "POST";
            //        request.ContentType = "application/x-www-form-urlencoded";
            //        request.ContentLength = content.Length;

            //        using (StreamWriter writer = new StreamWriter(request.GetRequestStream()))
            //        {
            //            writer.Write(content);
            //        }

            //        Console.WriteLine("&Content: " + content);

            //        HttpWebResponse resp = (HttpWebResponse)await request.GetResponseAsync();

            //        string response;
            //        using (StreamReader responseReader = new StreamReader(request.GetResponse().GetResponseStream()))
            //        {
            //            response = responseReader.ReadToEnd();
            //        }
            //        Console.WriteLine("&Response: " + response);

            //        JToken json = JObject.Parse(response);
            //        GoogleCredential credential = GoogleCredential.FromAccessToken((string)json.SelectToken("access_token"));
            //        YoutubeManager.YoutubeService = new YouTubeService(new BaseClientService.Initializer()
            //        {
            //            HttpClientInitializer = credential,
            //            ApplicationName = "Opus"
            //        });

            //        int expireIn = (int)json.SelectToken("expires_in");
            //        NextRefreshDate = DateTime.UtcNow.AddSeconds(expireIn - 30); //Should refresh a bit before the expiration of the acess token
            //    }
            //    catch (WebException ex)
            //    {
            //        Console.WriteLine("&New access token get error: " + ex.Message + " - " + ex.StackTrace);
            //        CreateYoutube(false);
            //        UnknowError(new Action(() => { CreateYoutube(); }));
            //    }
            //}
            //else
            //{
            //    Login(true);
            //}
        }

        public void OnFailure()
        {
            Console.WriteLine("&Failure");
        }

        public void OnConnectionFailed(ConnectionResult result)
        {
            Console.WriteLine("&Connection Failed: " + result.ErrorMessage);
        }

        public async Task<bool> WaitForYoutube(bool silentWait = false)
        {
            if (YoutubeManager.YoutubeService == null)
            {
                if (!waitingForYoutube)
                    Login(!silentWait);

                waitingForYoutube = true;

                if (silentWait)
                {
                    int i = 0;
                    while (true)
                    {
                        await Task.Delay(10);
                        i++;

                        if (YoutubeManager.YoutubeService == null)
                            return true;
                        else if (i > 1000) //10 seconds timout
                            return false;
                    }
                }
                else
                {
                    while (YoutubeManager.YoutubeService == null)
                    {
                        if (waitingForYoutube == false)
                            return false;

                        await Task.Delay(10);
                    }
                }
            }
            else if (NextRefreshDate == null || NextRefreshDate <= DateTime.UtcNow) //Acess token has expired
            {
                waitingForYoutube = true;
                CreateYoutube();
            }
            waitingForYoutube = false;
            return true;
        }
        #endregion

        #region Permission Request
        public bool HasReadPermission()
        {
            if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.ReadExternalStorage) == (int)Permission.Granted)
                return true;
            else
                return false;
        }

        public async Task<bool> GetReadPermission(bool ask = true)
        {
            if (HasReadPermission())
                return true;
            PermissionGot = null;

            if (ask)
            {
                string[] permissions = new string[] { Manifest.Permission.ReadExternalStorage };
                RequestPermissions(permissions, RequestCode);
            }

            while (PermissionGot == null)
                await Task.Delay(10);

            return (bool)PermissionGot;
        }

        public async Task<bool> GetWritePermission()
        {
            const string permission = Manifest.Permission.WriteExternalStorage;
            if (ContextCompat.CheckSelfPermission(this, permission) == (int)Permission.Granted)
            {
                return true;
            }
            PermissionGot = null;
            string[] permissions = new string[] { permission };
            RequestPermissions(permissions, RequestCode);

            while (PermissionGot == null)
                await Task.Delay(10);

            return (bool)PermissionGot;
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            if (requestCode == RequestCode)
            {
                if (grantResults.Length > 0)
                {
                    if (grantResults[0] == Permission.Granted)
                        PermissionGot = true;
                    else
                    {
                        PermissionGot = false;
                        Snackbar snackBar = Snackbar.Make(FindViewById<CoordinatorLayout>(Resource.Id.snackBar), Resource.String.no_permission, Snackbar.LengthLong);
                        snackBar.View.FindViewById<TextView>(Resource.Id.snackbar_text).SetTextColor(Color.White);
                        snackBar.Show();
                    }
                }
            }
            else if (requestCode == WriteRequestCode)
            {
                if (grantResults[0] == Permission.Granted)
                    PermissionGot = true;
                else
                {
                    PermissionGot = false;
                    Snackbar snackBar = Snackbar.Make(FindViewById<CoordinatorLayout>(Resource.Id.snackBar), Resource.String.no_permission, Snackbar.LengthLong);
                    snackBar.View.FindViewById<TextView>(Resource.Id.snackbar_text).SetTextColor(Color.White);
                    snackBar.Show();
                }
            }
        }
        #endregion

        #region Has Wifi
        public static bool HasInternet()
        {
            ConnectivityManager connectivityManager = (ConnectivityManager)Application.Context.GetSystemService(ConnectivityService);
            NetworkInfo activeNetworkInfo = connectivityManager.ActiveNetworkInfo;
            if (activeNetworkInfo == null || !activeNetworkInfo.IsConnected)
                return false;

            return true;
        }

        public bool HasWifi()
        {
            ConnectivityManager connectivityManager = (ConnectivityManager)Application.Context.GetSystemService(ConnectivityService);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            {
                Network network = connectivityManager.ActiveNetwork;
                if (network == null)
                    return false;

                NetworkCapabilities capabilities = connectivityManager.GetNetworkCapabilities(network);
                if (capabilities.HasTransport(TransportType.Wifi) || capabilities.HasTransport(TransportType.Ethernet))
                    return true;
            }
            else
            {
                Network[] allNetworks = connectivityManager.GetAllNetworks();
                for (int i = 0; i < allNetworks.Length; i++)
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    if (allNetworks[i] != null && connectivityManager.GetNetworkInfo(allNetworks[i]).IsConnected && connectivityManager.GetNetworkInfo(allNetworks[i]).Type == ConnectivityType.Wifi)
#pragma warning restore CS0618 // Type or member is obsolete
                        return true;
                }
            }

            return false;
        }
        #endregion

        #region Convert density pixels to screen pixels
        public int DpToPx(int dx)
        {
            float scale = Resources.DisplayMetrics.Density;
            return (int)(dx * scale + 0.5f);
        }
        #endregion

        #region Chromcast session manager
        public void OnSessionEnded(Java.Lang.Object session, int error)
        {
            Console.WriteLine("&Session Ended");
            SwitchRemote(null);
        }

        public void OnSessionEnding(Java.Lang.Object session) { }

        public void OnSessionResumeFailed(Java.Lang.Object session, int error) { }

        public void OnSessionResumed(Java.Lang.Object session, bool wasSuspended)
        {
            Console.WriteLine("&Session Resumed");
            SwitchRemote(((CastSession)session).RemoteMediaClient, false);
        }

        public void OnSessionResuming(Java.Lang.Object session, string sessionId) { }

        public void OnSessionStartFailed(Java.Lang.Object session, int error) { }

        public void OnSessionStarted(Java.Lang.Object session, string sessionId)
        {
            Console.WriteLine("&Session Started");
            SwitchRemote(((CastSession)session).RemoteMediaClient, true);
        }

        public void OnSessionStarting(Java.Lang.Object session) { }

        public void OnSessionSuspended(Java.Lang.Object session, int reason)
        {
            Console.WriteLine("&Session Suspended");
            SwitchRemote(null);
        }

        private async void SwitchRemote(RemoteMediaClient remoteClient, bool justStarted = true)
        {
            Console.WriteLine("&Switching to another remote player: (null check)" + (remoteClient == null));

            MusicPlayer.Initialized = false;
            if (remoteClient != null)
            {
                MusicPlayer.RemotePlayer = remoteClient;

                if (MusicPlayer.CastCallback == null)
                {
                    MusicPlayer.CastCallback = new CastCallback();
                    MusicPlayer.RemotePlayer.RegisterCallback(MusicPlayer.CastCallback);
                }
                if (MusicPlayer.CastQueueManager == null)
                {
                    MusicPlayer.CastQueueManager = new CastQueueManager();
                    MusicPlayer.RemotePlayer.MediaQueue.RegisterCallback(MusicPlayer.CastQueueManager);
                }
            }
            else
            {
                if (MusicPlayer.CastCallback != null)
                {
                    MusicPlayer.RemotePlayer.UnregisterCallback(MusicPlayer.CastCallback);
                    MusicPlayer.CastCallback = null;
                }
                if (MusicPlayer.CastQueueManager != null)
                {
                    MusicPlayer.RemotePlayer.MediaQueue.UnregisterCallback(MusicPlayer.CastQueueManager);
                    MusicPlayer.CastQueueManager = null;
                }

                MusicPlayer.RemotePlayer = remoteClient;
                MusicPlayer.isRunning = false;
                Player.instance.RefreshPlayer();
            }

            MusicPlayer.UseCastPlayer = MusicPlayer.RemotePlayer != null;

            await Task.Delay(1000);
            if (MusicPlayer.UseCastPlayer)
            {
                if (justStarted && MusicPlayer.RemotePlayer.MediaQueue.ItemCount == 0)
                {
                    Intent intent = new Intent(this, typeof(MusicPlayer));
                    intent.SetAction("StartCasting");
                    StartService(intent);
                }
                else
                {
                    MusicPlayer.Initialized = true;
                    MusicPlayer.GetQueueFromCast();
                }
            }
        }
#endregion
    }

    /// <summary>
    /// From where unknow errors are called. For debugging purpose on a deployed version.
    /// </summary>
    public enum ErrorCode
    {
        SP1, //Song parser 1
        SP2, //Song parser 2
        DL1, //Main downloader loop
        SM1, //Song Mix 1
        CG1  //Channel Get 1
    }
}