using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

public partial class Window
{
    internal int OpenPopupCount => _popupManager.Count;

    internal UIElement OpenPopupAt(int index) => _popupManager.ElementAt(index);
}
