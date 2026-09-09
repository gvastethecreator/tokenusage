using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace TokenUsage.App.Controls;

public sealed class ThemeLogoButton : Button
{
    public ThemeLogoButton()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }
}
