using System;
using System.ComponentModel;
using MahApps.Metro.IconPacks;
using tterm.Terminal;

namespace tterm.Ui.Models
{
    internal class TabDataItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public string Title { get; set; }
        public PackIconMaterialKind Image { get; set; }

        public event EventHandler Click;

        public bool IsImage => (Title == null);
        public bool IsActive { get; set; }
        public bool IsDisabled { get; set; }

        public void RaiseClickEvent()
        {
            Click?.Invoke(this, EventArgs.Empty);
        }
    }
}
