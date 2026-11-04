using CommunityToolkit.Mvvm.ComponentModel;
using Keystream.Core.Localization;

namespace Keystream.App.ViewModels;

/// <summary>§124 - rename-one-card dialog model. Same shape as
/// PageEditViewModel (Name + Accepted + Title); the window is the
/// small PageEditWindow pattern, NOT the full binding editor - rename
/// must never open the editor modal.</summary>
public partial class BindingRenameViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    public bool Accepted { get; set; }
    public string Title => Localizer.Get("S1643");
}
