using Shared.Enums;
using Shared.IPC;
using Xunit;

namespace Shared.Tests
{
    /// <summary>
    /// Locks down the hand-rolled JSON serializer/parser used for all widget↔helper
    /// IPC. This is the fragile part of the wire layer (regex parsing), so the
    /// round-trip and edge cases are the highest-value thing to pin.
    /// </summary>
    public class PipeMessageTests
    {
        [Fact]
        public void RoundTrip_PreservesCoreFields()
        {
            var msg = new PipeMessage
            {
                RequestId = 7,
                Command = Command.Set,
                Function = Function.TDP,
                Content = "15",
            };

            var parsed = PipeMessage.FromJson(msg.ToJson());

            Assert.Equal(7, parsed.RequestId);
            Assert.Equal(Command.Set, parsed.Command);
            Assert.Equal(Function.TDP, parsed.Function);
            Assert.Equal("15", parsed.Content);
        }

        [Fact]
        public void RoundTrip_PreservesExtraStringAndNumber()
        {
            var msg = new PipeMessage { Command = Command.Get, Function = Function.OSD };
            msg.Extra["Foo"] = "bar";
            msg.Extra["Num"] = 42;

            var parsed = PipeMessage.FromJson(msg.ToJson());

            Assert.Equal("bar", parsed.Extra["Foo"]);
            // Unquoted integers come back as long (parser prefers long for big values like Ticks).
            Assert.Equal(42L, System.Convert.ToInt64(parsed.Extra["Num"]));
        }

        [Fact]
        public void FromJson_UnquotedNumericContent_ReturnsStringDigits()
        {
            var parsed = PipeMessage.FromJson("{\"RequestId\":0,\"Command\":1,\"Function\":2,\"Content\":42}");
            Assert.Equal("42", parsed.Content);
        }

        [Fact]
        public void FromJson_BooleanContent_NormalizesToDotNetBoolString()
        {
            // .NET bool.Parse compatibility: helper stores "True"/"False".
            Assert.Equal("True", PipeMessage.FromJson("{\"Command\":1,\"Function\":2,\"Content\":true}").Content);
            Assert.Equal("False", PipeMessage.FromJson("{\"Command\":1,\"Function\":2,\"Content\":false}").Content);
        }

        [Fact]
        public void RoundTrip_EscapesQuotesAndNewlines()
        {
            var msg = new PipeMessage
            {
                Command = Command.Set,
                Function = Function.OSDConfig,
                Content = "a\"b\nc\\d",
            };

            var parsed = PipeMessage.FromJson(msg.ToJson());

            Assert.Equal("a\"b\nc\\d", parsed.Content);
        }

        [Fact]
        public void FromJson_Malformed_ReturnsEmptyMessageWithoutThrowing()
        {
            var parsed = PipeMessage.FromJson("this is not json");

            Assert.Equal(Command.Get, parsed.Command);     // enum default = 0
            Assert.Equal(Function.None, parsed.Function);  // enum default = 0
            Assert.Null(parsed.Content);
        }

        [Fact]
        public void ToJson_OmitsContentWhenNull()
        {
            var json = new PipeMessage { Command = Command.Get, Function = Function.TDP }.ToJson();
            Assert.DoesNotContain("\"Content\"", json);
        }
    }
}
