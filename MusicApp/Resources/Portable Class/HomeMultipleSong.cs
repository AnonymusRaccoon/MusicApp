﻿using Android.Support.V7.Widget;
using Android.Views;
using Android.Widget;
using System;

namespace MusicApp.Resources.Portable_Class
{
    public class HomeMultipleSong : RecyclerView.ViewHolder
    {
        public TextView Title;
        public TextView Artist;
        public ImageView AlbumArt;
        public ImageView more;

        public HomeMultipleSong(View itemView, Action<int> listener) : base(itemView)
        {
            Title = itemView.FindViewById<TextView>(Resource.Id.title);
            Artist = itemView.FindViewById<TextView>(Resource.Id.artist);
            AlbumArt = itemView.FindViewById<ImageView>(Resource.Id.albumArt);
            more = itemView.FindViewById<ImageView>(Resource.Id.moreButton);

            itemView.Click += (sender, e) => listener(AdapterPosition);
        }
    }
}