using Shared.Data;
using Xunit;

namespace Shared.Tests
{
    /// <summary>
    /// The validation rules for a custom quick-action definition, extracted from the helper's
    /// Program.HotkeyHandlers.cs SetCustomQuickSetting so they can be tested without a
    /// WinRT/helper host.
    /// </summary>
    public class CustomQuickSettingValidatorTests
    {
        [Fact]
        public void Add_WithValidFields_IsAccepted()
        {
            Assert.True(CustomQuickSettingValidator.Validate("tile1", "My Shortcut", "Ctrl+Alt+X", delete: false, out var reason));
            Assert.Null(reason);
        }

        [Fact]
        public void Add_MissingName_IsRejected()
        {
            Assert.False(CustomQuickSettingValidator.Validate("tile1", "", "Ctrl+Alt+X", delete: false, out var reason));
            Assert.NotNull(reason);
        }

        [Fact]
        public void Add_MissingShortcut_IsRejected()
        {
            Assert.False(CustomQuickSettingValidator.Validate("tile1", "My Shortcut", "", delete: false, out var reason));
            Assert.NotNull(reason);
        }

        [Fact]
        public void Add_NameTooLong_IsRejected()
        {
            string longName = new string('a', CustomQuickSettingValidator.MaxNameLength + 1);
            Assert.False(CustomQuickSettingValidator.Validate("tile1", longName, "Ctrl+Alt+X", delete: false, out var reason));
            Assert.NotNull(reason);
        }

        [Fact]
        public void Add_ShortcutTooLong_IsRejected()
        {
            string longShortcut = new string('a', CustomQuickSettingValidator.MaxShortcutLength + 1);
            Assert.False(CustomQuickSettingValidator.Validate("tile1", "My Shortcut", longShortcut, delete: false, out var reason));
            Assert.NotNull(reason);
        }

        [Fact]
        public void Add_MissingId_IsRejected()
        {
            Assert.False(CustomQuickSettingValidator.Validate("", "My Shortcut", "Ctrl+Alt+X", delete: false, out var reason));
            Assert.NotNull(reason);
        }

        [Fact]
        public void Add_IdTooLong_IsRejected()
        {
            string longId = new string('a', CustomQuickSettingValidator.MaxIdLength + 1);
            Assert.False(CustomQuickSettingValidator.Validate(longId, "My Shortcut", "Ctrl+Alt+X", delete: false, out var reason));
            Assert.NotNull(reason);
        }

        [Fact]
        public void Delete_WithOnlyId_IsAccepted()
        {
            // Delete requests carry no name/shortcut - only the id needs to be valid.
            Assert.True(CustomQuickSettingValidator.Validate("tile1", null, null, delete: true, out var reason));
            Assert.Null(reason);
        }

        [Fact]
        public void Delete_MissingId_IsRejected()
        {
            Assert.False(CustomQuickSettingValidator.Validate(null, null, null, delete: true, out var reason));
            Assert.NotNull(reason);
        }
    }
}
