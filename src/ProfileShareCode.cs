using System;
using System.Text;

namespace GammaLift
{
    /// <summary>单屏参数分享码：GL2 + Base64Url(flags + RGB 数值 + CRC-8)。
    /// 联动且三通道相同时只存一个通道, 整码 11 字符; 否则存三通道, 22 字符。
    /// 只投影单台显示器的亮度 / 对比度 / Gamma 与联动状态, 不含名称、热键、硬件 ID 或基线。</summary>
    internal static class ProfileShareCode
    {
        private const string Prefix = "GL2";
        private const int MaxCodeLength = 64;
        private const byte FlagLinked = 0x01;
        private const byte FlagCompact = 0x02;
        private const int CompactPayloadLength = 5;    // flags + 一个通道
        private const int FullPayloadLength = 13;      // flags + 三个通道
        private const int CompactBytes = CompactPayloadLength + 1;
        private const int FullBytes = FullPayloadLength + 1;

        public static string Encode(MonitorConfig config)
        {
            if (config == null || config.Red == null || config.Green == null || config.Blue == null)
                throw new ArgumentException(Loc.T("调节配置及 RGB 通道不能为空。"));

            ChannelSettings[] channels = { config.Red, config.Green, config.Blue };
            for (int i = 0; i < channels.Length; i++)
                ValidateChannel(channels[i]);

            bool compact = config.Linked && ChannelEquals(channels[0], channels[1]) &&
                ChannelEquals(channels[0], channels[2]);
            byte[] payload = new byte[compact ? CompactPayloadLength : FullPayloadLength];
            payload[0] = (byte)((config.Linked ? FlagLinked : 0) | (compact ? FlagCompact : 0));

            int offset = 1;
            offset = WriteChannel(payload, offset, channels[0]);
            if (!compact)
            {
                offset = WriteChannel(payload, offset, channels[1]);
                offset = WriteChannel(payload, offset, channels[2]);
            }

            byte[] bytes = new byte[payload.Length + 1];
            Array.Copy(payload, bytes, payload.Length);
            bytes[bytes.Length - 1] = Crc8(payload);
            return Prefix + ToBase64Url(bytes);
        }

        public static bool TryDecode(string code, out MonitorConfig config, out string error)
        {
            config = null;
            error = null;
            if (code == null)
                return Fail("分享码不能为空。", out error);

            code = code.Trim();
            if (code.Length == 0)
                return Fail("分享码不能为空。", out error);
            if (code.Length > MaxCodeLength)
                return Fail("分享码不能超过 64 个字符。", out error);
            for (int i = 0; i < code.Length; i++)
            {
                if (code[i] > 127)
                    return Fail("分享码格式无效，应为本程序生成的 GL2 分享码。", out error);
            }

            if (!code.StartsWith(Prefix, StringComparison.Ordinal))
                return Fail(IsVersionPrefix(code) ? "不支持此分享码版本。" :
                    "分享码格式无效，应为本程序生成的 GL2 分享码。", out error);

            string body = code.Substring(Prefix.Length);
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') || c == '-' || c == '_'))
                    return Fail("分享码数据不是有效的 Base64URL 编码。", out error);
            }
            if (body.Length % 4 == 1)
                return Fail("分享码数据不是有效的 Base64URL 编码。", out error);

            byte[] bytes;
            try
            {
                string padded = body.Replace('-', '+').Replace('_', '/');
                padded += new string('=', (4 - padded.Length % 4) % 4);
                bytes = Convert.FromBase64String(padded);
            }
            catch (FormatException)
            {
                return Fail("分享码数据不是有效的 Base64URL 编码。", out error);
            }
            if (!string.Equals(ToBase64Url(bytes), body, StringComparison.Ordinal))
                return Fail("分享码数据不是有效的 Base64URL 编码。", out error);

            if (bytes.Length != CompactBytes && bytes.Length != FullBytes)
                return Fail("分享码格式无效，应为本程序生成的 GL2 分享码。", out error);

            byte flags = bytes[0];
            if ((flags & ~(FlagLinked | FlagCompact)) != 0)
                return Fail("分享码格式无效，应为本程序生成的 GL2 分享码。", out error);

