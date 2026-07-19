using NLog;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string gameName)
                DeleteGameProfile(gameName);
        }

        private void DeleteActiveProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (HasValidGame(currentGameName))
                DeleteGameProfile(currentGameName);
        }

        private void DeleteGameProfile(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName)) return;

            deleteGameProfile?.ForceSetValue(gameName);
            helperProfileCatalog.Remove($"Game_{gameName}");
            helperProfileCatalog.Remove($"Game_{gameName}_AC");
            helperProfileCatalog.Remove($"Game_{gameName}_DC");
            helperProfileCatalogPaths.Remove(gameName);
            Logger.Info($"Requested helper deletion of game profile '{gameName}'");
            UpdateAllGameProfilesDisplay();
        }
    }
}
