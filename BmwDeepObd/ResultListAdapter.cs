using System.Collections.Generic;
using Android.App;
using Android.Views;
using Android.Widget;

namespace BmwDeepObd
{
    public class ResultListAdapter : BaseAdapter<TableResultItem>
    {
        private readonly List<TableResultItem> _items;
        public List<TableResultItem> Items => _items;
        private readonly Activity _context;
        private readonly float _textWeight;
        private readonly bool _showCheckBox;
        private bool _ignoreCheckEvent;

        public ResultListAdapter(Activity context, float textWeight, bool showCheckBox)
        {
            _context = context;
            _items = new List<TableResultItem> ();
            _textWeight = textWeight;
            _showCheckBox = showCheckBox;
        }

        public ResultListAdapter(Activity context, float textWeight)
            : this(context, textWeight, false)
        {
        }

        public ResultListAdapter(Activity context)
            : this(context, -1, false)
        {
        }

        public override long GetItemId(int position)
        {
            return position;
        }

        public override TableResultItem this[int position] => _items[position];

        public override int Count => _items.Count;

        public override bool IsEnabled(int position)
        {
            return false;
        }

        public override View GetView(int position, View convertView, ViewGroup parent)
        {
            var item = _items[position];

            View view = convertView ?? _context.LayoutInflater.Inflate(Resource.Layout.result_list, null);
            CheckBox checkBoxSelect = view.FindViewById<CheckBox>(Resource.Id.checkBoxResultSelect);

            if (_showCheckBox)
            {
                checkBoxSelect.Visibility = item.CheckVisible ? ViewStates.Visible : ViewStates.Invisible;
            }
            else
            {
                checkBoxSelect.Visibility = ViewStates.Gone;
            }
            _ignoreCheckEvent = true;
            checkBoxSelect.Checked = item.Selected;
            _ignoreCheckEvent = false;

            checkBoxSelect.Tag = new TagInfo(item);
            checkBoxSelect.CheckedChange -= OnCheckChanged;
            checkBoxSelect.CheckedChange += OnCheckChanged;

            TextView textView1 = view.FindViewById<TextView>(Resource.Id.ListText1);
            TextView textView2 = view.FindViewById<TextView>(Resource.Id.ListText2);
            textView1.Text = item.Text1;
            if (item.Text2 == null)
            {
                textView2.Visibility = ViewStates.Gone;
            }
            else
            {
                textView2.Visibility = ViewStates.Visible;
                textView2.Text = item.Text2;
                if (_textWeight >= 0)
                {
                    LinearLayout.LayoutParams layoutPar = (LinearLayout.LayoutParams)textView2.LayoutParameters;
                    layoutPar.Weight = _textWeight;
                    textView2.LayoutParameters = layoutPar;
                }
            }

            return view;
        }

        private void OnCheckChanged(object sender, CompoundButton.CheckedChangeEventArgs args)
        {
            if (!_ignoreCheckEvent)
            {
                CheckBox checkBox = (CheckBox)sender;
                TagInfo tagInfo = (TagInfo)checkBox.Tag;
                if (tagInfo.Info.Selected != args.IsChecked)
                {
                    tagInfo.Info.Selected = args.IsChecked;
                    NotifyDataSetChanged();
                }
            }
        }

        private class TagInfo : Java.Lang.Object
        {
            public TagInfo(TableResultItem info)
            {
                Info = info;
            }

            public TableResultItem Info { get; }
        }
    }

    public class TableResultItem
    {
        private bool _selected;
        public delegate void CheckChangeEventHandler(TableResultItem item);
        public event CheckChangeEventHandler CheckChangeEvent;

        public TableResultItem(string text1, string text2, object tag, bool checkVisible, bool selected)
        {
            Text1 = text1;
            Text2 = text2;
            Tag = tag;
            CheckVisible = checkVisible;
            _selected = selected;
        }

        public TableResultItem(string text1, string text2, object tag)
            : this(text1, text2, tag, false, false)
        {
        }

        public TableResultItem(string text1, string text2)
            : this(text1, text2, null, false, false)
        {
        }

        public string Text1 { get; }

        public string Text2 { get; }

        public object Tag { get; }

        public bool CheckVisible { get; }

        public bool Selected
        {
            get { return _selected; }
            set
            {
                bool changed = _selected != value && CheckChangeEvent != null;
                _selected = value;
                if (changed)
                {
                    CheckChangeEvent(this);
                }
            }
        }
    }
}
