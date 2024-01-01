using System;
using MahApps.Metro.IconPacks;
using PropertyChanged;
using tterm.Terminal;

namespace tterm.Ui.Models
{
    [ImplementPropertyChanged]
    internal class TabDataItem
    {
        public string Title { get; set; }
        public PackIconMaterialKind Image { get; set; }

        public event EventHandler Click;

        public bool IsImage => (Title == null);

        public void RaiseClickEvent()
        {
            Click?.Invoke(this, EventArgs.Empty);
        }
    }
}
