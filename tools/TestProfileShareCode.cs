using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace GammaLift
{
    internal static class TestProfileShareCode
    {
        private const string EmptyError = "分享码不能为空。";
        private const string LengthError = "分享码不能超过 64 个字符。";
        private const string FormatError = "分享码格式无效，应为本程序生成的 GL2 分享码。";
        private const string VersionError = "不支持此分享码版本。";
        private const string Base64Error = "分享码数据不是有效的 Base64URL 编码。";
        private const string ChecksumError = "分享码校验失败，请检查是否复制完整。";
        private const string RangeError = "分享码中的参数超出允许范围。";
        private const string IntegerError = "亮度和对比度必须是 -100 到 100 之间的整数。";
        private const string GammaError = "Gamma 必须是 0.30 到 3.00 之间的有限数值，且最多保留两位小数。";
        private const string NullConfigError = "调节配置及 RGB 通道不能为空。";
        private static int _assertions;
        private static int _failures;

        public static int Main()
        {
            CultureInfo culture = Thread.CurrentThread.CurrentCulture;
            CultureInfo uiCulture = Thread.CurrentThread.CurrentUICulture;
            string language = Loc.Code;
            try
            {
                Loc.SetLanguage(Loc.SourceCode);
                Run("round trips and model fidelity", TestRoundTrips);
                Run("canonical length and format", TestCanonical);
                Run("transport validation and limits", TestTransport);
                Run("checksum, flags and ranges", TestCorruption);
                Run("culture independence", TestCultures);
                Run("invalid export settings", TestInvalidEncode);
                Run("missing settings fields fall back to constructor defaults", TestMissingFieldDefaults);
                Run("deterministic malformed input", TestMalformedInputs);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = uiCulture;
                Loc.SetLanguage(language);
            }
            Console.WriteLine("Assertions: " + _assertions + "; failed groups: " + _failures);
            return _failures == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS " + name);
            }
            catch (Exception ex)
            {
                _failures++;
                Console.Error.WriteLine("FAIL " + name + ": " + ex);
            }
        }

        private static void TestRoundTrips()
        {
            RoundTrip(new MonitorConfig());
            foreach (bool linked in new bool[] { true, false })
            {
                foreach (int value in new int[] { -100, 0, 100 })
                {
                    foreach (double gamma in new double[] { 0.30, 1.00, 3.00 })
                        RoundTrip(MakeConfig(linked, value, -value, gamma));
                }
            }

            MonitorConfig independent = MakeConfig(false, -100, 100, 0.30);
            independent.Green = MakeChannel(0, 0, 1.23);
            independent.Blue = MakeChannel(100, -100, 3.00);
            RoundTrip(independent);
            independent.Linked = true;
            RoundTrip(independent);

            for (int hundredths = 30; hundredths <= 300; hundredths++)
                RoundTrip(MakeConfig(false, 0, 0, hundredths / 100.0));

            MonitorConfig linkedDifferent = MakeConfig(true, 10, -20, 1.37);
            linkedDifferent.Blue = MakeChannel(5, 6, 0.85);
            RoundTrip(linkedDifferent);

            MonitorConfig source = MakeConfig(false, 10, -20, 1.37);
            MonitorConfig decoded = Decode(ProfileShareCode.Encode(source));
            Assert(!object.ReferenceEquals(source, decoded), "new monitor instance");
            Assert(!object.ReferenceEquals(source.Red, decoded.Red), "new channel instances");
            Assert(!object.ReferenceEquals(decoded.Red, decoded.Green), "channels are not aliased");
            decoded.Red.Brightness = 99;
            Assert(source.Red.Brightness == 10 && decoded.Green.Brightness == 10, "decoded state is detached");
        }

        private static void TestCanonical()
        {
            string linked = ProfileShareCode.Encode(new MonitorConfig());
            byte[] neutralPayload = MakeBytes(true, 0, 0, 100);
            Assert(linked == MakeCode(neutralPayload, Crc8(neutralPayload, neutralPayload.Length)),
                "canonical compact payload: " + linked + " vs " +
                MakeCode(neutralPayload, Crc8(neutralPayload, neutralPayload.Length)));
            Assert(linked.Length == 11, "linked code is 11 characters");
            Assert(linked.StartsWith("GL2", StringComparison.Ordinal), "GL2 prefix");
            foreach (char c in linked) Assert(c <= 127, "ASCII export");
            foreach (char c in linked) Assert(IsBase64Url(c), "Base64URL alphabet");

            MonitorConfig unlinked = new MonitorConfig();
            unlinked.Linked = false;
            string full = ProfileShareCode.Encode(unlinked);
            Assert(full.Length == 22, "three-channel code is 22 characters");

            MonitorConfig linkedDifferent = MakeConfig(true, 10, -20, 1.37);
            linkedDifferent.Blue = MakeChannel(5, 6, 0.85);
            Assert(ProfileShareCode.Encode(linkedDifferent).Length == 22, "differing linked channels use full payload");
            MonitorConfig decoded = Decode(ProfileShareCode.Encode(linkedDifferent));
            Assert(decoded.Linked, "linked flag preserved with full payload");
            AssertSame(linkedDifferent, decoded);

            PrivateMonitorConfig privateConfig = new PrivateMonitorConfig();
            privateConfig.Name = "secret-profile-name";
            privateConfig.HotKey = 123456;
            privateConfig.Baselines = new System.Collections.Generic.List<int> { 11111, 22222 };
            privateConfig.SettingsPath = "C:\\private\\GammaLiftSettings.json";
            privateConfig.HardwareId = "MONITOR-secret-hardware-id";
            privateConfig.DdcBrightness = 71;
            Assert(ProfileShareCode.Encode(privateConfig) == linked, "only projected software values are exported");

            Assert(ProfileShareCode.Encode(MakeConfig(true, 0, 0, 1.2300000001)) ==
                ProfileShareCode.Encode(MakeConfig(true, 0, 0, 1.23)), "canonicalization of floating-point noise");
        }

        private static void TestTransport()
        {
            string good = ProfileShareCode.Encode(new MonitorConfig());
            AssertSame(new MonitorConfig(), Decode(" \t\r\n\u3000" + good + "\u3000\r\n "));
            Reject(null, EmptyError);
            Reject("", EmptyError);
            Reject(" \r\n\t", EmptyError);
            Reject(new string('A', 65), LengthError);
            Reject("GL2" + new string('A', 62), LengthError);

            foreach (string bad in new string[] { "gl2AAAAAAA", "XL2AAAAAAA", "GL", "G L2AAAAAAA", "\uFF27L2AAAAAAA" })
                Reject(bad, FormatError);
            foreach (string prefix in new string[] { "GL0", "GL1", "GL3", "GL99", "GL01" })
                Reject(prefix + "AAAAAAA", VersionError);

            foreach (string bad in new string[] { "+", "/", "=", "%", ".", "?", "@" })
                Reject("GL2" + good.Substring(3) + bad, Base64Error);
            Reject("GL2A", Base64Error);
            Reject("GL2AAAA", FormatError);
            Reject("GL2" + new string('A', 18), FormatError);
        }

        private static void TestCorruption()
        {
            byte[] payload = MakeBytes(true, 0, 0, 100);
            string code = MakeCode(payload, Crc8(payload, payload.Length));
            AssertSame(new MonitorConfig(), Decode(code));

            byte[] corrupt = (byte[])payload.Clone();
            corrupt[corrupt.Length - 1] ^= 0x01;
            Reject(MakeCode(corrupt, Crc8(payload, payload.Length)), ChecksumError);

            byte[] reserved = (byte[])payload.Clone();
            reserved[0] |= 0x04;
            Reject(MakeCode(reserved, Crc8(reserved, reserved.Length)), FormatError);

            byte[] compactWithoutLinked = (byte[])payload.Clone();
            compactWithoutLinked[0] = 0x02;
            Reject(MakeCode(compactWithoutLinked, Crc8(compactWithoutLinked, compactWithoutLinked.Length)), FormatError);

            byte[] fullWithCompact = new byte[13];
            fullWithCompact[0] = 0x03;
            for (int i = 1; i < fullWithCompact.Length; i++) fullWithCompact[i] = 100;
            Reject(MakeCode(fullWithCompact, Crc8(fullWithCompact, fullWithCompact.Length)), FormatError);

            byte[] linkedNoCompact = new byte[13];
            linkedNoCompact[0] = 0x01;
            for (int i = 1; i < linkedNoCompact.Length; i += 4)
            {
                linkedNoCompact[i] = 100;
                linkedNoCompact[i + 1] = 100;
                linkedNoCompact[i + 2] = 100;
                linkedNoCompact[i + 3] = 0;
            }
            AssertSame(new MonitorConfig(), Decode(MakeCode(linkedNoCompact,
                Crc8(linkedNoCompact, linkedNoCompact.Length))));

            byte[] brightness = (byte[])payload.Clone();
            brightness[1] = 201;
            Reject(MakeCode(brightness, Crc8(brightness, brightness.Length)), RangeError);

            byte[] contrast = (byte[])payload.Clone();
            contrast[2] = 201;
            Reject(MakeCode(contrast, Crc8(contrast, contrast.Length)), RangeError);

            byte[] gammaLow = (byte[])payload.Clone();
            gammaLow[3] = 29;
            gammaLow[4] = 0;
            Reject(MakeCode(gammaLow, Crc8(gammaLow, gammaLow.Length)), RangeError);

            byte[] gammaHigh = (byte[])payload.Clone();
            gammaHigh[3] = 45;
            gammaHigh[4] = 0x01;
            Reject(MakeCode(gammaHigh, Crc8(gammaHigh, gammaHigh.Length)), RangeError);

            byte[] nonCanonical = new byte[14];
            nonCanonical[0] = 0x02;
            for (int i = 1; i < nonCanonical.Length; i++) nonCanonical[i] = 100;
            string canonical = MakeCode(nonCanonical, Crc8(nonCanonical, nonCanonical.Length));
            int lastIndex = Base64Index(canonical[canonical.Length - 1]);
            char changed = Base64Char(lastIndex ^ 0x01);
            Reject(canonical.Substring(0, canonical.Length - 1) + changed, null);
        }

        private static void TestCultures()
        {
            CultureInfo culture = Thread.CurrentThread.CurrentCulture;
            CultureInfo uiCulture = Thread.CurrentThread.CurrentUICulture;
            string language = Loc.Code;
            MonitorConfig config = MakeConfig(false, -23, 45, 1.37);
            string expected = ProfileShareCode.Encode(config);
            try
            {
                foreach (string name in new string[] { "en-US", "de-DE", "tr-TR" })
                {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo(name);
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo(name);
                    Loc.SetLanguage(name.Substring(0, 2));
                    Assert(ProfileShareCode.Encode(config) == expected, "culture-independent code: " + name);
                    AssertSame(config, Decode(expected));
                    Reject(null, EmptyError);
                    Reject("gl2" + expected.Substring(3), FormatError);
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = uiCulture;
                Loc.SetLanguage(language);
            }
        }

        private static void TestInvalidEncode()
        {
            RejectEncode(null, NullConfigError);
            foreach (int index in new int[] { 0, 1, 2 })
            {
                MonitorConfig missing = new MonitorConfig();
                if (index == 0) missing.Red = null;
                if (index == 1) missing.Green = null;
                if (index == 2) missing.Blue = null;
                RejectEncode(missing, NullConfigError);
                foreach (int value in new int[] { -101, 101, int.MinValue, int.MaxValue })
                {
                    MonitorConfig brightness = new MonitorConfig();
                    brightness.GetChannel(index).Brightness = value;
                    RejectEncode(brightness, IntegerError);
                    MonitorConfig contrast = new MonitorConfig();
                    contrast.GetChannel(index).Contrast = value;
                    RejectEncode(contrast, IntegerError);
                }
                foreach (double value in new double[]
                {
                    double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0.0, -0.0,
                    0.29, 3.01, 1.234, 1.2300001, double.MinValue, double.MaxValue
                })
                {
                    MonitorConfig invalid = new MonitorConfig();
                    invalid.GetChannel(index).Gamma = value;
                    RejectEncode(invalid, GammaError);
                }
            }
        }

        private static void TestMissingFieldDefaults()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            AppSettings old = serializer.Deserialize<AppSettings>(
                "{\"Profiles\":[{\"Name\":\"old\",\"HotKey\":0,\"Monitors\":{}}]}");
            Assert(old.NotifyEnabled, "old settings default NotifyEnabled=true");
            Assert(old.NotifyProfileApplied, "old settings default NotifyProfileApplied=true");
            Assert(old.NotifyTrayOnClose, "old settings default NotifyTrayOnClose=true");
            Assert(old.MinimizeToTray, "old settings default MinimizeToTray=true");
            Assert((int)old.CycleHotKeyTrigger == 0, "old cycle trigger defaults to zero");
            Assert(old.Profiles.Count == 1 && (int)old.Profiles[0].HotKeyTrigger == 0,
                "old profile trigger defaults to zero");
            Assert((int)new Profile().HotKeyTrigger == 0 && (int)new AppSettings().CycleHotKeyTrigger == 0,
                "new trigger constructor defaults");
            AppSettings explicitFalse = serializer.Deserialize<AppSettings>(
                "{\"NotifyEnabled\":false,\"NotifyProfileApplied\":false,\"NotifyTrayOnClose\":false,\"MinimizeToTray\":false}");
            Assert(!explicitFalse.NotifyEnabled && !explicitFalse.NotifyProfileApplied && !explicitFalse.NotifyTrayOnClose,
                "explicit notification settings remain false");
            Assert(!explicitFalse.MinimizeToTray, "explicit MinimizeToTray=false remains false");
        }

        private static void TestMalformedInputs()
        {
            Random random = new Random(71023);
            for (int sample = 0; sample < 300; sample++)
            {
                char[] chars = new char[random.Next(1, 160)];
                for (int i = 0; i < chars.Length; i++) chars[i] = (char)random.Next(0, 128);
                Reject(new string(chars), null);

                byte[] payload = new byte[13];
                random.NextBytes(payload);
                payload[0] = 0x00;
                byte[] withBadCrc = new byte[payload.Length + 1];
                Array.Copy(payload, withBadCrc, payload.Length);
                withBadCrc[withBadCrc.Length - 1] = (byte)(Crc8(payload, payload.Length) ^ 0xFF);
                Reject(MakeCodeRaw(withBadCrc), ChecksumError);
            }
        }

        private static void RoundTrip(MonitorConfig config)
        {
            string code = ProfileShareCode.Encode(config);
            MonitorConfig decoded = Decode(code);
            AssertSame(config, decoded);
            Assert(ProfileShareCode.Encode(decoded) == code, "stable re-encoding");
        }

        private static MonitorConfig Decode(string code)
        {
            MonitorConfig config;
            string error;
            Assert(ProfileShareCode.TryDecode(code, out config, out error), "decode success: " + error);
            Assert(config != null && error == null, "successful decode output contract");
            return config;
        }

        private static void Reject(string code, string expectedError)
        {
            MonitorConfig config = new MonitorConfig();
            string error = "stale";
            bool success = ProfileShareCode.TryDecode(code, out config, out error);
            Assert(!success, "invalid input rejected: " + (code == null ? "<null>" :
                (code.Length > 40 ? code.Substring(0, 40) : code)));
            Assert(config == null, "failed decode clears config");
            Assert(!string.IsNullOrEmpty(error), "failed decode has a specific error");
            if (expectedError != null)
                Assert(error == Loc.T(expectedError), "error: expected " + Loc.T(expectedError) + ", got " + error);
        }

        private static void RejectEncode(MonitorConfig config, string expectedError)
        {
            try
            {
                ProfileShareCode.Encode(config);
            }
            catch (ArgumentException ex)
            {
                Assert(ex.Message == Loc.T(expectedError), "localized, value-free export error");
                return;
            }
            throw new Exception("invalid export must throw ArgumentException");
        }

        private static void AssertSame(MonitorConfig expected, MonitorConfig actual)
        {
            Assert(expected.Linked == actual.Linked, "linked flag preserved");
            for (int i = 0; i < 3; i++)
            {
                ChannelSettings left = expected.GetChannel(i);
                ChannelSettings right = actual.GetChannel(i);
                Assert(left.Brightness == right.Brightness, "brightness preserved " + i + ": " +
                    left.Brightness + " vs " + right.Brightness);
                Assert(left.Contrast == right.Contrast, "contrast preserved");
                Assert(Math.Abs(left.Gamma - right.Gamma) <= 0.000000001, "gamma preserved");
            }
        }

        private static void Assert(bool condition, string message)
        {
            _assertions++;
            if (!condition) throw new Exception(message);
        }

        private static MonitorConfig MakeConfig(bool linked, int brightness, int contrast, double gamma)
        {
            MonitorConfig config = new MonitorConfig();
            config.Linked = linked;
            config.Red = MakeChannel(brightness, contrast, gamma);
            config.Green = MakeChannel(brightness, contrast, gamma);
            config.Blue = MakeChannel(brightness, contrast, gamma);
            return config;
        }

        private static ChannelSettings MakeChannel(int brightness, int contrast, double gamma)
        {
            ChannelSettings channel = new ChannelSettings();
            channel.Brightness = brightness;
            channel.Contrast = contrast;
            channel.Gamma = gamma;
            return channel;
        }

        private static byte[] MakeBytes(bool linked, int brightness, int contrast, int gammaHundredths)
        {
            byte[] payload = new byte[5];
            payload[0] = (byte)(linked ? 0x03 : 0x00);
            payload[1] = (byte)(brightness + 100);
            payload[2] = (byte)(contrast + 100);
            payload[3] = (byte)(gammaHundredths & 0xFF);
            payload[4] = (byte)((gammaHundredths >> 8) & 0xFF);
            return payload;
        }

        private static string MakeCode(byte[] payload, byte checksum)
        {
            byte[] bytes = new byte[payload.Length + 1];
            Array.Copy(payload, bytes, payload.Length);
            bytes[bytes.Length - 1] = checksum;
            return MakeCodeRaw(bytes);
        }

        private static string MakeCodeRaw(byte[] bytes)
        {
            return "GL2" + ToBase64Url(bytes);
        }

        private static string ToBase64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte Crc8(byte[] data, int count)
        {
            byte crc = 0;
            for (int i = 0; i < count; i++)
            {
                crc ^= data[i];
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((crc & 0x80) != 0) crc = (byte)((crc << 1) ^ 0x07);
                    else crc = (byte)(crc << 1);
                }
            }
            return crc;
        }

        private static bool IsBase64Url(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                (c >= '0' && c <= '9') || c == '-' || c == '_';
        }

        private static int Base64Index(char c)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
            int index = alphabet.IndexOf(c);
            if (index < 0) throw new Exception("not a Base64URL character: " + c);
            return index;
        }

        private static char Base64Char(int index)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
            return alphabet[index & 63];
        }

        private sealed class PrivateMonitorConfig : MonitorConfig
        {
            public string Name { get; set; }
            public int HotKey { get; set; }
            public System.Collections.Generic.List<int> Baselines { get; set; }
            public string SettingsPath { get; set; }
            public string HardwareId { get; set; }
            public int DdcBrightness { get; set; }
        }
    }
}