            bool linked = (flags & FlagLinked) != 0;
            bool compact = (flags & FlagCompact) != 0;
            if (compact && (!linked || bytes.Length != CompactBytes))
                return Fail("分享码格式无效，应为本程序生成的 GL2 分享码。", out error);
            if (!compact && bytes.Length != FullBytes)
                return Fail("分享码格式无效，应为本程序生成的 GL2 分享码。", out error);

            if (bytes[bytes.Length - 1] != Crc8(bytes, bytes.Length - 1))
                return Fail("分享码校验失败，请检查是否复制完整。", out error);

            int offset = 1;
            ChannelSettings red;
            if (!ReadChannel(bytes, ref offset, out red, out error))
                return false;

            MonitorConfig decoded = new MonitorConfig();
            decoded.Linked = linked;
            decoded.Red = red;
            if (compact)
            {
                decoded.Green = red.Clone();
                decoded.Blue = red.Clone();
            }
            else
            {
                ChannelSettings green;
                ChannelSettings blue;
                if (!ReadChannel(bytes, ref offset, out green, out error) ||
                    !ReadChannel(bytes, ref offset, out blue, out error))
                    return false;
                decoded.Green = green;
                decoded.Blue = blue;
            }

            if (offset != bytes.Length - 1)
                return Fail("分享码格式无效，应为本程序生成的 GL2 分享码。", out error);

            config = decoded;
            return true;
        }

        private static void ValidateChannel(ChannelSettings channel)
        {
            if (channel.Brightness < -100 || channel.Brightness > 100 ||
                channel.Contrast < -100 || channel.Contrast > 100)
                throw new ArgumentException(Loc.T("亮度和对比度必须是 -100 到 100 之间的整数。"));
            if (!IsValidGamma(channel.Gamma))
                throw new ArgumentException(Loc.T("Gamma 必须是 0.30 到 3.00 之间的有限数值，且最多保留两位小数。"));
        }

        private static bool ChannelEquals(ChannelSettings a, ChannelSettings b)
        {
            return a.Brightness == b.Brightness && a.Contrast == b.Contrast &&
                Math.Abs(a.Gamma - b.Gamma) < 0.001;
        }

        private static int WriteChannel(byte[] payload, int offset, ChannelSettings channel)
        {
            payload[offset++] = (byte)(channel.Brightness + 100);
            payload[offset++] = (byte)(channel.Contrast + 100);
            int hundredths = (int)Math.Round(channel.Gamma * 100.0, MidpointRounding.AwayFromZero);
            payload[offset++] = (byte)(hundredths & 0xFF);
            payload[offset++] = (byte)((hundredths >> 8) & 0xFF);
            return offset;
        }

        private static bool ReadChannel(byte[] bytes, ref int offset, out ChannelSettings channel,
            out string error)
        {
            channel = null;
            error = null;
            if (offset + 4 > bytes.Length - 1)
                return Fail("分享码格式无效，应为本程序生成的 GL2 分享码。", out error);

            int brightness = bytes[offset] - 100;
            int contrast = bytes[offset + 1] - 100;
            int gammaHundredths = bytes[offset + 2] | (bytes[offset + 3] << 8);
            offset += 4;

            if (brightness > 100 || contrast > 100 || gammaHundredths < 30 || gammaHundredths > 300)
                return Fail("分享码中的参数超出允许范围。", out error);

            channel = new ChannelSettings();
            channel.Brightness = brightness;
            channel.Contrast = contrast;
            channel.Gamma = gammaHundredths / 100.0;
            return true;
        }

        private static bool IsVersionPrefix(string code)
        {
            if (!code.StartsWith("GL", StringComparison.Ordinal) || code.Length <= 2)
                return false;
            if (code[2] < '0' || code[2] > '9')
                return false;
            return true;
        }

        private static bool IsValidGamma(double gamma)
        {
            return !double.IsNaN(gamma) && !double.IsInfinity(gamma) && gamma >= 0.30 && gamma <= 3.00 &&
                Math.Abs(gamma - Math.Round(gamma, 2, MidpointRounding.AwayFromZero)) <= 0.000000001;
        }

        private static string ToBase64Url(byte[] payload)
        {
            return Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte Crc8(byte[] data)
        {
            return Crc8(data, data.Length);
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

        private static bool Fail(string source, out string error)
        {
            error = Loc.T(source);
            return false;
        }
    }
}
