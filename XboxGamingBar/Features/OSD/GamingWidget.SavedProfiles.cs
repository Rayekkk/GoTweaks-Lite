using System;
using System.Collections.Generic;
using Windows.UI.Xaml;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private void ProfileSaveCategoriesExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isProfileSaveCategoriesExpanded = !isProfileSaveCategoriesExpanded;
            if (ProfileSaveCategoriesContent != null)
                ProfileSaveCategoriesContent.Visibility = isProfileSaveCategoriesExpanded ? Visibility.Visible : Visibility.Collapsed;
            if (ProfileSaveCategoriesExpandIcon != null)
                ProfileSaveCategoriesExpandIcon.Glyph = isProfileSaveCategoriesExpanded ? ((char)0xE70E).ToString() : ((char)0xE70D).ToString();
        }

        private void SavedProfilesExpandToggle_Click(object sender, RoutedEventArgs e)
        {
            isSavedProfilesExpanded = !isSavedProfilesExpanded;
            if (SavedProfilesContent != null)
                SavedProfilesContent.Visibility = isSavedProfilesExpanded ? Visibility.Visible : Visibility.Collapsed;
            if (SavedProfilesExpandIcon != null)
                SavedProfilesExpandIcon.Glyph = isSavedProfilesExpanded ? "\uE70E" : "\uE70D";
            if (isSavedProfilesExpanded)
                RefreshSavedProfilesList();
        }

        // Controller configuration is part of helper-owned game profiles, not a
        // separately persisted widget collection.
        private void RefreshSavedProfilesList()
        {
            SavedProfilesList.ItemsSource = new List<SavedProfileInfo>();
            NoSavedProfilesText.Visibility = Visibility.Visible;
        }

        private void DeleteSavedProfile_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info("Ignoring obsolete local controller-profile delete action");
        }
    }
}
